"""Reader and writer for MOVE animation-graph files (movemgr.bin, dlc*.bin).

One set of layout functions drives both directions: RCtx records each primitive it
parses, WCtx replays the recorded values while emitting bytes. Because the writer
returns the same values the reader saw, version gates and list terminators take the
same branches without being special-cased.

Back-references are resolved to objects on read and renumbered on write, so a
byte-identical round trip also proves the registration-order model.

Field names are the engine's own, taken from the debug string every Transfer call
passes; they carry no bytes and exist so move_xml can name what it emits.
Format notes: docs/docs/file-formats/move.md
"""
import collections
import struct
import sys

TAG = 0x3ADE68B1
F_NAMED, F_GRAPH = 0x20000, 0x40000

CLASSES = {
    0x4D764D67: "CMoveMgr", 0x4D76534D: "CMoveStateMachine", 0x4D765643: "CMoveValueContainer",
    0x4D765664: "CMoveValueDef", 0x4D764253: "CMoveBaseState", 0x4D765354: "CMoveState",
    0x4D765379: "CSyncState", 0x4D76444E: "CDoNothing", 0x4D764466: "CMoveDefinition",
    0x4D764772: "CMoveGroup", 0x4D436D74: "CMoveComment", 0x4D537452: "CMoveStateRef",
    0x4C537452: "CLayeredStateRef", 0x4C795354: "CLayeredState", 0x4C794178: "CLayeredAxialBlend",
    0x4C795061: "CLayeredParameter", 0x506C4D53: "CPlayerMoveState",
    0x466B5354: "CFrankensteinState", 0x466B5061: "CFrankensteinParameter",
    0x42534147: "CAxialBlendAnimGroup", 0x41416E63: "CAnimTechAnchor",
    0x41744174: "CAnimTechAttach", 0x4174494B: "CAnimTechIKPath",
    0x4174506F: "CAnimTechPossession", 0x41526167: "CAnimTechRagdoll",
    0x416E5061: "CMoveDefParameter", 0x53794465: "CSyncDefinition",
    0x53795061: "CSyncDefParameter", 0x54434C70: "CTimeControlledLayeredParameter",
    0x54434D70: "CTimeControlledMoveParameter", 0x4E494C73: "CNotInterruptibleLink",
    0x544C4173: "CTransitionLink", 0x4D454944: "CMoveCriteriaEntityIDEqual",
    0x43494E45: "CMoveCriteriaEntityIDNotEqual", 0x4D434545: "CMoveCriteriaEnumEqual",
    0x43454E45: "CMoveCriteriaEnumNotEqual", 0x4D455543: "TMoveCriteriaEqual<uint8>",
    0x4D4E4543: "TMoveCriteriaNotEqual<uint8>", 0x4D634549: "TMoveCriteriaEqual<int>",
    0x4D4E4549: "TMoveCriteriaNotEqual<int>", 0x4D634542: "TMoveCriteriaEqual<bool>",
    0x4D4E4542: "TMoveCriteriaNotEqual<bool>", 0x4D634949: "TMoveCriteriaIntv<int>",
    0x4D634946: "TMoveCriteriaIntv<float>", 0x4D634941: "TMoveCriteriaIntv<CAngle>",
    0x4D635049: "TMoveCriteriaPerc<int>", 0x4D635046: "TMoveCriteriaPerc<float>",
}
CLSID = {v: k for k, v in CLASSES.items()}


class Drift(Exception):
    pass


class Obj(object):
    """One serialized object: its class and the ordered, named primitives it holds."""

    __slots__ = ("cls", "ops", "index")

    def __init__(self, cls):
        self.cls = cls
        self.ops = []
        self.index = -1

    def __repr__(self):
        return "<%s #%d>" % (self.cls, self.index)


