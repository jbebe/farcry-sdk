using JackAll.Core.Vfs;
using JackAll.Core.Xrefs;
using JackAll.Tools.Spk;

namespace JackAll.Tools.Xrefs;

/// <summary>
/// References inside an `.spk` sound bank: each record's own id (a definition), and the id-reference
/// fields its sub-headers carry.
/// </summary>
/// <remarks>
/// The definitions are the more valuable half here. `.spk` record ids and `.sbao` file ids share one
/// id space (see <see cref="SpkPackage"/>'s remarks - they overlap at noise level across a whole
/// install, i.e. they're mutually exclusive storage paths for the same ids), so recording which bank
/// owns which id is what lets a reference to a sound resolve to a file at all. Without it, every
/// audio id in the graph would be a dead end.
///
/// Only the fields the format page actually calls id-references are indexed. The `.spk` sub-headers
/// have several other words whose meaning is unconfirmed; treating those as references would
/// manufacture edges from what may well be gain values or frame counts.
/// </remarks>
public sealed class SpkReferenceExtractor : IReferenceExtractor
{
    public bool CanHandle(VfsFile file) => file.Type.Extension is "spk";

    public void Extract(VfsFile file, byte[] content, ReferenceSink sink)
    {
        SpkPackage package = SpkPackage.Parse(content);

        foreach (SpkRecord record in package.Records)
        {
            sink.Define(RefSpace.SoundResource, record.Id, sink.Intern("spk record"));

            // The record's own id is the site: an xref row can then say which record inside this
            // bank holds the link, which is the only way to find it again in a bank of hundreds.
            if (record.SimpleFixed68 is { } simple)
            {
                // Only a leaf event's word[2] is a link; a composite event reuses that word as a byte
                // offset into its child list, so indexing it there recorded an edge to id 0 and missed
                // every real child. The children are the edges that matter: they're what makes a bank
                // holding no audio of its own reach the bank that does.
                if (simple.LinkedId is { } linkedId)
                {
                    sink.Add(RefSpace.SoundResource, linkedId, RefKind.SpkRecordLink, record.Id);
                }

                foreach (uint childId in simple.ChildIds)
                {
                    sink.Add(RefSpace.SoundResource, childId, RefKind.SpkEventChild, record.Id);
                }

                sink.Add(RefSpace.SoundResource, simple.CategoryId, RefKind.SpkCategory, record.Id);
            }

            if (record.TransformedFixed128 is { } transformed)
            {
                sink.Add(RefSpace.SoundResource, transformed.FlatCopySiblingId, RefKind.SpkFlatCopySibling, record.Id);
            }
        }
    }
}

/// <summary>
/// The `.sbao` half of the same id space: a standalone sound object *is* one resource, so the file
/// defines exactly one id - its own, taken from the filename.
/// </summary>
/// <remarks>
/// The id lives in the name (<c>soundbinary\&lt;id:08x&gt;.sbao</c>), not in the header, so a file
/// whose name was never recovered contributes no definition. That's correct rather than a gap: an
/// unnamed `.sbao` genuinely can't be tied to an id by anything the tool can see.
/// </remarks>
public sealed class SbaoReferenceExtractor : IReferenceExtractor
{
    public bool CanHandle(VfsFile file) => file.Type.Extension is "sbao" or "bao";

    public void Extract(VfsFile file, byte[] content, ReferenceSink sink)
    {
        if (!file.NameIsKnown)
        {
            return;
        }

        string stem = Path.GetFileNameWithoutExtension(file.FileName);
        if (uint.TryParse(stem, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint id))
        {
            sink.Define(RefSpace.SoundResource, id, sink.Intern("sbao file"));
        }
    }
}
