"""
Reverse-engineered .mgb (Magma binary UI package) parser.

Every field layout below is transcribed from a decompile of the matching
`magma::BinaryLoadVisitor::Visit*` method. Addresses are `FarCry2_server`
(the Linux dedicated server, which retains full `magma::` C++ symbols and
links the same portable serialization code as the PC `Dunia.dll`).

Two independent facts make the whole format tractable, and both were only
established in the 2026-08-07 pass; everything else follows mechanically:

  1. Reader primitive widths. Every field read goes through the serializer's
     vtable; the slot number tells you the width and nothing else:
        +0x08 -> 4   +0x0c -> 4   +0x10 -> 2   +0x14 -> 2   +0x18 -> 1
        +0x1c -> 1   +0x20 -> 4   +0x24 -> 1   +0x28 -> raw bytes
        +0x2c -> raw UTF-16 (charCount * 2 bytes)
     (+0x08/+0x0c/+0x20 are distinct slots that all consume 4 bytes; the
     engine distinguishes int/float/"value", the wire format does not.)

  2. ELF vtable offsets are shifted by 8 relative to the offsets that appear
     in call sites: an object's vptr points at slot 0, which sits at +0x08 in
     the vtable object (+0x00 is offset-to-top, +0x04 is typeinfo). So a
     `CALL [vtable + 0x7c]` in a decompile resolves to the symbol listed at
     +0x84 in the vtable dump. Getting this wrong is what produced several
     generations of wrong State-hierarchy models in earlier passes.

The load entry point is `magma::BinaryLoadVisitor::Open` (0x0a060070), which
calls ReadHeader (0x0a05fef0) and then VisitPackage (0x0a0619e0).

Usage: python mgb_parser.py <file.mgb> [file2.mgb ...]
       python mgb_parser.py *.mgb   (shell-expanded) or omit args to scan cwd for *.mgb.

Sample files to test against live in tmp/menu/ (gitignored, not part of the repo checkout).
"""
import struct
import sys
import glob
import os
import zlib

MAGIC = b"MAGMA"
VERSION = 0x001EAB90
ENDIAN_MARKER_NORMAL = 0xAB

# magma::Id::Hash(char const*) (0x0a0782a0) is plain CRC-32 (IEEE, 0xEDB88320
# polynomial) over the class name string, so a file's type-table entries --
# which are raw Id values -- can be resolved back to class names with a static
# dictionary, without walking objecttypemanager's registrations.
KNOWN_TYPE_NAMES = [
    # --- Widget hierarchy: the 14 concrete types Factory::MakeElement accepts ---
    "Image", "Text", "RectShape", "Placeholder", "Window",
    "AreaInstance", "AutonomousAreaInstance", "ButtonInstance",
    "CheckBoxInstance", "RadioButtonInstance", "PageInstance",
    "ListBox", "EditBox", "Slider",
    # --- Widget hierarchy: bases and non-constructible members ---
    "Widget", "Element", "Focusable", "PageFocusable", "Checkable",
    "Radioable", "TextBase", "PixmapFont", "ExternalFont", "Font",
    # --- Area hierarchy: the 5 types Factory::MakeArea accepts, plus bases ---
    "Area", "Page", "Button", "CheckBox", "Cursor",
    # --- Keyframe / State hierarchy ---
    "Keyframe", "State", "RotationState", "PosState", "ScaleState",
    "RectState", "TextBaseState", "TextState", "ImageState", "RectShapeState",
    # --- Action hierarchy: the 9 types Factory::MakeActionExecuter accepts ---
    "ActionExecuter", "ActionExecuterEvent", "ActionExecuterInputable",
    "ActionExecuterFocusable", "ActionExecuterPage",
    "ActionExecuterPageInstance", "ActionExecuterListbox",
    "ActionExecuterEditbox", "ActionExecuterSlider",
    "Action", "ActionCaller", "ActionContinue", "ActionStop", "ActionPopPage",
    "ActionPushPage", "ActionGotoFrameIndex", "ActionGotoKeyFrame",
    # --- Package-level / infrastructure classes ---
    "Package", "NamedObject", "UserData", "UserDataItem", "Material",
    "Texture", "FontFamily", "StringTable", "StringResource",
    "StringResourceExternalId", "FullLink", "AreaLink", "AreaLinkTags",
    "GenericObject", "GenericObjectTable", "EngineRoot", "EngineObject",
    "EngineObjectGroup", "AnonymousType", "Variant", "VariantContainer",
    "WindowSection", "StretchableWindowSection", "DisplayConfiguration",
    "GlyphFont", "BaseObject", "Acceptor", "IScrollable",
    # --- Handlers / timing strategies (registered, never Factory-constructible) ---
    "Handler", "AreaHandler", "PageHandler", "DrawHandler", "EventHandler",
    "TickTimingStrategy", "NoTimingStrategy", "SyncTimingStrategy",
    "EventTriggeredTimingStrategy", "TimingStrategy",
    "TextScrollerPageHandler", "TextScrollerEventHandler",
    "TextScrollerDrawHandler",
    # --- Nomad (platform) subclasses seen in live Register hooks ---
    "CTextureNomad", "CEditBoxNomad", "CActionSignalBase",
    "SpecificType<ClassType>", "SpecificType<void>", "CActionSignal<S>",
    "ActionManager", "ObjectTypeCollection",
]
NAME_BY_HASH = {zlib.crc32(n.encode()) & 0xFFFFFFFF: n for n in KNOWN_TYPE_NAMES}


# ---------------------------------------------------------------------------
# Class taxonomy, transcribed from the three Factory dispatchers. Each is an
# ancestor-walk over an ObjectTypeInfo: the most-derived listed match wins, and
# a type matching nothing yields a null object (which the callers dereference
# unguarded -- so a real, loadable file can never contain such a type in these
# slots).
# ---------------------------------------------------------------------------