class RCtx(object):
    def __init__(self, b, flags):
        self.b = b
        self.o = 0
        self.flags = flags
        self.cur = None
        self.seq = []

    def _need(self, n):
        if self.o + n > len(self.b):
            raise Drift("EOF at 0x%x (+%d)" % (self.o, n))

    def _rec(self, kind, name, value):
        self.cur.ops.append((kind, name, value))
        return value

    def _blob(self, kind, name):
        self._need(4)
        n = struct.unpack_from("<I", self.b, self.o)[0]
        self.o += 4
        if n > 0x10000:
            raise Drift("absurd %s length %d at 0x%x" % (kind, n, self.o - 4))
        self._need(n)
        v = self.b[self.o:self.o + n]
        self.o += n
        return self._rec(kind, name, v)

    def u8(self, name):
        self._need(1)
        v = self.b[self.o]
        self.o += 1
        return self._rec("u8", name, v)

    def u32(self, name):
        self._need(4)
        v = struct.unpack_from("<I", self.b, self.o)[0]
        self.o += 4
        return self._rec("u32", name, v)

    def s32(self, name):
        self._need(4)
        v = struct.unpack_from("<i", self.b, self.o)[0]
        self.o += 4
        return self._rec("s32", name, v)

    # floats stay as raw bytes so NaN and -0.0 survive a round trip
    def f32(self, name):
        self._need(4)
        v = self.b[self.o:self.o + 4]
        self.o += 4
        return self._rec("f32", name, v)

    def string(self, name):
        return self._blob("str", name)

    def data(self, name):
        return self._blob("data", name)

    def raw(self, name, n):
        self._need(n)
        v = self.b[self.o:self.o + n]
        self.o += n
        return self._rec("raw", name, v)

    def ver(self, name):
        if self.o + 4 <= len(self.b) and struct.unpack_from("<I", self.b, self.o)[0] == TAG:
            self.o += 4
            self._need(4)
            v = struct.unpack_from("<I", self.b, self.o)[0]
            self.o += 4
            self._rec("ver", name, v)
            return v
        self._rec("nover", name, 0)
        return 0

    def ptr(self, name):
        at = self.o
        self._need(4)
        idx = struct.unpack_from("<i", self.b, self.o)[0]
        self.o += 4
        if idx == -2:
            self._rec("pnull", name, None)
            return None
        if idx >= 0:
            if idx >= len(self.seq):
                raise Drift("backref %d >= %d objects at 0x%x" % (idx, len(self.seq), at))
            return self._rec("pref", name, self.seq[idx])
        self._need(4)
        cid = struct.unpack_from("<I", self.b, self.o)[0]
        self.o += 4
        cls = CLASSES.get(cid)
        if cls is None:
            raise Drift("unknown ClassType 0x%08X at 0x%x" % (cid, self.o - 4))
        obj = Obj(cls)
        obj.index = len(self.seq)
        self.seq.append(obj)
        self._rec("pnew", name, obj)
        prev, self.cur = self.cur, obj
        DISPATCH[cls](self)
        self.cur = prev
        return obj


class WCtx(object):
    def __init__(self, flags):
        self.out = bytearray()
        self.flags = flags
        self.cur = None
        self.i = 0
        self.stack = []
        self.seq = []

    def _next(self, kind):
        k, _, v = self.cur.ops[self.i]
        if k != kind:
            raise Drift("op mismatch in %s at %d: have %s want %s"
                        % (self.cur.cls, self.i, k, kind))
        self.i += 1
        return v

    def u8(self, name):
        v = self._next("u8")
        self.out.append(v)
        return v

    def u32(self, name):
        v = self._next("u32")
        self.out += struct.pack("<I", v)
        return v

    def s32(self, name):
        v = self._next("s32")
        self.out += struct.pack("<i", v)
        return v

    def f32(self, name):
        v = self._next("f32")
        self.out += v
        return v

    def string(self, name):
        v = self._next("str")
        self.out += struct.pack("<I", len(v)) + v
        return v

    def data(self, name):
        v = self._next("data")
        self.out += struct.pack("<I", len(v)) + v
        return v

    def raw(self, name, n):
        v = self._next("raw")
        self.out += v
        return v

    def ver(self, name):
        k, _, v = self.cur.ops[self.i]
        self.i += 1
        if k == "nover":
            return 0
        if k != "ver":
            raise Drift("expected a version op in %s, got %s" % (self.cur.cls, k))
        self.out += struct.pack("<II", TAG, v)
        return v

    def ptr(self, name):
        k, _, v = self.cur.ops[self.i]
        self.i += 1
        if k == "pnull":
            self.out += struct.pack("<i", -2)
            return None
        if k == "pref":
            self.out += struct.pack("<i", v.index)
            return v
        if k != "pnew":
            raise Drift("expected a pointer op in %s, got %s" % (self.cur.cls, k))
        self.out += struct.pack("<iI", -1, CLSID[v.cls])
        v.index = len(self.seq)
        self.seq.append(v)
        self.stack.append((self.cur, self.i))
        self.cur, self.i = v, 0
        DISPATCH[v.cls](self)
        self.cur, self.i = self.stack.pop()
        return v


