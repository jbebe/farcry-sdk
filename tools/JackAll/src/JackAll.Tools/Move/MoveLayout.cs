namespace JackAll.Tools.Move;

/// <summary>
/// What each MOVE class writes, in order. One set of functions drives both directions: the reader
/// records each primitive, the writer replays the recorded values while emitting bytes, so the
/// version gates and list terminators below take the same branches without being special-cased.
/// </summary>
/// <remarks>Recovered by decompiling every <c>Serialize</c> in the MOVE subsystem; field names are
/// the engine's own. See docs/docs/file-formats/move.md.</remarks>
internal static class MoveLayout
{
    private static readonly Dictionary<string, Action<IMoveCodec>> Dispatch = new()
    {
        ["CMoveObject"] = MoveObjectBase,
        ["CMoveMgr"] = MoveMgr,
        ["CMoveValueContainer"] = ValueContainer,
        ["CPlayerMoveState"] = ValueContainer,
        ["CMoveStateMachine"] = StateMachine,
        ["CMoveBaseState"] = BaseState,
        ["CMoveState"] = MoveState,
        ["CLayeredState"] = BaseState,
        ["CSyncState"] = BaseState,
        ["CFrankensteinState"] = BaseState,
        ["CMoveGroup"] = MoveGroup,
        ["CDoNothing"] = DescriptorGroup,
        ["CMoveComment"] = MoveComment,
        ["CMoveDefinition"] = MoveDefinition,
        ["CSyncDefinition"] = MoveDefinition,
        ["CMoveStateRef"] = StateRef,
        ["CLayeredStateRef"] = StateRef,
        ["CTransitionLink"] = TransitionLink,
        ["CNotInterruptibleLink"] = NotInterruptibleLink,
        ["CAnimTechAnchor"] = AnimTechAnchor,
        ["CAnimTechIKPath"] = AnimTech,
        ["CAnimTechAttach"] = AnimTech,
        ["CAnimTechPossession"] = AnimTech,
        ["CAnimTechRagdoll"] = AnimTechRagdoll,
        ["CAxialBlendAnimGroup"] = AxialBlendAnimGroup,
        ["CMoveDefParameter"] = DefParameter,
        ["CLayeredParameter"] = LayeredParameter,
        ["CLayeredAxialBlend"] = LayeredAxialBlend,
        ["CTimeControlledLayeredParameter"] = TimeControlledLayeredParameter,
        ["CTimeControlledMoveParameter"] = TimeControlledMoveParameter,
        ["CMoveValueDef"] = ValueDef,
        ["CSyncDefParameter"] = SyncDefParameter,
        ["CFrankensteinParameter"] = FrankensteinParameter,
        ["CMoveCriteriaEnumEqual"] = CriteriaEnum,
        ["CMoveCriteriaEnumNotEqual"] = CriteriaEnum,
        ["CMoveCriteriaEntityIDEqual"] = CriteriaEntityId,
        ["CMoveCriteriaEntityIDNotEqual"] = CriteriaEntityId,
        ["TMoveCriteriaEqual<uint8>"] = CriteriaByte,
        ["TMoveCriteriaNotEqual<uint8>"] = CriteriaByte,
        ["TMoveCriteriaEqual<bool>"] = CriteriaByte,
        ["TMoveCriteriaNotEqual<bool>"] = CriteriaByte,
        ["TMoveCriteriaEqual<int>"] = CriteriaInt,
        ["TMoveCriteriaNotEqual<int>"] = CriteriaInt,
        ["TMoveCriteriaIntv<int>"] = CriteriaIntervalInt,
        ["TMoveCriteriaIntv<float>"] = CriteriaIntervalFloat,
        ["TMoveCriteriaIntv<CAngle>"] = CriteriaIntervalAngle,
        ["TMoveCriteriaPerc<int>"] = CriteriaPercentage,
        ["TMoveCriteriaPerc<float>"] = CriteriaPercentage,
    };

    public static void Serialize(IMoveCodec codec, string className)
    {
        if (!Dispatch.TryGetValue(className, out Action<IMoveCodec>? layout))
        {
            throw new MoveFormatException($"no layout for {className}");
        }

        layout(codec);
    }

    private static bool Named(IMoveCodec c) => (c.Flags & MoveFlags.Named) != 0;

