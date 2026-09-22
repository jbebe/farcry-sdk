using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>One event link: when the owning entity raises <paramref name="Output"/>, the target
/// receives <paramref name="EventName"/> of class <paramref name="EventClass"/>.</summary>
public sealed record EntityLink(string Output, ulong TargetId, string EventClass, string EventName);

/// <summary>
/// Reads and writes the event links on an entity's <c>CEventComponent/hidLinks</c>. The layout is in
/// docs/docs/engine-internals/entity-instancing.md; the unnamed fields are kept as their hashes.
/// </summary>
public static class EntityLinks
{
    private static readonly uint Link = FcbClassDefinitions.Crc32Ascii("Link");
    private static readonly uint Event = FcbClassDefinitions.Crc32Ascii("Event");
    private static readonly uint EventName = FcbClassDefinitions.Crc32Ascii("hidEventName");
    private static readonly uint EventType = FcbClassDefinitions.Crc32Ascii("hidType");
    private const uint Output = 0xAB7AED5F;
    private const uint Target = 0x7D1A6B64;
    private const uint EventClass = 0xCF68E402;
    private const uint EventClassHash = 0x25368426;
    private const uint EventFlag = 0x83F9B027;
    private const uint EventTarget = 0xDCC35857;

    public static IReadOnlyList<EntityLink> Read(FcbObject entity)
        => LinksNode(entity) is { } links
            ? [.. links.Children.Where(l => l.TypeHash == Link).Select(Parse)]
            : [];

    /// <summary>Appends a link, creating the event component and its list when the entity has neither.</summary>
    public static void Add(FcbObject entity, EntityLink link)
    {
        FcbObject links = LinksNode(entity) ?? CreateLinksNode(entity);
        var ev = new FcbObject { TypeHash = Event };
        ev.Values[EventClass] = FcbEntityFields.StringBytes(link.EventClass);
        ev.Values[EventClassHash] = BitConverter.GetBytes(FcbClassDefinitions.Crc32Ascii(link.EventClass));
        ev.Values[EventName] = FcbEntityFields.StringBytes(link.EventName);
        ev.Values[EventFlag] = BitConverter.GetBytes(1u);
        ev.Values[EventTarget] = BitConverter.GetBytes(link.TargetId);
        ev.Values[EventType] = BitConverter.GetBytes(1u);

        var node = new FcbObject { TypeHash = Link };
        node.Values[Output] = FcbEntityFields.StringBytes(link.Output);
        node.Values[Target] = BitConverter.GetBytes(link.TargetId);
        node.Children.Add(ev);
        links.Children.Add(node);
    }

    /// <summary>Removes the <paramref name="index"/>th link, counted as <see cref="Read"/> lists them.</summary>
    public static void RemoveAt(FcbObject entity, int index)
    {
        FcbObject links = LinksNode(entity) ?? throw new ArgumentOutOfRangeException(nameof(index));
        links.Children.Remove(links.Children.Where(l => l.TypeHash == Link).ElementAt(index));
    }

    /// <summary>Points every link aimed at a key of <paramref name="ids"/> at its value instead.</summary>
    public static void Retarget(FcbObject entity, IReadOnlyDictionary<ulong, ulong> ids)
    {
        foreach (FcbObject link in LinksNode(entity)?.Children.Where(l => l.TypeHash == Link) ?? [])
        {
            if (ids.TryGetValue(FcbEntityFields.ReadU64(link, Target), out ulong id))
            {
                link.Values[Target] = BitConverter.GetBytes(id);
                if (link.Children.FirstOrDefault(c => c.TypeHash == Event) is { } ev && ev.Values.ContainsKey(EventTarget))
                {
                    ev.Values[EventTarget] = BitConverter.GetBytes(id);
                }
            }
        }
    }

    private static EntityLink Parse(FcbObject link)
    {
        FcbObject ev = link.Children.FirstOrDefault(c => c.TypeHash == Event) ?? new FcbObject();
        return new EntityLink(
            FcbEntityFields.ReadString(link, Output), FcbEntityFields.ReadU64(link, Target),
            FcbEntityFields.ReadString(ev, EventClass), FcbEntityFields.ReadString(ev, EventName));
    }

    private static FcbObject? LinksNode(FcbObject entity)
        => FcbEntityFields.FindComponent(entity, WorldHashes.CEventComponent)?.Children
            .FirstOrDefault(c => c.TypeHash == WorldHashes.HidLinks);

    private static FcbObject CreateLinksNode(FcbObject entity)
    {
        FcbObject events = FcbEntityFields.FindComponent(entity, WorldHashes.CEventComponent) ?? AddEventComponent(entity);
        var links = new FcbObject { TypeHash = WorldHashes.HidLinks };
        events.Children.Add(links);
        return links;
    }

    private static FcbObject AddEventComponent(FcbObject entity)
    {
        FcbObject? components = entity.Children.FirstOrDefault(c => c.TypeHash == WorldHashes.Components);
        if (components is null)
        {
            components = new FcbObject { TypeHash = WorldHashes.Components };
            entity.Children.Add(components);
        }
        var events = new FcbObject { TypeHash = WorldHashes.CEventComponent };
        events.Values[WorldHashes.HidHasAliasName] = [0];
        components.Children.Add(events);
        return events;
    }
}