def plist(s, name):
    while s.ptr(name) is not None:
        pass


def sid(s, name):
    s.u32(name)
    if s.flags & F_NAMED:
        s.string(name + ".sourceString")


def hname(s, name):
    s.u32(name + ".m_nHashValue")
    s.string(name + ".m_szName")


def r_MoveObject(s):
    s.ver("CMoveObject")
    if s.flags & F_NAMED:
        s.string("m_name")
        s.raw("m_guid", 16)


def r_MoveDescriptor(s):
    r_MoveObject(s)
    plist(s, "CMoveCriteria")


def r_MoveDescriptorGroup(s):
    r_MoveDescriptor(s)
    v = s.ver("CMoveDescriptorGroup")
    plist(s, "CMoveDescriptor")
    if v > 0:
        plist(s, "CTransitionLink")
    if v >= 2:
        plist(s, "CNotInterruptibleLink")


def r_MoveStateRef(s):
    r_MoveDescriptor(s)
    if s.flags & F_GRAPH:
        s.ptr("m_state")


def r_MoveBaseState(s):
    v = s.ver("CMoveBaseState")
    r_MoveDescriptorGroup(s)
    if s.flags & F_NAMED:
        if v >= 4:
            sid(s, "m_stateNameHash")
        if v > 4:
            sid(s, "aliasID")
        s.u32("m_namedTrailer")
        return
    if v >= 4:
        s.u32("m_stateNameHash")
    if v > 4:
        s.u32("aliasID")


def r_MoveState(s):
    v = s.ver("CMoveState")
    if v <= 1:
        s.u8("m_fHonorFacing")
        s.u8("m_fLooping")
        s.u8("m_fRelative")
    r_MoveBaseState(s)


def r_MoveGroup(s):
    r_MoveDescriptorGroup(s)
    if s.ver("CMoveGroup") > 0:
        s.u8("m_branchEnable")


def r_AnimTech(s):
    v = s.ver("CAnimTech")
    r_MoveObject(s)
    if v < 9:
        s.f32("m_startTime")
        s.f32("m_stopTime")
        s.s32("m_blendStyle")
    s.f32("m_flStartTimeIn")
    s.f32("m_flDurationIn")
    s.f32("m_flStartTimeOut")
    s.f32("m_flDurationOut")
    s.u32("m_dwBlendTypeIn")
    s.u32("m_dwBlendTypeOut")
    s.s32("m_iParentID")
    if 1 <= v <= 5:
        s.s32("m_iHandleHash")
        s.s32("m_iModelHashPart")
    if v < 9:
        s.s32("m_iHandleHash")
        hname(s, "m_iModelHashNamePart")
    else:
        sid(s, "m_iModelHashNamePartID")
    if v > 1:
        s.string("m_partName")
    if v > 2:
        hname(s, "m_parentBoneName")


def r_BaseAnimGroup(s):
    r_MoveDescriptorGroup(s)
    v = s.ver("CBaseAnimGroup")
    if v >= 1:
        s.f32("m_flAnimGroupValue")
    if v >= 3:
        plist(s, "CAnimTech")
    if v > 3:
        s.f32("m_headLookAtEnable")
    if v in (5, 6):
        plist(s, "CTransitionLink")
        return
    if v >= 6:
        s.u8("m_livePostureEnable")
    if v == 8:
        s.u8("m_useStaticChestLookat")
    if v > 8:
        s.s32("m_weaponOffsetMode")
    if v > 8 or v < 6:
        s.u8("m_destructiveLookat")