    private static bool Graph(IMoveCodec c) => (c.Flags & MoveFlags.StateGraph) != 0;

    /// <summary>A run of pointers closed by a null; the engine walks a linked list to write it.</summary>
    private static void PointerList(IMoveCodec c, string name)
    {
        while (c.Pointer(name) is not null)
        {
        }
    }

    /// <summary>A CStringID or CPathID: the hash, plus the text it came from in a named file.</summary>
    private static void StringId(IMoveCodec c, string name)
    {
        c.U32(name);
        if (Named(c))
        {
            c.Str(name + ".sourceString");
        }
    }

    private static void HashName(IMoveCodec c, string name)
    {
        c.U32(name + ".m_nHashValue");
        c.Str(name + ".m_szName");
    }

    private static void MoveObjectBase(IMoveCodec c)
    {
        c.Version("CMoveObject");
        if (Named(c))
        {
            c.Str("m_name");
            c.Raw("m_guid", 16);
        }
    }

    private static void Descriptor(IMoveCodec c)
    {
        MoveObjectBase(c);
        PointerList(c, "CMoveCriteria");
    }

    private static void DescriptorGroup(IMoveCodec c)
    {
        Descriptor(c);
        uint v = c.Version("CMoveDescriptorGroup");
        PointerList(c, "CMoveDescriptor");
        if (v > 0)
        {
            PointerList(c, "CTransitionLink");
        }

        if (v >= 2)
        {
            PointerList(c, "CNotInterruptibleLink");
        }
    }

    private static void StateRef(IMoveCodec c)
    {
        Descriptor(c);
        if (Graph(c))
        {
            c.Pointer("m_state");
        }
    }

    private static void BaseState(IMoveCodec c)
    {
        uint v = c.Version("CMoveBaseState");
        DescriptorGroup(c);
        if (Named(c))
        {
            if (v >= 4)
            {
                StringId(c, "m_stateNameHash");
            }

            if (v > 4)
            {
                StringId(c, "aliasID");
            }

            c.U32("m_namedTrailer");
            return;
        }

        if (v >= 4)
        {
            c.U32("m_stateNameHash");
        }

        if (v > 4)
        {
            c.U32("aliasID");
        }
    }

    private static void MoveState(IMoveCodec c)
    {
        uint v = c.Version("CMoveState");
        if (v <= 1)
        {
            c.U8("m_fHonorFacing");
            c.U8("m_fLooping");
            c.U8("m_fRelative");
        }

        BaseState(c);
    }

    private static void MoveGroup(IMoveCodec c)
    {
        DescriptorGroup(c);
        if (c.Version("CMoveGroup") > 0)
        {
            c.U8("m_branchEnable");
        }
    }

    private static void AnimTech(IMoveCodec c)
    {
        uint v = c.Version("CAnimTech");
        MoveObjectBase(c);
        if (v < 9)
        {
            c.F32("m_startTime");
            c.F32("m_stopTime");
            c.S32("m_blendStyle");
        }

        c.F32("m_flStartTimeIn");
        c.F32("m_flDurationIn");
        c.F32("m_flStartTimeOut");
        c.F32("m_flDurationOut");
        c.U32("m_dwBlendTypeIn");
        c.U32("m_dwBlendTypeOut");
        c.S32("m_iParentID");
        if (v is >= 1 and <= 5)
        {
            c.S32("m_iHandleHash");
            c.S32("m_iModelHashPart");
        }

        if (v < 9)
        {
            c.S32("m_iHandleHash");
            HashName(c, "m_iModelHashNamePart");
        }
        else
        {
            StringId(c, "m_iModelHashNamePartID");
        }

        if (v > 1)
        {
            c.Str("m_partName");
        }

        if (v > 2)
        {
            HashName(c, "m_parentBoneName");
        }
    }

    private static void BaseAnimGroup(IMoveCodec c)
    {
        DescriptorGroup(c);
        uint v = c.Version("CBaseAnimGroup");
        if (v >= 1)
        {
            c.F32("m_flAnimGroupValue");
        }

        if (v >= 3)
        {
            PointerList(c, "CAnimTech");
        }

        if (v > 3)
        {
            c.F32("m_headLookAtEnable");
        }

        if (v is 5 or 6)
        {
            PointerList(c, "CTransitionLink");
            return;
        }

        if (v >= 6)
        {
            c.U8("m_livePostureEnable");
        }

        if (v == 8)
        {
            c.U8("m_useStaticChestLookat");
        }

        if (v > 8)
        {
            c.S32("m_weaponOffsetMode");
        }

        if (v > 8 || v < 6)
        {
            c.U8("m_destructiveLookat");
        }
    }

