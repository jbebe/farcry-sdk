using JackAll.Core.Format;

namespace JackAll.Core.Tests;

/// <summary>
/// A hand-built, minimal-but-valid .mgb file exercising <see cref="MgbBody"/>'s <c>UserData</c>
/// "external string resource" property types (<c>0x11</c>-<c>0x15</c>) - these don't appear in any
/// small real sample checked into this repo (the file that surfaced the original bug,
/// <c>ingameeditor.mgb</c>, is a real game asset, not something to embed here), so this is a synthetic
/// regression test for the <c>VisitFullLink</c>/<c>VisitStringResourceExternalId</c> fix instead. See
/// reverse/dunia/mgb_format.md's "Reader-vtable slots" for the wire formats being exercised.
/// </summary>
public class MgbSyntheticTests
{
    [Fact]
    public void Decodes_UserData_FullLink_and_StringResourceExternalId_properties()
    {
        var w = new ByteWriter();

        // Header: magic, sentinel, version, flag, an empty type table (count byte 1 => 0 entries).
        w.Ascii("MAGMA");
        w.U8(0xCD); w.U8(0x00); w.U8(0x00); w.U8(0xAB);
        w.U32(MgbHeader.ExpectedVersion);
        w.U8(0x00); // flag byte
        w.U8(0x01); // type count byte -> 0 entries

        // Package preamble.
        for (int i = 0; i < 65; i++) w.U32(0); // config block

        w.U32(0xAAAAAAAA); // UserData NamedObject name hash
        w.U32(2);          // UserData property count

        w.U32(0x11111111); w.U32(0x12);           // key, type=0x12 -> FullLink
        w.U16(0);                                  // FullLink entryCount = 0
        w.U8(5);                                    // FullLink typeId (unresolved, out of range - must not throw)

        w.U32(0x22222222); w.U32(0x13);           // key, type=0x13 -> StringResourceExternalId
        w.U32(0x33333333); w.U32(0x44444444);      // its 2 raw u32s

        w.U16(100); w.U16(200); // PAGESIZE
        w.U16(10); w.U16(20);   // DISPLAYOFFSET
        w.U32(0); w.U32(0);     // materialCount, materialUnknownField
        w.U32(0);               // fontSubstCount
        w.U32(0);               // fontDeclCount
        w.U32(0);               // fontFamilyCount
        w.U32(0);               // areaCount
        w.U8(0);                // hasGlobalFocusArea
        w.U8(0);                // hasSecondArea
        w.U32(0);                // defaultMaterialNameLen

        byte[] content = w.ToArray();
        MgbHeader header = MgbHeader.Decode(content);
        Assert.Empty(header.Types);

        MgbNode root = MgbBody.ParsePackage(content, header);
        Assert.Equal("Package", root.Kind);
        Assert.Equal("100 x 200", root.Fields.First(f => f.Label == "PageSize").Value);

        MgbNode userData = root.Children.First(c => c.Kind == "UserData");
        string fullLink = userData.Fields.First(f => f.Label.StartsWith("Property[0x11111111]")).Value;
        Assert.StartsWith("FullLink<", fullLink);
        Assert.Contains("out of range", fullLink); // typeId 5 with an empty type table

        string stringResource = userData.Fields.First(f => f.Label.StartsWith("Property[0x22222222]")).Value;
        Assert.Equal("StringResourceExternalId(0x33333333, 0x44444444)", stringResource);

        // The whole point of the fix: parsing must land exactly on the last byte written, not drift.
        Assert.DoesNotContain(root.Fields, f => f.Label == "StoppedDecoding");
        Assert.Equal($"{content.Length - header.HeaderLength:N0} (file has {content.Length - header.HeaderLength:N0} body bytes total)",
            root.Fields.First(f => f.Label == "BytesConsumed").Value);
    }

    private sealed class ByteWriter
    {
        private readonly List<byte> _bytes = [];

        public void U8(byte b) => _bytes.Add(b);
        public void U16(ushort v) { _bytes.Add((byte)v); _bytes.Add((byte)(v >> 8)); }
        public void U32(uint v) { _bytes.Add((byte)v); _bytes.Add((byte)(v >> 8)); _bytes.Add((byte)(v >> 16)); _bytes.Add((byte)(v >> 24)); }
        public void Ascii(string s) { foreach (char c in s) _bytes.Add((byte)c); }
        public byte[] ToArray() => [.. _bytes];
    }
}