def r_MoveComment(s):
    r_MoveDescriptor(s)
    s.u8("m_popup")


def r_MoveDefinition(s):
    v = s.ver("CMoveDefinition")
    s.s32("m_eMoveDefVariation")
    r_MoveDescriptorGroup(s) if v == 0 else r_BaseAnimGroup(s)


def r_MoveCriteria(s):
    v = s.ver("CMoveCriteria")
    if s.flags & F_GRAPH:
        s.u8("m_eValueID")
    r_MoveObject(s)
    if v < 4:
        s.u8("m_bHysteresisEnabled")
    if v > 2:
        s.s32("m_logicOperator")


def r_CritEnum(s):
    v = s.ver("CMoveCriteriaEnum")
    if not (v > 0 and not (s.flags & F_GRAPH)):
        s.s32("m_Value")
    r_MoveCriteria(s)


def r_CritEntityID(s):
    s.u8("m_Value") if s.flags & F_GRAPH else s.string("m_szEntityID")
    r_MoveCriteria(s)


def r_Crit_u8(s):
    s.u8("m_Value")
    r_MoveCriteria(s)


def r_Crit_i32(s):
    s.s32("m_Value")
    r_MoveCriteria(s)


def r_CritPerc(s):
    s.u8("m_uchPercentage")
    r_MoveCriteria(s)


def r_CritIntv_i(s):
    v = s.ver("TMoveCriteriaIntv")
    s.s32("m_LowerBound")
    s.s32("m_UpperBound")
    if v > 1:
        s.u8("m_inclusive")
    r_MoveCriteria(s)


def r_CritIntv_f(s):
    v = s.ver("TMoveCriteriaIntv")
    s.f32("m_LowerBound")
    s.f32("m_UpperBound")
    if v > 1:
        s.u8("m_inclusive")
    r_MoveCriteria(s)


def r_CritIntv_a(s):
    s.f32("m_LowerBound")
    s.f32("m_UpperBound")
    r_MoveCriteria(s)


def r_MoveObjectRef(s):
    if s.flags & F_NAMED:
        s.raw("m_targetGuid", 16)
        s.string("m_targetName")
    else:
        s.ptr("m_ptr")


def r_TransitionLink(s):
    v = s.ver("CTransitionLink")
    r_MoveObject(s)
    if v > 0:
        s.f32("flBlendTime")
        s.u32("dwBlendType")
        s.f32("flBlendRate")
        r_MoveObjectRef(s)
    if v >= 2:
        s.ptr("m_group")


def r_NotInterruptibleLink(s):
    v = s.ver("CNotInterruptibleLink")
    r_MoveObject(s)
    if v > 0:
        r_MoveObjectRef(s)


def r_AnimTechAnchor(s):
    r_AnimTech(s)
    v = s.ver("CAnimTechAnchor")
    sid(s, "m_anchorPartName")
    if v == 1:
        s.u8("m_followTerrain")
    if v >= 3:
        s.u8("m_followTerrain")
    if v >= 4:
        s.u8("m_disablePhysics")
    if v >= 6:
        s.u8("m_disable")


def r_AnimTechRagdoll(s):
    r_AnimTech(s)
    s.f32("m_physicsEnable")
    s.f32("m_physicsMuscleIntensity")