    private static void MoveComment(IMoveCodec c)
    {
        Descriptor(c);
        c.U8("m_popup");
    }

    private static void MoveDefinition(IMoveCodec c)
    {
        uint v = c.Version("CMoveDefinition");
        c.S32("m_eMoveDefVariation");
        if (v == 0)
        {
            DescriptorGroup(c);
        }
        else
        {
            BaseAnimGroup(c);
        }
    }

    private static void Criteria(IMoveCodec c)
    {
        uint v = c.Version("CMoveCriteria");
        if (Graph(c))
        {
            c.U8("m_eValueID");
        }

        MoveObjectBase(c);
        if (v < 4)
        {
            c.U8("m_bHysteresisEnabled");
        }

        if (v > 2)
        {
            c.S32("m_logicOperator");
        }
    }

    private static void CriteriaEnum(IMoveCodec c)
    {
        uint v = c.Version("CMoveCriteriaEnum");
        if (!(v > 0 && !Graph(c)))
        {
            c.S32("m_Value");
        }

        Criteria(c);
    }

    private static void CriteriaEntityId(IMoveCodec c)
    {
        if (Graph(c))
        {
            c.U8("m_Value");
        }
        else
        {
            c.Str("m_szEntityID");
        }

        Criteria(c);
    }

    private static void CriteriaByte(IMoveCodec c)
    {
        c.U8("m_Value");
        Criteria(c);
    }

    private static void CriteriaInt(IMoveCodec c)
    {
        c.S32("m_Value");
        Criteria(c);
    }

    private static void CriteriaPercentage(IMoveCodec c)
    {
        c.U8("m_uchPercentage");
        Criteria(c);
    }

    private static void CriteriaIntervalInt(IMoveCodec c)
    {
        uint v = c.Version("TMoveCriteriaIntv");
        c.S32("m_LowerBound");
        c.S32("m_UpperBound");
        if (v > 1)
        {
            c.U8("m_inclusive");
        }

        Criteria(c);
    }

    private static void CriteriaIntervalFloat(IMoveCodec c)
    {
        uint v = c.Version("TMoveCriteriaIntv");
        c.F32("m_LowerBound");
        c.F32("m_UpperBound");
        if (v > 1)
        {
            c.U8("m_inclusive");
        }

        Criteria(c);
    }

    private static void CriteriaIntervalAngle(IMoveCodec c)
    {
        c.F32("m_LowerBound");
        c.F32("m_UpperBound");
        Criteria(c);
    }

    /// <summary>In a named file this is a GUID and the target's name, not a stream position.</summary>
    private static void ObjectRef(IMoveCodec c)
    {
        if (Named(c))
        {
            c.Raw("m_targetGuid", 16);
            c.Str("m_targetName");
        }
        else
        {
            c.Pointer("m_ptr");
        }
    }

    private static void TransitionLink(IMoveCodec c)
    {
        uint v = c.Version("CTransitionLink");
        MoveObjectBase(c);
        if (v > 0)
        {
            c.F32("flBlendTime");
            c.U32("dwBlendType");
            c.F32("flBlendRate");
            ObjectRef(c);
        }

        if (v >= 2)
        {
            c.Pointer("m_group");
        }
    }

    private static void NotInterruptibleLink(IMoveCodec c)
    {
        uint v = c.Version("CNotInterruptibleLink");
        MoveObjectBase(c);
        if (v > 0)
        {
            ObjectRef(c);
        }
    }

    private static void AnimTechAnchor(IMoveCodec c)
    {
        AnimTech(c);
        uint v = c.Version("CAnimTechAnchor");
        StringId(c, "m_anchorPartName");
        if (v == 1)
        {
            c.U8("m_followTerrain");
        }

        if (v >= 3)
        {
            c.U8("m_followTerrain");
        }

        if (v >= 4)
        {
            c.U8("m_disablePhysics");
        }

        if (v >= 6)
        {
            c.U8("m_disable");
        }
    }

