using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using JackAll.Tools.World;

namespace JackAll.App.MapEditor;

/// <summary>The hierarchy's context menu: clipboard, delete and layer moves on entities, and new
/// entities and paste on layers.</summary>
public partial class MapTabView
{
    private IReadOnlyList<Control> HierarchyMenuFor(EntityTreeNode node)
    {
        if (_edits is not { } edits)
        {
            return [];
        }
        if (node is { IsLayer: true, LayerPathId: { } layer })
        {
            return LayerMenu(edits, layer);
        }
        return node.IsEntity && !node.IsDeleted ? EntityMenu(edits) : [];
    }

    private IReadOnlyList<Control> EntityMenu(WorldEditSession edits)
    {
        bool single = SelectionIsOneRoot;
        string here = _selection.Primary?.LayerPathId ?? "";
        return
        [
            MenuItems.Create("Copy", CopySelected, single),
            MenuItems.Create("Cut", CutSelected, single),
            MenuItems.Create("Delete", DeleteSelected),
            new Separator(),
            MenuItems.Submenu("Move to layer", edits.Layers
                .Where(l => !l.Equals(here, StringComparison.OrdinalIgnoreCase))
                .Select(l => MenuItems.Create(l, () => MoveSelectedToLayer(l)))),
        ];
    }

    private IReadOnlyList<Control> LayerMenu(WorldEditSession edits, string layer)
    {
        MenuItem create = MenuItems.Submenu("New",
        [
            MenuItems.Create("Prefab", () => NewAtViewCentre(at => edits.NewPrefab(at, layer))),
            MenuItems.Create("Archetype…", () => PickArchetype(layer)),
            MenuItems.Submenu("Standalone", edits.StandaloneClasses.Select(c =>
                MenuItems.Create(c, () => NewAtViewCentre(at => edits.NewStandalone(c, at, layer))))),
        ]);
        return [create, MenuItems.Create("Paste", () => PasteIntoLayer(layer), _clipboard is not null)];
    }

    /// <summary>Whether the selection is one entity, or one prefab with its members: all the clipboard holds.</summary>
    private bool SelectionIsOneRoot => _selection.Items.Except(MembersOf(_selection.Items).Keys).Count() == 1;

    private void CutSelected()
    {
        if (!SelectionIsOneRoot)
        {
            StatusText.Text = "Cut takes one entity or one prefab at a time";
            return;
        }
        CopySelected();
        DeleteSelected();
    }

    private void MoveSelectedToLayer(string layer)
    {
        if (_edits is not { } edits)
        {
            return;
        }

        LayerStep step;
        try
        {
            step = LayerStep.Move(edits, _selection.Items, layer);
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
            return;
        }
        _history.Push(step);
        EntitySetChanged();
        Reveal(_selection.Primary!);
        StatusText.Text = step.Label;
    }

    /// <summary>Pastes where the copy was taken from, or at the view's centre when it had no position.</summary>
    private void PasteIntoLayer(string layer)
    {
        if ((_clipboard?.Original.Position ?? GroundAtViewCentre()) is { } at)
        {
            PasteClipboard(at, layer);
        }
    }

    private void PickArchetype(string layer)
    {
        if (_library.Root is not { } archetypes)
        {
            return;
        }
        var picker = new ArchetypePickerWindow(archetypes) { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() == true && picker.Result is { } archetype)
        {
            Place(archetype, layer, ViewCentre);
        }
    }

    private void NewAtViewCentre(Func<Vector3, WorldEntity> create)
    {
        if (GroundAtViewCentre() is { } ground && Add(() => [create(ground)], []) is [var added])
        {
            StatusText.Text = $"Created {added.Name} in {added.LayerPathId}, sector {added.HomeSector.SectorId}";
        }
    }

    private Point ViewCentre => new(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2);

    private Vector3? GroundAtViewCentre() => GroundAt(ViewCentre, "There is no ground in the middle of the view to place on");
}