def r_MoveDefParameter(s):
    v = s.ver("CMoveDefParameter")
    s.f32("m_flStartTime")
    s.f32("m_flStopTime")
    s.f32("m_flCutTime")
    s.u32("m_dwBlendType")
    s.f32("m_flBlendTime")
    s.f32("m_flMultiplier")
    r_BaseAnimGroup(s)
    s.u8("m_fInterruptible")
    if v > 0x18:
        s.u8("m_dropEventsOutsideRange")
    if v < 0x13:
        s.f32("m_physicsEnable")
    s.f32("m_physicsMuscleIntensity")
    s.s32("m_loopOverride")
    s.u8("m_categoryOverride")
    s.s32("m_cutBehaviour")
    s.u8("m_motionOrientationCorrection")
    s.f32("m_lastAnimDataDuration")
    if v > 0x0F:
        s.u32("m_animNameHash")
    if v > 0x10:
        s.u8("m_bodyPartAvailability")
    if v > 0x13:
        s.u8("m_lowerBodyProgressState")
    if v == 0x12:
        s.u8("m_physicsControlledRagdoll")
    if v > 0x12:
        s.u8("m_ragdollController")
    if v > 0x14:
        s.u8("m_displacementMode")
    if v > 0x15:
        sid(s, "m_package")
    if v > 0x16:
        s.u8("m_poseInfoForPMS")


def r_AxialBlendAnimGroup(s):
    s.u8("m_eAxisValueID") if s.flags & F_GRAPH else s.string("m_szValueID")
    r_BaseAnimGroup(s)
    if s.ver("CAxialBlendAnimGroup") > 3:
        s.u8("m_scaleDuration")


def r_LayeredParameter(s):
    v = s.ver("CLayeredParameter")
    if v > 1:
        s.s32("m_spliceBlendMode")
    s.data("m_rgflBoneWeights")
    r_MoveDefParameter(s)
    if v > 3:
        s.f32("m_worldOffsetForLayer")
    if v > 4:
        s.f32("m_flBlendOutTime")


def r_LayeredAxialBlend(s):
    v = s.ver("CLayeredAxialBlend")
    if v >= 2:
        s.u8("m_spliceBlendMode")
    s.data("m_rgflBoneWeights")
    r_AxialBlendAnimGroup(s)
    if v > 3:
        s.f32("m_worldOffsetForLayer")
    if v > 4:
        s.f32("m_flBlendOutTime")


def r_TimeCtrlLayeredParam(s):
    v = s.ver("CTimeControlledLayeredParameter")
    r_LayeredParameter(s)
    s.u8("m_eTimeSourceID") if s.flags & F_GRAPH else s.string("m_szValueID")
    if v > 1:
        s.f32("m_timeSourceRangeMin")
        s.f32("m_timeSourceRangeMax")


def r_TimeCtrlMoveParam(s):
    v = s.ver("CTimeControlledMoveParameter")
    r_MoveDefParameter(s)
    s.u8("m_eTimeSourceID") if s.flags & F_GRAPH else s.string("m_szValueID")
    if v > 1:
        s.f32("m_timeSourceRangeMin")
        s.f32("m_timeSourceRangeMax")


def r_MoveValueDef(s):
    s.s32("m_eMVType")
    s.u8("m_fMirrorable")


def r_SyncDefParameter(s):
    v = s.ver("CSyncDefParameter")
    if v >= 8:
        s.u8("m_bApplyDisplacement")
    if v >= 7:
        s.u8("m_fLockedEntityLocation")
    if v >= 6:
        s.u8("m_fLockedEntity")
    if v >= 1:
        s.u8("m_fOptionalEntity")
    s.f32("m_flSyncTime")
    if v <= 1:
        s.f32("m_flStartTime")
        s.f32("m_flStopTime")
        s.u32("m_dwBlendType")
        s.f32("m_flBlendTime")
        s.f32("m_flCutTime")
        s.f32("m_flMultiplier")
    s.u8("m_eEntityID") if (v < 5 or s.flags & F_GRAPH) else s.string("m_szEntityID")
    r_MoveDescriptor(s) if v < 2 else r_MoveDefParameter(s)


def r_FrankensteinParameter(s):
    v = s.ver("CFrankensteinParameter")
    r_MoveDescriptorGroup(s)
    if v >= 2:
        s.u32("m_poseNameHash")
    if v >= 3:
        s.f32("m_flStopTime")
    if v >= 4:
        s.s32("m_speedMode")
        s.f32("m_customSpeed")