    private static void AnimTechRagdoll(IMoveCodec c)
    {
        AnimTech(c);
        c.F32("m_physicsEnable");
        c.F32("m_physicsMuscleIntensity");
    }

    private static void DefParameter(IMoveCodec c)
    {
        uint v = c.Version("CMoveDefParameter");
        c.F32("m_flStartTime");
        c.F32("m_flStopTime");
        c.F32("m_flCutTime");
        c.U32("m_dwBlendType");
        c.F32("m_flBlendTime");
        c.F32("m_flMultiplier");
        BaseAnimGroup(c);
        c.U8("m_fInterruptible");
        if (v > 0x18)
        {
            c.U8("m_dropEventsOutsideRange");
        }

        if (v < 0x13)
        {
            c.F32("m_physicsEnable");
        }

        c.F32("m_physicsMuscleIntensity");
        c.S32("m_loopOverride");
        c.U8("m_categoryOverride");
        c.S32("m_cutBehaviour");
        c.U8("m_motionOrientationCorrection");
        c.F32("m_lastAnimDataDuration");
        if (v > 0x0F)
        {
            c.U32("m_animNameHash");
        }

        if (v > 0x10)
        {
            c.U8("m_bodyPartAvailability");
        }

        if (v > 0x13)
        {
            c.U8("m_lowerBodyProgressState");
        }

        if (v == 0x12)
        {
            c.U8("m_physicsControlledRagdoll");
        }

        if (v > 0x12)
        {
            c.U8("m_ragdollController");
        }

        if (v > 0x14)
        {
            c.U8("m_displacementMode");
        }

        if (v > 0x15)
        {
            StringId(c, "m_package");
        }

        if (v > 0x16)
        {
            c.U8("m_poseInfoForPMS");
        }
    }

    private static void AxialBlendAnimGroup(IMoveCodec c)
    {
        if (Graph(c))
        {
            c.U8("m_eAxisValueID");
        }
        else
        {
            c.Str("m_szValueID");
        }

        BaseAnimGroup(c);
        if (c.Version("CAxialBlendAnimGroup") > 3)
        {
            c.U8("m_scaleDuration");
        }
    }

    private static void LayeredParameter(IMoveCodec c)
    {
        uint v = c.Version("CLayeredParameter");
        if (v > 1)
        {
            c.S32("m_spliceBlendMode");
        }

        c.Data("m_rgflBoneWeights");
        DefParameter(c);
        if (v > 3)
        {
            c.F32("m_worldOffsetForLayer");
        }

        if (v > 4)
        {
            c.F32("m_flBlendOutTime");
        }
    }

    private static void LayeredAxialBlend(IMoveCodec c)
    {
        uint v = c.Version("CLayeredAxialBlend");
        if (v >= 2)
        {
            c.U8("m_spliceBlendMode");
        }

        c.Data("m_rgflBoneWeights");
        AxialBlendAnimGroup(c);
        if (v > 3)
        {
            c.F32("m_worldOffsetForLayer");
        }

        if (v > 4)
        {
            c.F32("m_flBlendOutTime");
        }
    }

    private static void TimeSource(IMoveCodec c)
    {
        if (Graph(c))
        {
            c.U8("m_eTimeSourceID");
        }
        else
        {
            c.Str("m_szValueID");
        }
    }

    private static void TimeControlledLayeredParameter(IMoveCodec c)
    {
        uint v = c.Version("CTimeControlledLayeredParameter");
        LayeredParameter(c);
        TimeSource(c);
        if (v > 1)
        {
            c.F32("m_timeSourceRangeMin");
            c.F32("m_timeSourceRangeMax");
        }
    }

    private static void TimeControlledMoveParameter(IMoveCodec c)
    {
        uint v = c.Version("CTimeControlledMoveParameter");
        DefParameter(c);
        TimeSource(c);
        if (v > 1)
        {
            c.F32("m_timeSourceRangeMin");
            c.F32("m_timeSourceRangeMax");
        }
    }

    private static void ValueDef(IMoveCodec c)
    {
        c.S32("m_eMVType");
        c.U8("m_fMirrorable");
    }