# Factory::MakeArea(Area::ObjectTypeInfo const*) @ 0x0a0480a0.
AREA_SUBTYPES = ("Area", "Page", "Button", "Cursor", "CheckBox")

# Factory::MakeElement(Widget::ObjectTypeInfo const*) @ 0x0a0481a0 -- picks
# which Element subclass wraps the widget. Only these 14 widget types (and
# their descendants) can appear in an Area's element list at all.
WIDGET_WRAPPER = {
    "Image": "Element",
    "Text": "Element",
    "RectShape": "Element",
    "Placeholder": "Element",
    "Window": "Element",
    "AreaInstance": "Element",
    "AutonomousAreaInstance": "Element",
    "ButtonInstance": "Focusable",
    "ListBox": "Focusable",
    "EditBox": "Focusable",
    "Slider": "Focusable",
    "PageInstance": "PageFocusable",
    "CheckBoxInstance": "Checkable",
    "RadioButtonInstance": "Radioable",
}

# Factory::MakeState(Widget::ObjectTypeInfo const*) @ 0x0a047c30 -- the widget
# class alone decides the concrete State subclass used by *every* keyframe on
# that element. (Earlier passes modelled this as a single RectState plus a
# per-owner "tail"; the tails were really just these different classes.)
WIDGET_STATE = {
    "Image": "ImageState",
    "Text": "TextState",
    "RectShape": "RectShapeState",
    "Placeholder": "RectState",
    "Window": "RectState",
    "AreaInstance": "ScaleState",
    "AutonomousAreaInstance": "ScaleState",
    "ButtonInstance": "ScaleState",
    "CheckBoxInstance": "ScaleState",
    "RadioButtonInstance": "ScaleState",
    "PageInstance": "ScaleState",
    "ListBox": "ScaleState",
    "EditBox": "ScaleState",
    "Slider": "ScaleState",
}

# Factory::MakeActionExecuter(ActionExecuter::ObjectTypeInfo const*) @ 0x0a0483c0.
# Bare ActionExecuter reads only the flat action list; the other 8 each forward
# (through zero-field hops) to VisitActionExecuterEvent, which appends the
# named-event index table on top.
ACTION_EXECUTER_EVENT_SUBTYPES = (
    "ActionExecuterEvent", "ActionExecuterInputable", "ActionExecuterFocusable",
    "ActionExecuterPage", "ActionExecuterPageInstance", "ActionExecuterListbox",
    "ActionExecuterEditbox", "ActionExecuterSlider",
)
ACTION_EXECUTER_SUBTYPES = ("ActionExecuter",) + ACTION_EXECUTER_EVENT_SUBTYPES


def build_slot_type_names(type_table):
    """type_table: list of (slot, external_id) from the header. Returns
    {slot: class_name} for every external_id we can resolve via CRC32."""
    return {
        slot: NAME_BY_HASH[ext_id]
        for slot, ext_id in type_table
        if ext_id in NAME_BY_HASH
    }


def is_empty_type_slot(slot: int, type_table_dict: dict) -> bool:
    """True when this file's type-remap byte for `slot` was never assigned.

    BinaryLoadVisitor's constructor (0x0a05edf0) does `memset(this+0x34, 0,
    0xff)`, and ReadHeader only overwrites an entry when the file's raw Id for
    it is non-zero -- so slot 0 (never written; the fill loop starts at 1) and
    any slot whose Id is 0 both resolve through objecttypemanager::GetType(0),
    i.e. the same single "type 0" class, whatever Factory does with it."""
    return slot == 0 or type_table_dict.get(slot, 0) == 0


class ParseError(Exception):
    pass


class ByteReader:
    """Wraps raw bytes with a cursor; mirrors BinaryReadSerializer /
    BinaryInvertReadSerializer (the latter byte-swaps every multi-byte read,
    for big-endian console content)."""

    def __init__(self, data: bytes, invert: bool = False):
        self.data = data
        self.pos = 0
        self.invert = invert

    def _take(self, n: int) -> bytes:
        if n < 0:
            raise ParseError(f"negative read length {n} at offset {self.pos}")
        if self.pos + n > len(self.data):
            raise ParseError(
                f"EOF: wanted {n} bytes at offset {self.pos}, only {len(self.data) - self.pos} left"
            )
        b = self.data[self.pos:self.pos + n]
        self.pos += n
        return b

    def u32(self) -> int:
        raw = self._take(4)
        if self.invert:
            raw = bytes((raw[3], raw[2], raw[1], raw[0]))
        return struct.unpack("<I", raw)[0]

    def u16(self) -> int:
        raw = self._take(2)
        if self.invert:
            raw = bytes((raw[1], raw[0]))
        return struct.unpack("<H", raw)[0]

    def u8(self) -> int:
        return self._take(1)[0]

    def bool_(self) -> bool:
        return self.u8() != 0

    def buf(self, n: int) -> bytes:
        if n == 0:
            return b""
        return self._take(n)

    def utf16(self, char_count: int) -> bytes:
        """reader+0x2c: raw UTF-16 code units, `char_count` of them."""
        return self.buf(char_count * 2)

    def f32(self) -> float:
        raw = self._take(4)
        if self.invert:
            raw = bytes((raw[3], raw[2], raw[1], raw[0]))
        return struct.unpack("<f", raw)[0]

    def eof(self) -> bool:
        return self.pos >= len(self.data)

    def remaining(self) -> int:
        return len(self.data) - self.pos


# ---------------------------------------------------------------------------
# Shared leaf records
# ---------------------------------------------------------------------------

def read_named_object(reader: ByteReader):
    """VisitNamedObject (0x0a05f840): one u32 name hash."""
    return reader.u32()