def r_MoveStateMachine(s):
    r_MoveObject(s)
    if s.flags & F_GRAPH:
        for _ in range(s.u32("nbState")):
            s.ptr("CMoveBaseState")


def r_ValueContainer(s):
    n = s.u32("ms_iNumMoveValue")
    r_MoveObject(s)
    for _ in range(n):
        t = s.u32("m_eMVType")
        s.u8("m_fMirrorable")
        if not (s.flags & F_NAMED):
            continue
        s.string("m_szName")
        if t == 5:
            count = s.u32("m_iNumEnumValues")
            s.u32("m_iNumEnumValues2")
            for _ in range(count):
                s.string("m_szEnumValue")


def r_MoveMgr(s):
    v = s.ver("CMoveMgr")
    r_MoveObject(s)
    if v > 4:
        s.ver("DefinitionFile")
    s.ptr("CMoveValueContainer")
    vpl = s.ver("PackageList")
    for _ in range(s.u32("size")):
        s.string("Name")
        s.string("Extension")
        if vpl > 0:
            s.string("ExportWithWorld")
    vtf = s.ver("TransitionFile") if v > 4 else v
    named = bool(s.flags & F_NAMED)
    ncat = 0
    for _ in range(s.s32("m_iNumMoveBlendSet")):
        ncat = s.s32("m_rgiNumMoveBlendCategory")
        if named:
            s.string("m_szBlendSetName")
        for _ in range(ncat):
            if named:
                s.string("m_szBlendCategoryName")
            if vtf > 3:
                s.s32("m_rgiNumMoveBlendCategoryParent")
            npose = s.s32("m_rgiNumMoveBlendPose")
            s.u8("m_rgfBlendCategoryStationary")
            if named:
                for _ in range(npose):
                    s.string("m_szBlendPoseName")
                for _ in range(npose):
                    s.string("m_szMirrorBlendPoseName")
            else:
                for _ in range(npose):
                    s.s32("m_rgiMirrorMoveBlendPose")
    s.ptr("CMoveStateMachine")
    if vtf > 0:
        s.ptr("m_defaultTransition")
    for _ in range(ncat * ncat):
        s.ptr("m_transitionMatrix")


DISPATCH = {
    "CMoveObject": r_MoveObject,
    "CMoveMgr": r_MoveMgr,
    "CMoveValueContainer": r_ValueContainer,
    "CPlayerMoveState": r_ValueContainer,
    "CMoveStateMachine": r_MoveStateMachine,
    "CMoveBaseState": r_MoveBaseState,
    "CMoveState": r_MoveState,
    "CLayeredState": r_MoveBaseState,
    "CSyncState": r_MoveBaseState,
    "CFrankensteinState": r_MoveBaseState,
    "CMoveGroup": r_MoveGroup,
    "CDoNothing": r_MoveDescriptorGroup,
    "CMoveComment": r_MoveComment,
    "CMoveDefinition": r_MoveDefinition,
    "CSyncDefinition": r_MoveDefinition,
    "CMoveStateRef": r_MoveStateRef,
    "CLayeredStateRef": r_MoveStateRef,
    "CTransitionLink": r_TransitionLink,
    "CNotInterruptibleLink": r_NotInterruptibleLink,
    "CAnimTechAnchor": r_AnimTechAnchor,
    "CAnimTechIKPath": r_AnimTech,
    "CAnimTechAttach": r_AnimTech,
    "CAnimTechPossession": r_AnimTech,
    "CAnimTechRagdoll": r_AnimTechRagdoll,
    "CAxialBlendAnimGroup": r_AxialBlendAnimGroup,
    "CMoveDefParameter": r_MoveDefParameter,
    "CLayeredParameter": r_LayeredParameter,
    "CLayeredAxialBlend": r_LayeredAxialBlend,
    "CTimeControlledLayeredParameter": r_TimeCtrlLayeredParam,
    "CTimeControlledMoveParameter": r_TimeCtrlMoveParam,
    "CMoveValueDef": r_MoveValueDef,
    "CSyncDefParameter": r_SyncDefParameter,
    "CFrankensteinParameter": r_FrankensteinParameter,
    "CMoveCriteriaEnumEqual": r_CritEnum,
    "CMoveCriteriaEnumNotEqual": r_CritEnum,
    "CMoveCriteriaEntityIDEqual": r_CritEntityID,
    "CMoveCriteriaEntityIDNotEqual": r_CritEntityID,
    "TMoveCriteriaEqual<uint8>": r_Crit_u8,
    "TMoveCriteriaNotEqual<uint8>": r_Crit_u8,
    "TMoveCriteriaEqual<bool>": r_Crit_u8,
    "TMoveCriteriaNotEqual<bool>": r_Crit_u8,
    "TMoveCriteriaEqual<int>": r_Crit_i32,
    "TMoveCriteriaNotEqual<int>": r_Crit_i32,
    "TMoveCriteriaIntv<int>": r_CritIntv_i,
    "TMoveCriteriaIntv<float>": r_CritIntv_f,
    "TMoveCriteriaIntv<CAngle>": r_CritIntv_a,
    "TMoveCriteriaPerc<int>": r_CritPerc,
    "TMoveCriteriaPerc<float>": r_CritPerc,
}