    private static void SyncDefParameter(IMoveCodec c)
    {
        uint v = c.Version("CSyncDefParameter");
        if (v >= 8)
        {
            c.U8("m_bApplyDisplacement");
        }

        if (v >= 7)
        {
            c.U8("m_fLockedEntityLocation");
        }

        if (v >= 6)
        {
            c.U8("m_fLockedEntity");
        }

        if (v >= 1)
        {
            c.U8("m_fOptionalEntity");
        }

        c.F32("m_flSyncTime");
        if (v <= 1)
        {
            c.F32("m_flStartTime");
            c.F32("m_flStopTime");
            c.U32("m_dwBlendType");
            c.F32("m_flBlendTime");
            c.F32("m_flCutTime");
            c.F32("m_flMultiplier");
        }

        if (v < 5 || Graph(c))
        {
            c.U8("m_eEntityID");
        }
        else
        {
            c.Str("m_szEntityID");
        }

        if (v < 2)
        {
            Descriptor(c);
        }
        else
        {
            DefParameter(c);
        }
    }

    private static void FrankensteinParameter(IMoveCodec c)
    {
        uint v = c.Version("CFrankensteinParameter");
        DescriptorGroup(c);
        if (v >= 2)
        {
            c.U32("m_poseNameHash");
        }

        if (v >= 3)
        {
            c.F32("m_flStopTime");
        }

        if (v >= 4)
        {
            c.S32("m_speedMode");
            c.F32("m_customSpeed");
        }
    }

    private static void StateMachine(IMoveCodec c)
    {
        MoveObjectBase(c);
        if (!Graph(c))
        {
            return;
        }

        uint count = c.U32("nbState");
        for (uint i = 0; i < count; i++)
        {
            c.Pointer("CMoveBaseState");
        }
    }

    private static void ValueContainer(IMoveCodec c)
    {
        uint count = c.U32("ms_iNumMoveValue");
        MoveObjectBase(c);
        for (uint i = 0; i < count; i++)
        {
            uint type = c.U32("m_eMVType");
            c.U8("m_fMirrorable");
            if (!Named(c))
            {
                continue;
            }

            c.Str("m_szName");
            if (type != 5)
            {
                continue;
            }

            uint values = c.U32("m_iNumEnumValues");
            c.U32("m_iNumEnumValues2");
            for (uint e = 0; e < values; e++)
            {
                c.Str("m_szEnumValue");
            }
        }
    }

    private static void MoveMgr(IMoveCodec c)
    {
        uint v = c.Version("CMoveMgr");
        MoveObjectBase(c);
        if (v > 4)
        {
            c.Version("DefinitionFile");
        }

        c.Pointer("CMoveValueContainer");

        uint packageVersion = c.Version("PackageList");
        uint packages = c.U32("size");
        for (uint i = 0; i < packages; i++)
        {
            c.Str("Name");
            c.Str("Extension");
            if (packageVersion > 0)
            {
                c.Str("ExportWithWorld");
            }
        }

        uint transitionVersion = v > 4 ? c.Version("TransitionFile") : v;
        bool named = Named(c);
        int categories = 0;
        int sets = c.S32("m_iNumMoveBlendSet");
        for (int s = 0; s < sets; s++)
        {
            categories = c.S32("m_rgiNumMoveBlendCategory");
            if (named)
            {
                c.Str("m_szBlendSetName");
            }

            for (int cat = 0; cat < categories; cat++)
            {
                if (named)
                {
                    c.Str("m_szBlendCategoryName");
                }

                if (transitionVersion > 3)
                {
                    c.S32("m_rgiNumMoveBlendCategoryParent");
                }

                int poses = c.S32("m_rgiNumMoveBlendPose");
                c.U8("m_rgfBlendCategoryStationary");
                if (named)
                {
                    for (int p = 0; p < poses; p++)
                    {
                        c.Str("m_szBlendPoseName");
                    }

                    for (int p = 0; p < poses; p++)
                    {
                        c.Str("m_szMirrorBlendPoseName");
                    }
                }
                else
                {
                    for (int p = 0; p < poses; p++)
                    {
                        c.S32("m_rgiMirrorMoveBlendPose");
                    }
                }
            }
        }

        c.Pointer("CMoveStateMachine");
        if (transitionVersion > 0)
        {
            c.Pointer("m_defaultTransition");
        }

        for (int i = 0; i < categories * categories; i++)
        {
            c.Pointer("m_transitionMatrix");
        }
    }
}
