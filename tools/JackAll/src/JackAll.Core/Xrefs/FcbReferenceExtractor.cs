using System.Xml.Linq;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Rml;
using JackAll.Core.Vfs;

namespace JackAll.Core.Xrefs;

/// <summary>
/// Pulls references out of a `.fcb` object tree: <c>String</c> values that name a game asset, and
/// <c>Hash</c>/<c>HashArray</c> values, which are name hashes by definition.
/// </summary>
/// <remarks>
/// The walk mirrors <see cref="FcbXml.ToXml"/>'s own (<c>WriteObject</c>): a child object's class is
/// resolved against its *parent's* scope, not the flat top-level table, because the shipped
/// <c>binary_classes.xml</c> really does shadow class names per parent. Getting that wrong would
/// silently mistype values in exactly the nested objects most likely to hold references.
///
/// **Class and member key hashes are deliberately not indexed.** Every `.fcb` in the game carries a
/// <c>Name</c> member and a <c>CEntity</c>-ish type, so indexing those would add tens of millions of
/// edges that answer no question anyone would ask - and <c>binary_classes.xml</c> already names them
/// far better than an xref list could. Only *values* become edges.
/// </remarks>
public sealed class FcbReferenceExtractor : IReferenceExtractor
{
    public bool CanHandle(VfsFile file) => file.Type.Extension is "fcb";

    public void Extract(VfsFile file, byte[] content, ReferenceSink sink)
    {
        FcbObject root = FcbDocument.Deserialize(content);
        Walk(root, sink.Classes, sink);
    }

    private static void Walk(FcbObject obj, IFcbClassScope scope, ReferenceSink sink)
    {
        FcbClass ownClass = scope.Resolve(obj.TypeHash);

        foreach ((uint nameHash, byte[] value) in obj.Values)
        {
            FcbMember? member = ownClass.FindMember(nameHash);
            if (member is null)
            {
                // No declared type means the bytes are opaque (FcbXml falls back to BinHex here too).
                // Guessing - "4 bytes, might be a hash" - would flood the index with coincidences.
                continue;
            }

            // A member the config names gets that name as its site; one known only by hash keeps the
            // hash, and the xref list renders it as #XXXXXXXX.
            uint site = member.Name is { } memberName ? sink.Intern(memberName) : nameHash;
            Emit(member.Type, value, site, sink);
        }

        foreach (FcbObject child in obj.Children)
        {
            Walk(child, ownClass, sink);
        }
    }

    private static void Emit(FcbMemberType type, byte[] value, uint site, ReferenceSink sink)
    {
        switch (type)
        {
            case FcbMemberType.String:
                if (FcbValueCodec.TryDecode(type, value, out object text))
                {
                    sink.AddPath((string)text, RefKind.FcbPathValue, site);
                }
                break;

            case FcbMemberType.Hash:
                if (FcbValueCodec.TryDecode(type, value, out object hash))
                {
                    sink.Add(RefSpace.EngineName, (uint)hash, RefKind.FcbNameValue, site);
                }
                break;

            case FcbMemberType.HashArray:
                if (FcbValueCodec.TryDecode(type, value, out object array))
                {
                    var hashes = (uint[])array;
                    for (int i = 0; i < hashes.Length; i++)
                    {
                        sink.Add(RefSpace.EngineName, hashes[i], RefKind.FcbNameValue, site, i);
                    }
                }
                break;

            case FcbMemberType.Rml:
                // An embedded RML sub-document - only 6 members in the shipped config declare one,
                // but those are exactly the "a list of assets this thing pulls in" members, so the
                // paths inside are worth having.
                if (RmlDocument.TryDeserialize(value, out XElement? rml))
                {
                    RmlReferenceScan.Scan(rml, RefKind.FcbPathValue, site, sink);
                }
                break;
        }
    }
}
