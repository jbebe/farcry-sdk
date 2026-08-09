using System.Collections.ObjectModel;
using JackAll.Tools.Mgb;

namespace JackAll.App.FileHandlers.Mgb;

/// <summary>
/// One row of the <c>.mgb</c> tree. Wraps a live <see cref="MgbRecord"/> (or the package itself) so
/// edits made through the property grid land on the model the writer will serialise.
/// </summary>
/// <remarks>
/// The tree deliberately mirrors the format's own containment rather than inventing a friendlier
/// shape: what you can add, remove and reorder is exactly what the format allows in that position,
/// which is what lets the editor guarantee it never writes a package the game would reject.
/// </remarks>
public sealed class MgbTreeNode
{
    public MgbTreeNode(string label, object? target, MgbNodeKind kind = MgbNodeKind.Other)
    {
        Label = label;
        Target = target;
        Kind = kind;
    }

    public string Label { get; private set; }

    /// <summary>The model object this row edits, or null for a pure grouping row.</summary>
    public object? Target { get; }

    public MgbNodeKind Kind { get; }

    public ObservableCollection<MgbTreeNode> Children { get; } = [];

    /// <summary>The list this node lives in, when it is a removable/reorderable list member.
    /// Non-null only for areas and elements.</summary>
    public System.Collections.IList? OwnerList { get; init; }

    public bool IsExpanded { get; set; }

    public override string ToString() => Label;

    /// <summary>Builds the whole tree for a package.</summary>
    public static MgbTreeNode Build(MgbPackage package, MgbNameLookup names)
    {
        var root = new MgbTreeNode("Package", package, MgbNodeKind.Package) { IsExpanded = true };

        root.Children.Add(new MgbTreeNode($"UserData ({package.UserData.Properties.Count})", package.UserData));

        if (package.Materials.Count > 0)
        {
            var materials = new MgbTreeNode($"Materials ({package.Materials.Count})", null);
            foreach (MgbMaterial material in package.Materials)
            {
                materials.Children.Add(new MgbTreeNode(
                    $"Material  {names.Describe(material.NameId)}  {material.TexturePath}", material));
            }
            root.Children.Add(materials);
        }

        AddFontGroup(root, package, names);

        var areas = new MgbTreeNode($"Areas ({package.Areas.Count})", null) { IsExpanded = true };
        foreach (MgbArea area in package.Areas)
        {
            areas.Children.Add(BuildArea(area, package.Areas, names));
        }
        root.Children.Add(areas);

        if (package.StringTable is { } table)
        {
            var node = new MgbTreeNode($"StringTable ({table.Strings.Count})", table);
            foreach (MgbStringResource s in table.Strings)
            {
                node.Children.Add(new MgbTreeNode($"String  {names.Describe(s.NameId)}  \"{s.Text}\"", s));
            }
            root.Children.Add(node);
        }

        if (package.GenericObjectTable is { } generic)
        {
            var node = new MgbTreeNode($"GenericObjectTable ({generic.Objects.Count})", generic);
            foreach (MgbGenericObject o in generic.Objects)
            {
                node.Children.Add(new MgbTreeNode($"GenericObject  {names.Describe(o.NameId)}", o));
            }
            root.Children.Add(node);
        }

        return root;
    }

    private static void AddFontGroup(MgbTreeNode root, MgbPackage package, MgbNameLookup names)
    {
        int total = package.FontSubsts.Count + package.FontRefs.Count + package.FontFamilies.Count;
        if (total == 0)
        {
            return;
        }
        var fonts = new MgbTreeNode($"Fonts ({total})", null);
        foreach (MgbFontSubst subst in package.FontSubsts)
        {
            fonts.Children.Add(new MgbTreeNode(
                $"FontSubst  \"{MgbText.Ansi(subst.SubstName)}\"  ({subst.FontData.Length:N0} bytes)", subst));
        }
        foreach (MgbFontRef reference in package.FontRefs)
        {
            fonts.Children.Add(new MgbTreeNode(
                $"FontRef  \"{MgbText.Ansi(reference.Name)}\"", reference));
        }
        foreach (MgbFontFamily family in package.FontFamilies)
        {
            fonts.Children.Add(new MgbTreeNode(
                $"FontFamily  {names.Describe(family.NameId)}  \"{MgbText.Ansi(family.FontName)}\"", family));
        }
        root.Children.Add(fonts);
    }