def read_full_link(reader: ByteReader):
    """VisitFullLink (0x0a0604d0):
        count : u16 (reader+0x14); if 0, the function returns immediately --
                no type byte, no ids.
        type_slot : u8 (reader+0x1c)
        count x id : u32 (reader+0xc)"""
    count = reader.u16()
    if count == 0:
        return {"type_slot": None, "ids": []}
    type_slot = reader.u8()
    return {"type_slot": type_slot, "ids": [reader.u32() for _ in range(count)]}


def read_string_resource_external_id(reader: ByteReader):
    """VisitStringResourceExternalId (0x0a05feb0): 2x u32."""
    return (reader.u32(), reader.u32())


def read_variant(reader: ByteReader):
    """VisitUserData's (0x0a062c90) per-property payload dispatch. It is a
    switch on the type tag: only the cases below consume bytes, and *every*
    other value -- enumerated in the switch or not -- is a legal, payload-less
    property. Never treat an unknown tag as an error."""
    type_tag = reader.u32()
    if type_tag == 2:
        payload = reader.u32()          # reader+0x8
    elif type_tag == 7:
        payload = reader.f32()          # reader+0x20
    elif type_tag == 0xC:
        payload = reader.bool_()        # reader+0x24
    elif type_tag == 0x10:
        length = reader.u32()
        payload = reader.buf(length) if length else b""
    elif type_tag in (0x11, 0x12, 0x15):
        payload = read_full_link(reader)
    elif type_tag == 0x13:
        payload = read_string_resource_external_id(reader)
    else:
        payload = None
    return type_tag, payload


def read_user_data(reader: ByteReader):
    """VisitUserData (0x0a062c90): VisitNamedObject, then a property count and
    that many [key:u32][tag:u32][payload] entries."""
    name_id = read_named_object(reader)
    count = reader.u32()
    entries = []
    for _ in range(count):
        key = reader.u32()
        type_tag, payload = read_variant(reader)
        entries.append((key, type_tag, payload))
    return name_id, entries


def read_resource_ref(reader: ByteReader):
    """The shared shape of LoadMaterial (0x0a0608c0) and LoadFontFamily
    (0x0a060f20) -- byte-identical functions differing only in which Package
    lookup they call afterwards:
        present : bool; if false, nothing else is read.
        id      : u32   -- the resource's name hash
        pkg_len : u32; if non-zero, pkg_len raw ANSI bytes naming the package
                  that owns it (empty => the current package)."""
    if not reader.bool_():
        return {"present": False}
    res_id = reader.u32()
    pkg_len = reader.u32()
    pkg_name = reader.buf(pkg_len) if pkg_len else b""
    return {"present": True, "id": res_id, "pkg_name": pkg_name}


def read_area_link(reader: ByteReader):
    """VisitAreaLink (0x0a0601c0):
        timing_type_slot : u8  -- resolved in-memory via the type remap table
        package_ref      : u32
        has_area_ref     : bool; if set -> area_ref : u32
        is_duplicate     : bool"""
    timing_type_slot = reader.u8()
    package_ref = reader.u32()
    has_area_ref = reader.bool_()
    area_ref = reader.u32() if has_area_ref else None
    return {
        "timing_type_slot": timing_type_slot,
        "package_ref": package_ref,
        "area_ref": area_ref,
        "is_duplicate": reader.bool_(),
    }


# ---------------------------------------------------------------------------
# Action / ActionExecuter family
# ---------------------------------------------------------------------------

def read_action_executer(reader: ByteReader, type_name):
    """VisitActionExecuter (0x0a05f870) plus, for the 8 named subtypes, the
    VisitActionExecuterEvent (0x0a05e840) tail.

    Base:  action_count : u32
           per action:   action_id : u32 (a raw CRC32 Id handed straight to
                         ActionServer::MakeAction -- not a type-table slot),
                         then the Action's own body. No concrete Action opcode
                         overrides Visit*, and VisitAction (0x0a05dd70)
                         forwards to VisitUserData, so every action is a plain
                         UserData record whatever its opcode.
    Event tail: group_count : u32
           per group: index_count : u32, then index_count x u32."""
    action_count = reader.u32()
    actions = []
    for _ in range(action_count):
        action_id = reader.u32()
        actions.append((action_id, read_user_data(reader)))
    result = {"action_count": action_count, "actions": actions}
    if type_name in ACTION_EXECUTER_EVENT_SUBTYPES:
        group_count = reader.u32()
        groups = []
        for _ in range(group_count):
            inner = reader.u32()
            groups.append([reader.u32() for _ in range(inner)])
        result["named_index_groups"] = groups
    return result


def read_action_caller(reader: ByteReader, slot_names: dict, type_table_dict: dict):
    """VisitActionCaller (0x0a05e910), called by Area, Element and Keyframe
    before their own fields:
        has_executer : bool; if false, nothing else is read.
        type_slot    : u8 -> Factory::MakeActionExecuter -> executer->Accept"""
    if not reader.bool_():
        return None
    type_slot = reader.u8()
    type_name = slot_names.get(type_slot)
    if type_name not in ACTION_EXECUTER_SUBTYPES:
        # An unresolved or type-0 slot lands on bare ActionExecuter's shape,
        # the only one with no extra tail.
        if type_name is not None and not is_empty_type_slot(type_slot, type_table_dict):
            raise ParseError(
                f"ActionCaller type slot {type_slot} resolves to {type_name!r}, "
                f"which Factory::MakeActionExecuter cannot construct, "
                f"at offset {reader.pos:#x}"
            )
        type_name = "ActionExecuter"
    body = read_action_executer(reader, type_name)
    body["type_slot"] = type_slot
    body["type_name"] = type_name
    return body


# ---------------------------------------------------------------------------
# Keyframe State hierarchy
#
#   State -> RotationState -> PosState  -> ScaleState
#                          -> RectState -> TextBaseState -> TextState
#                                       -> ImageState
#                                       -> RectShapeState
#
# Cumulative on-the-wire sizes: State 8, RotationState 16, PosState 20,
# ScaleState 28, RectState 24, TextBaseState 30, TextState 42, ImageState 65,
# RectShapeState 51.
# ---------------------------------------------------------------------------