class MoveFile(object):
    def __init__(self):
        self.mtype = 0
        self.mversion = 0
        self.flags = 0
        self.root = None
        self.seq = []


def load(path):
    b = open(path, "rb").read()
    mtype, mversion, flags = struct.unpack_from("<III", b, 0)
    r = RCtx(b, flags)
    r.o = 12
    mf = MoveFile()
    mf.mtype, mf.mversion, mf.flags = mtype, mversion, flags
    mf.root = Obj("#file")
    r.cur = mf.root
    r.ptr("root")
    if r.o != len(b):
        raise Drift("short parse: 0x%x of 0x%x" % (r.o, len(b)))
    mf.seq = r.seq
    return mf


def save(mf):
    w = WCtx(mf.flags)
    w.out += struct.pack("<III", mf.mtype, mf.mversion, mf.flags)
    w.cur = mf.root
    w.ptr("root")
    return bytes(w.out)


def state_machine(mf):
    return next(o for o in mf.seq if o.cls == "CMoveStateMachine")


def channel_table(path):
    """The 105 value channels as (name, [enum values]), read from a *named* twin.

    Only the loadable form is fully decoded, so the parse is allowed to fail once the
    value container is behind us - the channel table is the first thing in the file.
    """
    b = open(path, "rb").read()
    r = RCtx(b, struct.unpack_from("<I", b, 8)[0])
    r.o = 12
    r.cur = Obj("#file")
    try:
        r.ptr("root")
    except Drift:
        pass
    container = next((o for o in r.seq if o.cls == "CMoveValueContainer"), None)
    if container is None:
        raise Drift("no CMoveValueContainer in %s" % path)
    channels, pending, values = [], None, None
    for kind, name, value in container.ops:
        if name == "m_eMVType":
            pending = value
        elif name == "m_szName" and pending is not None:
            values = []
            channels.append((value.decode("latin1"), values if pending == 5 else None))
            pending = None
        elif name == "m_szEnumValue":
            values.append(value.decode("latin1"))
    return channels


def field(obj, name):
    """Value of the first op carrying this field name, or None."""
    for _, n, v in obj.ops:
        if n == name:
            return v
    return None


def set_field(obj, name, value):
    for i, (kind, n, _) in enumerate(obj.ops):
        if n == name:
            obj.ops[i] = (kind, n, value)
            return True
    return False


def main():
    for path in sys.argv[1:]:
        original = open(path, "rb").read()
        mf = load(path)
        rewritten = save(mf)
        print("%s" % path)
        print("   %d bytes, %d objects, flags 0x%05x" % (len(original), len(mf.seq), mf.flags))
        print("   round trip: %s"
              % ("byte-identical" if rewritten == original else "*** MISMATCH ***"))
        for cls, n in collections.Counter(o.cls for o in mf.seq).most_common():
            print("      %-34s %d" % (cls, n))


if __name__ == "__main__":
    main()
