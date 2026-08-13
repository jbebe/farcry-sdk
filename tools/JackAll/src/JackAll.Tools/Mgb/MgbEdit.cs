namespace JackAll.Tools.Mgb;

/// <summary>
/// Structural edits to a package: create, duplicate, remove and reorder areas and elements.
/// </summary>
/// <remarks>
/// All of these are ordinary list mutations followed by a reserialise, which is only possible
/// because the whole format decodes - the previous editor spliced bytes into a file it could not
/// walk, and so could neither grow the type table nor touch anything it had not already located.
///
/// The legal-type helpers are the point: they come straight from the engine's own <c>Factory</c>
/// dispatchers, so an editor that only ever offers what they return cannot build a package the game
/// would reject.
/// </remarks>
public static class MgbEdit
{
    /// <summary>The area classes that may appear in a package's top-level list.</summary>
    public static IReadOnlyList<string> LegalAreaTypes => MgbSchema.AreaTypes;

    /// <summary>The widget classes that may appear in any area's element list.</summary>
    public static IReadOnlyList<string> LegalElementTypes { get; } =
        [.. MgbSchema.WidgetWrapper.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Creates an area of <paramref name="typeName"/>, declaring the class in the
    /// package's type table if it isn't already there.</summary>
    public static MgbArea CreateArea(MgbPackage package, string typeName, string name)
    {
        if (!MgbSchema.IsAreaType(typeName))
        {
            throw new MgbFormatException(
                $"'{typeName}' is not one of the {MgbSchema.AreaTypes.Length} area classes " +
                $"Factory::MakeArea can construct ({string.Join(", ", MgbSchema.AreaTypes)})");
        }

        var area = new MgbArea
        {
            TypeSlot = package.Types.SlotForName(typeName),
            TypeName = typeName,
            // 30 fps, matching what every corpus file uses; the engine stores 1000/this and clamps a
            // zero to 1, so a default of 0 would silently become a 1 ms frame.
            FrameRate = 30,
        };
        area.UserData.NameId = MgbTypeTable.Hash(name);
        if (typeName is "Button" or "CheckBox")
        {
            area.Timings = new uint[typeName == "Button" ? 6 : 12];
        }
        return area;
    }

    /// <summary>Creates an element wrapping a widget of <paramref name="widgetTypeName"/>,
    /// declaring the class in the package's type table if needed.</summary>
    public static MgbElement CreateElement(MgbPackage package, string widgetTypeName, string name)
    {
        if (!MgbSchema.IsWidgetType(widgetTypeName))
        {
            throw new MgbFormatException(
                $"'{widgetTypeName}' is not one of the 14 widget classes Factory::MakeElement can " +
                $"construct ({string.Join(", ", LegalElementTypes)})");
        }

        var element = new MgbElement
        {
            TypeSlot = package.Types.SlotForName(widgetTypeName),
            WidgetTypeName = widgetTypeName,
            Widget = MgbWidget.Create(widgetTypeName),
        };
        element.UserData.NameId = MgbTypeTable.Hash(name);
        if (MgbSchema.WrapperHasFocusableTail(widgetTypeName))
        {
            element.Focusable = new MgbFocusableTail();
        }
        return element;
    }

    /// <summary>A deep copy, made by round-tripping the record through the codec.</summary>
    /// <remarks>
    /// Cloning this way rather than by hand means a new field can never be missed by the copy - the
    /// same reason the reader and writer share one description. It also proves the clone is
    /// writable: anything that fails to serialise fails here rather than at save time.
    /// </remarks>
    public static MgbArea DuplicateArea(MgbPackage package, MgbArea area)
    {
        var ctx = new MgbContext(package.Types);
        var writer = new MgbWriteCodec(package.Invert);
        area.Serialize(writer, ctx);

        var copy = new MgbArea { TypeSlot = area.TypeSlot, TypeName = area.TypeName };
        copy.Serialize(new MgbReadCodec(writer.ToArray(), package.Invert), ctx);
        return copy;
    }

    /// <inheritdoc cref="DuplicateArea"/>
    public static MgbElement DuplicateElement(MgbPackage package, MgbElement element)
    {
        var ctx = new MgbContext(package.Types);
        var writer = new MgbWriteCodec(package.Invert);
        element.Serialize(writer, ctx);

        var copy = new MgbElement { TypeSlot = element.TypeSlot, WidgetTypeName = element.WidgetTypeName };
        copy.Serialize(new MgbReadCodec(writer.ToArray(), package.Invert), ctx);
        return copy;
    }
}