def read_state(reader: ByteReader):
    """VisitState (0x0a05dc90): 2x u32 (reader+0xc).

    Names from LoadVisitor::ReadState (0x0a066400), which writes the same two
    object offsets: +0x08 `INTERPOLATIONFLAGS`, +0x10 `STATECOLOR` (authored as
    `%d %d %d %d`, i.e. packed RGBA). Earlier passes called these start/end and
    read them as a frame-time range; they are nothing of the sort."""
    return {"interpolation_flags": reader.u32(), "state_color": reader.u32()}


def read_rotation_state(reader: ByteReader):
    """VisitRotationState (0x0a060460): base VisitState, then `ROTATION`
    (float) and `ORIGIN` x/y (2x u16)."""
    r = read_state(reader)
    r["rotation"] = reader.f32()
    r["origin"] = (reader.u16(), reader.u16())
    return r


def read_pos_state(reader: ByteReader):
    """VisitPosState (0x0a060180): base VisitRotationState, then `POSITION` x/y."""
    r = read_rotation_state(reader)
    r["position"] = (reader.u16(), reader.u16())
    return r


def read_scale_state(reader: ByteReader):
    """VisitScaleState (0x0a05dcd0): base VisitPosState, then `SCALEX`, `SCALEY`."""
    r = read_pos_state(reader)
    r["scale"] = (reader.f32(), reader.f32())
    return r


def read_rect_state(reader: ByteReader):
    """VisitRectState (0x0a05fc20): base VisitRotationState, then 4x u16 to
    +0x24/+0x26/+0x28/+0x2a — `LEFT`, `RIGHT`, `TOP`, `BOTTOM`. Note that
    order: left/right/top/bottom, not the l/t/r/b anyone would guess."""
    r = read_rotation_state(reader)
    r["left"] = reader.u16()
    r["right"] = reader.u16()
    r["top"] = reader.u16()
    r["bottom"] = reader.u16()
    return r


def read_text_base_state(reader: ByteReader):
    """VisitTextBaseState (0x0a05dd20): base VisitRectState, then `OFFSETY`
    (float) and `ABSOFFSETY` (u16)."""
    r = read_rect_state(reader)
    r["offset_y"] = reader.f32()
    r["abs_offset_y"] = reader.u16()
    return r


def read_text_state(reader: ByteReader):
    """VisitTextState (0x0a05fb70): base VisitTextBaseState, then `SHADOWCOLOR`,
    `HEIGHT` (read as u16, stored as float), `SHADOWOFFSETX`/`Y`, `LEADING`,
    `TRACKING`. (`TEXTCOLOR` in the XML is the inherited `STATECOLOR` under
    another name — not a field of its own.)"""
    r = read_text_base_state(reader)
    r["shadow_color"] = reader.u32()
    r["height"] = reader.u16()
    r["shadow_offset_x"] = reader.u8()
    r["shadow_offset_y"] = reader.u8()
    r["leading"] = reader.u16()
    r["tracking"] = reader.u16()
    return r


def read_image_state(reader: ByteReader):
    """VisitImageState (0x0a05fa20): base VisitRectState, then `SHADOWCOLOR`,
    `SHADOWOFFSETX`/`Y`, `TILING` x/y and `OFFSET` x/y (4 floats),
    `FLIPHORIZONTAL`/`FLIPVERTICAL`/`ACTUALSIZE` (3 bools), and `COLOR1`..
    `COLOR4` — the four corner colours of a gradient quad."""
    r = read_rect_state(reader)
    r["shadow_color"] = reader.u32()
    r["shadow_offset_x"] = reader.u8()
    r["shadow_offset_y"] = reader.u8()
    r["tiling"] = (reader.f32(), reader.f32())
    r["offset"] = (reader.f32(), reader.f32())
    r["flip_horizontal"] = reader.bool_()
    r["flip_vertical"] = reader.bool_()
    r["actual_size"] = reader.bool_()
    r["colors"] = tuple(reader.u32() for _ in range(4))
    return r


def read_rect_shape_state(reader: ByteReader):
    """VisitRectShapeState (0x0a05f950): base VisitRectState, then
    `OUTLINEWEIGHT`, `OUTLINECOLOR`, `FILLCOLOR1`..`FILLCOLOR4`, `SHADOWCOLOR`,
    `SHADOWOFFSETX`/`Y`."""
    r = read_rect_state(reader)
    r["outline_weight"] = reader.u8()
    r["outline_color"] = reader.u32()
    r["fill_colors"] = tuple(reader.u32() for _ in range(4))
    r["shadow_color"] = reader.u32()
    r["shadow_offset_x"] = reader.u8()
    r["shadow_offset_y"] = reader.u8()
    return r


STATE_READERS = {
    "State": read_state,
    "RotationState": read_rotation_state,
    "PosState": read_pos_state,
    "ScaleState": read_scale_state,
    "RectState": read_rect_state,
    "TextBaseState": read_text_base_state,
    "TextState": read_text_state,
    "ImageState": read_image_state,
    "RectShapeState": read_rect_shape_state,
}


def read_keyframe(reader: ByteReader, slot_names: dict, type_table_dict: dict, state_name: str):
    """VisitKeyframe (0x0a05ea90):
        VisitNamedObject -> name_id : u32
        VisitActionCaller
        `IDX`           : u32 (stored u16) -- the frame index
        `INTERPOLATION` : u32 -- a timing-strategy type id (the XML authors it
                          as a class name resolved through Util::GetType)
        then the concrete State's Accept. Which State class that is comes from
        Factory::MakeKeyframe's ObjectTypeInfo argument, which VisitElement
        derives from the owning widget's class -- never from the stream."""
    result = {"name_id": read_named_object(reader)}
    result["action"] = read_action_caller(reader, slot_names, type_table_dict)
    result["idx"] = reader.u32()
    result["interpolation"] = reader.u32()
    result["state_type"] = state_name
    result["state"] = STATE_READERS[state_name](reader)
    return result