    private static MgbTreeNode BuildArea(MgbArea area, IList<MgbArea> owner, MgbNameLookup names)
    {
        var node = new MgbTreeNode(
            $"{area.TypeName}  {names.Describe(area.UserData.NameId)}",
            area,
            MgbNodeKind.Area)
        {
            OwnerList = (System.Collections.IList)owner,
        };
        AddUserData(node, area.UserData, names);
        AddActionCaller(node, area.Action, names);
        foreach (MgbElement element in area.Elements)
        {
            node.Children.Add(BuildElement(element, area.Elements, names));
        }
        return node;
    }

    private static MgbTreeNode BuildElement(MgbElement element, IList<MgbElement> owner, MgbNameLookup names)
    {
        var node = new MgbTreeNode(
            $"{element.WidgetTypeName}{Summarise(element)}  {names.Describe(element.UserData.NameId)}",
            element,
            MgbNodeKind.Element)
        {
            OwnerList = (System.Collections.IList)owner,
        };

        AddUserData(node, element.UserData, names);
        AddActionCaller(node, element.Action, names);

        if (element.Widget is not MgbPlaceholder)
        {
            node.Children.Add(new MgbTreeNode($"{element.Widget.TypeName} (widget)", element.Widget));
        }
        if (element.Keyframes.Count > 0)
        {
            var frames = new MgbTreeNode($"Keyframes ({element.Keyframes.Count})", null);
            foreach (MgbKeyframe keyframe in element.Keyframes)
            {
                var kf = new MgbTreeNode($"Keyframe  idx {keyframe.Idx}", keyframe);
                kf.Children.Add(new MgbTreeNode(keyframe.State.TypeName, keyframe.State));
                AddActionCaller(kf, keyframe.Action, names);
                frames.Children.Add(kf);
            }
            node.Children.Add(frames);
        }
        if (element.Focusable is { } focusable)
        {
            node.Children.Add(new MgbTreeNode(
                $"{element.WrapperTypeName} ({focusable.Neighbors.Count} neighbour(s))", focusable));
        }
        return node;
    }

    /// <summary>A short, type-specific hint so the tree is scannable without expanding everything.</summary>
    private static string Summarise(MgbElement element) => element.Widget switch
    {
        MgbTextBase { UseStringTable: false } t when t.String.Length > 0 => $"  \"{Trim(t.Text)}\"",
        MgbTextBase { UseStringTable: true } t => $"  [string {t.TableId:X8}/{t.ResourceId:X8}]",
        MgbAreaInstance a when a.Label.Length > 0 => $"  → {Trim(a.LabelText)}",
        MgbImage { Material.Present: true } i => $"  [material {i.Material.Id:X8}]",
        _ => string.Empty,
    };

    private static string Trim(string text) =>
        text.Length <= 40 ? text : string.Concat(text.AsSpan(0, 39), "…");

    private static void AddUserData(MgbTreeNode parent, MgbUserData data, MgbNameLookup names)
    {
        if (data.Properties.Count == 0)
        {
            return;
        }
        var node = new MgbTreeNode($"UserData ({data.Properties.Count})", data);
        foreach (MgbProperty property in data.Properties)
        {
            node.Children.Add(new MgbTreeNode(
                $"{names.Describe(property.Key)}  tag {property.TypeTag:X2}", property));
        }
        parent.Children.Add(node);
    }

    private static void AddActionCaller(MgbTreeNode parent, MgbActionCaller caller, MgbNameLookup names)
    {
        if (caller.Executer is not { } executer)
        {
            return;
        }
        var node = new MgbTreeNode($"{executer.TypeName} ({executer.Actions.Count} action(s))", executer);
        foreach (MgbAction action in executer.Actions)
        {
            node.Children.Add(new MgbTreeNode(
                $"{action.OpcodeName ?? $"Action {action.ActionId:X8}"}", action));
        }
        parent.Children.Add(node);
    }
}

/// <summary>What a tree row is, for deciding which structural operations apply.</summary>
public enum MgbNodeKind
{
    Other,
    Package,
    Area,
    Element,
}