# ---------------------------------------------------------------------------
# Widget bodies -- read from the tail of VisitElement, via widget->Accept
# ---------------------------------------------------------------------------

def read_placeholder(reader: ByteReader):
    """Placeholder has no Visit override; Visitor::VisitPlaceholder
    (0x09606ae0) is an empty no-op. Zero bytes."""
    return {}


def read_rect_shape(reader: ByteReader):
    """VisitRectShape (0x0a05db40): `ISOUTLINED`, `ISFILLED`, `BLENDINGMODE`
    (read as u32, only the low byte kept)."""
    return {
        "is_outlined": reader.bool_(),
        "is_filled": reader.bool_(),
        "blending_mode": reader.u32() & 0xFF,
    }


def read_image(reader: ByteReader):
    """VisitImage (0x0a060e80): `MATERIALLINK`, `BLENDINGMODE`,
    `ALPHABLENDFIRST`, then `ADDRESSINGMODEU`/`ADDRESSINGMODEV` (two u32s that
    supply the low and high nibbles of one packed byte)."""
    return {
        "material": read_resource_ref(reader),
        "blending_mode": reader.u32() & 0xFF,
        "alpha_blend_first": reader.bool_(),
        "addressing_mode_u": reader.u32() & 0xF,
        "addressing_mode_v": reader.u32() & 0xF,
    }


def read_text_base(reader: ByteReader):
    """VisitTextBase (0x0a0616d0): base Visitor::VisitWidget (no-op), then
        localized : bool -- selects between a string-table reference and inline text
          if true  -> `TABLEID` : u32, `RESOURCEID` : u32
          if false -> char_count : u32, then char_count UTF-16 code units (`STRING`)
        `ALIGNMENTX`, `ALIGNMENTY` : u32, u32
        `WRAPPING`, `CLIPPING`, `ELLIPSIS`, `AUTOSIZED` : 4x bool
        has_slider_link : bool; if set -> `SLIDERLINK` : u32"""
    result = {}
    localized = reader.bool_()
    result["localized"] = localized
    if localized:
        result["table_id"] = reader.u32()
        result["resource_id"] = reader.u32()
    else:
        char_count = reader.u32()
        result["string"] = reader.utf16(char_count)
    result["alignment_x"] = reader.u32()
    result["alignment_y"] = reader.u32()
    result["wrapping"] = reader.bool_()
    result["clipping"] = reader.bool_()
    result["ellipsis"] = reader.bool_()
    result["auto_sized"] = reader.bool_()
    result["slider_link"] = reader.u32() if reader.bool_() else None
    return result


def read_text(reader: ByteReader):
    """VisitText (0x0a0610e0): base VisitTextBase, then `LoadFontFamily`,
    `BOLD`, `ITALICS`, `UNDERLINED`, `BLENDINGMODE`, `ALPHABLENDFIRST`."""
    result = read_text_base(reader)
    result["font_family"] = read_resource_ref(reader)
    result["bold"] = reader.bool_()
    result["italics"] = reader.bool_()
    result["underlined"] = reader.bool_()
    result["blending_mode"] = reader.u32()
    result["alpha_blend_first"] = reader.bool_()
    return result


def read_area_instance(reader: ByteReader):
    """VisitAreaInstance (0x0a060a80), shared unchanged by the whole
    AutonomousAreaInstance/ButtonInstance/CheckBoxInstance/RadioButtonInstance
    chain (each is a zero-field forward):
        base Visitor::VisitWidget (no-op)
        char_count : u32, then char_count UTF-16 code units (the target name)
        LoadMaterial
        has_link : bool; if set -> AreaLink
        final : u32"""
    char_count = reader.u32()
    name = reader.utf16(char_count)
    material = read_resource_ref(reader)
    link = read_area_link(reader) if reader.bool_() else None
    return {"name": name, "material": material, "link": link,
            "final_value": reader.u32()}


def read_page_instance(reader: ByteReader):
    """VisitPageInstance (0x0a05f3f0): base VisitAreaInstance, then
        count : u32, then count x (u8, u8, u32) default focus tags."""
    result = read_area_instance(reader)
    count = reader.u32()
    result["focus_tags"] = [(reader.u8(), reader.u8(), reader.u32())
                            for _ in range(count)]
    return result


def read_list_box(reader: ByteReader):
    """VisitListBox (0x0a05f680): base Visitor::VisitWidget (no-op), then
        sort_mode : u8 (reader+0x18)
        4x bool
        timing_slot : u8 (reader+0x1c)
        metrics : u32
        has_extra : bool; if set -> u32
        3x [has_link : bool; if set -> AreaLink]  (the +0x34/+0x50/+0x6c links)"""
    result = {
        "sort_mode": reader.u8(),
        "flags": tuple(reader.bool_() for _ in range(4)),
        "timing_slot": reader.u8(),
        "metrics": reader.u32(),
    }
    result["extra"] = reader.u32() if reader.bool_() else None
    result["links"] = [read_area_link(reader) if reader.bool_() else None
                       for _ in range(3)]
    return result


def read_edit_box(reader: ByteReader):
    """VisitEditBox (0x0a05ec80): base Visitor::VisitWidget (no-op), then
        max_length : u32 (only the low 16 bits are stored)
        has_password_char : bool; if set -> 1 UTF-16 code unit
        2x [has_link : bool; if set -> AreaLink]  (the +0x58/+0x74 links)"""
    result = {"max_length": reader.u32()}
    result["password_char"] = reader.utf16(1) if reader.bool_() else None
    result["links"] = [read_area_link(reader) if reader.bool_() else None
                       for _ in range(2)]
    return result


def read_slider(reader: ByteReader):
    """VisitSlider (0x0a05eb10): base Visitor::VisitWidget (no-op), then
        5x u32 (min, max, and three more -- SetRange takes the first two)
        1x bool
        4x [has_link : bool; if set -> AreaLink]  (+0x54/+0x70/+0x8c/+0xa8)"""
    result = {
        "range": tuple(reader.u32() for _ in range(5)),
        "flag": reader.bool_(),
    }
    result["links"] = [read_area_link(reader) if reader.bool_() else None
                       for _ in range(4)]
    return result


def read_window_section(reader: ByteReader, stretchable: bool):
    """ReadWindowSection (0x0a060c40): LoadMaterial, u32 blending mode, 4x bool.
    ReadStretchableWindowSection (0x0a060d20) appends one more u32 (stretch mode)."""
    section = {
        "material": read_resource_ref(reader),
        "blending_mode": reader.u32(),
        "flags": tuple(reader.bool_() for _ in range(4)),
    }
    if stretchable:
        section["stretch_mode"] = reader.u32()
    return section


def read_window(reader: ByteReader):
    """VisitWindow (0x0a060d70): base Visitor::VisitWidget (no-op), 2x bool,
    then nine 9-patch sections. Sections 0 and 5-8 are stretchable; 1-4 are not."""
    result = {"stretch_flags": (reader.bool_(), reader.bool_())}
    result["sections"] = [
        read_window_section(reader, stretchable=(i == 0 or i >= 5))
        for i in range(9)
    ]
    return result


WIDGET_BODY_READERS = {
    "Placeholder": read_placeholder,
    "Image": read_image,
    "Text": read_text,
    "RectShape": read_rect_shape,
    "AreaInstance": read_area_instance,
    "AutonomousAreaInstance": read_area_instance,
    "ButtonInstance": read_area_instance,
    "CheckBoxInstance": read_area_instance,
    "RadioButtonInstance": read_area_instance,
    "PageInstance": read_page_instance,
    "ListBox": read_list_box,
    "EditBox": read_edit_box,
    "Slider": read_slider,
    "Window": read_window,
}


# ---------------------------------------------------------------------------
# Element list
# ---------------------------------------------------------------------------

def read_focusable_tail(reader: ByteReader):
    """VisitFocusable (0x0a05fc80), after its base VisitElement call:
        neighbor count : u32 (`NEIGHBORS`/`COUNT`)
        per `NEIGHBOR` : `CONTROLLER` u8, `DIRECTION` u8, `ID` u32
        `INPUTFILTER`  : bool -- Focusable::SetInputController
    VisitPageFocusable/VisitCheckable/VisitRadioable are pure forwards to it."""
    neighbor_count = reader.u32()
    neighbors = [
        {"controller": reader.u8(), "direction": reader.u8(), "id": reader.u32()}
        for _ in range(neighbor_count)
    ]
    return {"neighbors": neighbors, "input_filter": reader.u8()}


def read_element(reader: ByteReader, slot_names: dict, type_table_dict: dict):
    """One entry of an Area's element list.

    VisitArea reads a u8 type slot, resolves it to a *widget* class, and hands
    it to Factory::MakeElement, which builds an Element (or a Focusable /
    PageFocusable / Checkable / Radioable, per WIDGET_WRAPPER) wrapping a
    widget of that class. Element::Accept then runs VisitElement
    (0x0a060290):

        VisitUserData                          (name hash + property list)
        VisitActionCaller
        2x bool (hidden flag, a second flag)
        u32 (low 3 bits are a category enum)
        u32 keyframe_count, then that many Keyframes
        widget->Accept(visitor)                <- the widget's own fields

    That trailing widget->Accept is easy to miss: VisitElement's decompile
    shows no serializer calls after the keyframe loop, but the call dispatches
    straight into VisitImage/VisitText/VisitAreaInstance/... which do read.
    The wrapper's own tail (Focusable's neighbour list) comes last of all,
    because VisitFocusable calls VisitElement *first*."""
    type_slot = reader.u8()
    widget_name = slot_names.get(type_slot)
    wrapper = WIDGET_WRAPPER.get(widget_name)
    if wrapper is None:
        raise ParseError(
            f"element type slot {type_slot} resolves to {widget_name!r}, which is not "
            f"one of the 14 widget classes Factory::MakeElement can construct, "
            f"at offset {reader.pos - 1:#x}"
        )

    result = {"type_slot": type_slot, "type_name": widget_name, "wrapper": wrapper}
    result["name_id"], result["user_data"] = read_user_data(reader)
    result["action"] = read_action_caller(reader, slot_names, type_table_dict)
    result["hidden"] = reader.bool_()
    result["is_duplicatable"] = reader.bool_()
    result["mask_mode"] = reader.u32() & 0x7
    keyframe_count = reader.u32()
    result["keyframe_count"] = keyframe_count
    state_name = WIDGET_STATE[widget_name]
    result["keyframes"] = [
        read_keyframe(reader, slot_names, type_table_dict, state_name)
        for _ in range(keyframe_count)
    ]
    result["widget"] = WIDGET_BODY_READERS[widget_name](reader)
    if wrapper != "Element":
        result.update(read_focusable_tail(reader))
    return result


# ---------------------------------------------------------------------------
# Area list
# ---------------------------------------------------------------------------

def read_area_body(reader: ByteReader, slot_names: dict, type_table_dict: dict):
    """VisitArea (0x0a05f4b0), shared by every Area subtype:
        VisitUserData
        VisitActionCaller
        `FRAMERATE`    : u32 (the engine stores 1000 / this)
        `CURRENTFRAME` : u32
        element count  : u32 (`CHILDREN`/`COUNT` in the XML)
        element_count x [u8 type slot + the element's body]
        `STATICBOX`    : 4x u16 -- Area::SetStaticBox"""
    result = {}
    result["name_id"], result["user_data"] = read_user_data(reader)
    result["action"] = read_action_caller(reader, slot_names, type_table_dict)
    result["frame_rate"] = reader.u32()
    result["current_frame"] = reader.u32()
    element_count = reader.u32()
    result["element_count"] = element_count
    result["elements"] = [
        read_element(reader, slot_names, type_table_dict)
        for _ in range(element_count)
    ]
    result["static_box"] = tuple(reader.u16() for _ in range(4))
    return result


def read_page_tail(reader: ByteReader):
    """VisitPage (0x0a05fd60), after the shared VisitArea body:
        tag_count : u32, then per tag u8, u32
        global_selection_mode : bool"""
    tag_count = reader.u32()
    tags = [(reader.u8(), reader.u32()) for _ in range(tag_count)]
    return {"tags": tags, "global_selection_mode": reader.bool_()}


def read_cursor_tail(reader: ByteReader):
    """VisitCursor (0x0a05dec0): 2x u16, stored negated -- a hotspot offset."""
    def signed16(v):
        return v - 0x10000 if v >= 0x8000 else v
    a, b = reader.u16(), reader.u16()
    return {"hotspot": (-signed16(a), -signed16(b))}


def read_button_tail(reader: ByteReader):
    """VisitButton (0x0a060410): 6x u32 (the loop runs to offset 0x18)."""
    return {"timings": [reader.u32() for _ in range(6)]}


def read_checkbox_tail(reader: ByteReader):
    """VisitCheckBox (0x0a05df20): 12x u32 (the loop runs to offset 0x30) --
    Button's six plus six more for the checked state."""
    return {"timings": [reader.u32() for _ in range(12)]}


AREA_TAIL_READERS = {
    "Area": lambda r: {},
    "Page": read_page_tail,
    "Cursor": read_cursor_tail,
    "Button": read_button_tail,
    "CheckBox": read_checkbox_tail,
}


def read_area(reader: ByteReader, slot_names: dict, type_table_dict: dict):
    """One entry of the package's top-level area list: a u8 type slot resolved
    through Factory::MakeArea (0x0a0480a0), which accepts only Area, Page,
    Button, Cursor and CheckBox (and their descendants)."""
    type_slot = reader.u8()
    type_name = slot_names.get(type_slot)
    if type_name not in AREA_SUBTYPES:
        raise ParseError(
            f"area type slot {type_slot} resolves to {type_name!r}, which is not one of "
            f"the 5 classes Factory::MakeArea can construct, at offset {reader.pos - 1:#x}"
        )
    result = read_area_body(reader, slot_names, type_table_dict)
    result.update(AREA_TAIL_READERS[type_name](reader))
    result["type_slot"] = type_slot
    result["type_name"] = type_name
    return result


# ---------------------------------------------------------------------------
# Package-level records
# ---------------------------------------------------------------------------

def read_material(reader: ByteReader):
    """VisitMaterial (0x0a0606a0): VisitNamedObject, a length-prefixed ANSI
    texture path, then 4 floats (a UV region rect)."""
    name_id = read_named_object(reader)
    tex_len = reader.u32()
    tex_name = reader.buf(tex_len) if tex_len else b""
    region = tuple(reader.f32() for _ in range(4))
    return {"name_id": name_id, "tex_name": tex_name, "region": region}


def read_len_prefixed_str(reader: ByteReader) -> bytes:
    n = reader.u32()
    return reader.buf(n) if n else b""


def read_font_subst_entry(reader: ByteReader):
    """VisitPackage's first font loop: a font type slot, an embedded font blob
    (handed to Font::Load, which parses it out of the already-read buffer and
    never touches the stream), then the substitution name it registers under."""
    type_slot = reader.u8()
    font_data = read_len_prefixed_str(reader)
    subst_name = read_len_prefixed_str(reader)
    return {"type_slot": type_slot, "font_data_len": len(font_data),
            "subst_name": subst_name}


def read_font_ref_entry(reader: ByteReader):
    """VisitPackage's second font loop: a font type slot and two
    length-prefixed names (looked up via Package::GetFontSubst, else
    FontServer::RequestFont)."""
    return {"type_slot": reader.u8(),
            "name1": read_len_prefixed_str(reader),
            "name2": read_len_prefixed_str(reader)}


def read_font_family(reader: ByteReader):
    """VisitFontFamily (0x0a0615a0): VisitNamedObject then LoadFont
    (0x0a061300) -- a length-prefixed font name and, only if that name is
    non-empty, a length-prefixed owning-package name."""
    name_id = read_named_object(reader)
    font_name = read_len_prefixed_str(reader)
    pkg_name = read_len_prefixed_str(reader) if font_name else b""
    return {"name_id": name_id, "font_name": font_name, "pkg_name": pkg_name}


def read_string_resource(reader: ByteReader):
    """VisitStringResource (0x0a0611c0): VisitNamedObject, then a u32 character
    count and that many UTF-16 code units."""
    name_id = read_named_object(reader)
    char_count = reader.u32()
    return {"name_id": name_id, "text": reader.utf16(char_count)}


def read_string_table(reader: ByteReader):
    """The package's optional string table -- VisitPackage builds it via
    Factory::MakeStringTable, not via the type table, which is why earlier
    passes could only describe it as an anonymous "focus area".
    VisitStringTable (0x0a05e9a0): VisitNamedObject, u32 count, count x
    StringResource."""
    name_id = read_named_object(reader)
    count = reader.u32()
    return {"name_id": name_id,
            "strings": [read_string_resource(reader) for _ in range(count)]}


def read_generic_object(reader: ByteReader):
    """VisitGenericObject (0x0a05de70): VisitNamedObject then a FullLink."""
    return {"name_id": read_named_object(reader), "link": read_full_link(reader)}


def read_generic_object_table(reader: ByteReader):
    """The package's optional generic-object table (Factory::MakeGenericObjectTable,
    the second anonymous trailing object). VisitGenericObjectTable (0x0a05e7c0):
    VisitNamedObject, u32 count, count x GenericObject."""
    name_id = read_named_object(reader)
    count = reader.u32()
    return {"name_id": name_id,
            "objects": [read_generic_object(reader) for _ in range(count)]}


class MgbHeader:
    def __init__(self):
        self.invert = False
        self.endian_marker_byte = None
        self.version = None
        self.flag = None
        self.type_table_count = None
        self.type_table = []   # list of (slot_index, raw_external_id)
        self.pool_counts = []  # 65 u32 pool pre-reservation counts
        self.name_id = None
        self.user_data = []
        self.dims = None       # PAGESIZE (w, h) + DISPLAYOFFSET (x, y)
        self.material_count = None
        self.some_count_2 = None
        self.materials = []
        self.font_substs = []
        self.font_refs = []
        self.font_families = []
        self.area_count = None
        self.areas = []
        self.area_error = None
        self.string_table = None
        self.generic_object_table = None
        self.default_material_name = None


def parse_header(reader: ByteReader) -> MgbHeader:
    """ReadHeader (0x0a05fef0) followed by VisitPackage (0x0a0619e0)."""
    hdr = MgbHeader()

    magic = reader.buf(5)
    if magic != MAGIC:
        raise ParseError(f"bad magic: {magic!r}")

    # A 4-byte read whose 4th byte is the endian marker; if it isn't 0xAB the
    # engine swaps in BinaryInvertReadSerializer and re-checks the 1st byte.
    marker_bytes = reader.buf(4)
    hdr.endian_marker_byte = marker_bytes[3]
    if marker_bytes[3] != ENDIAN_MARKER_NORMAL:
        reader.invert = True
        hdr.invert = True
        if marker_bytes[0] != ENDIAN_MARKER_NORMAL:
            raise ParseError(
                f"endian-invert sanity byte mismatch: {marker_bytes[0]:#x} != 0xAB"
            )

    hdr.version = reader.u32()
    if hdr.version != VERSION:
        raise ParseError(f"version mismatch: {hdr.version:#x} != {VERSION:#x}")

    hdr.flag = reader.bool_()

    # A single byte count, then count-1 raw Ids: the fill loop runs slot 1..N-1,
    # so slot 0 is never populated and a body type byte B names table entry B-1.
    hdr.type_table_count = reader.u8()
    for slot in range(1, hdr.type_table_count):
        ext_id = reader.u32()
        if ext_id != 0:
            hdr.type_table.append((slot, ext_id))

    # --- VisitPackage ---
    # 65 u32s feeding the Allocate*PoolChunk family: pure pre-reservation
    # sizing, no effect on any later offset.
    hdr.pool_counts = [reader.u32() for _ in range(65)]

    hdr.name_id, hdr.user_data = read_user_data(reader)

    # PAGESIZE (u16 w, u16 h) then DISPLAYOFFSET (u16 x, u16 y).
    hdr.dims = tuple(reader.u16() for _ in range(4))

    hdr.material_count = reader.u32()
    hdr.some_count_2 = reader.u32()
    hdr.materials = [read_material(reader) for _ in range(hdr.material_count)]

    font_count = reader.u32()
    hdr.font_substs = [read_font_subst_entry(reader) for _ in range(font_count)]

    font_ref_count = reader.u32()
    hdr.font_refs = [read_font_ref_entry(reader) for _ in range(font_ref_count)]

    font_family_count = reader.u32()
    hdr.font_families = [read_font_family(reader) for _ in range(font_family_count)]

    hdr.area_count = reader.u32()
    slot_names = build_slot_type_names(hdr.type_table)
    type_table_dict = dict(hdr.type_table)
    for _ in range(hdr.area_count):
        try:
            hdr.areas.append(read_area(reader, slot_names, type_table_dict))
        except ParseError as e:
            hdr.area_error = str(e)
            break

    if hdr.area_error is None:
        try:
            if reader.bool_():
                hdr.string_table = read_string_table(reader)
            if reader.bool_():
                hdr.generic_object_table = read_generic_object_table(reader)
            name_len = reader.u32()
            hdr.default_material_name = reader.buf(name_len) if name_len else None
            # End of file-consuming reads. Everything VisitPackage does past
            # this point (ResolveLinks, the duplication/instancing passes, the
            # second Allocate*PoolChunk sweep) is pure in-memory post-processing.
        except ParseError as e:
            hdr.area_error = str(e)

    return hdr


def parse_file(path: str):
    with open(path, "rb") as f:
        data = f.read()
    reader = ByteReader(data)
    hdr = parse_header(reader)
    return hdr, reader


def main(argv):
    paths = argv[1:]
    if not paths:
        paths = sorted(glob.glob("*.mgb"))

    for path in paths:
        name = os.path.basename(path)
        try:
            hdr, reader = parse_file(path)
        except ParseError as e:
            print(f"{name:30s} FAILED: {e}")
            continue

        size = len(reader.data)
        status = "OK " if (hdr.area_error is None and reader.pos == size) else "BAD"
        print(
            f"{name:30s} {status} invert={hdr.invert!s:5s} types={hdr.type_table_count:3d} "
            f"dims={hdr.dims} materials={hdr.material_count:5d} "
            f"fonts={len(hdr.font_substs)}/{len(hdr.font_refs)}/{len(hdr.font_families)} "
            f"areas={len(hdr.areas)}/{hdr.area_count} "
            f"pos=0x{reader.pos:05x}/0x{size:05x}"
        )
        if hdr.area_error:
            print(f"{'':30s}     error: {hdr.area_error}")
        elif reader.pos != size:
            print(f"{'':30s}     {size - reader.pos} trailing bytes not consumed")


if __name__ == "__main__":
    main(sys.argv)
