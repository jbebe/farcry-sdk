using System.Windows;
using System.Windows.Controls;

namespace JackAll.App.Mgb;

/// <summary>
/// Picks the editor for a property row from its <see cref="MgbPropertyRow.Kind"/>.
/// </summary>
/// <remarks>
/// A selector rather than one template with everything in it under collapsed visibility, which is
/// how this grid used to work: with a colour picker (a swatch plus a four-slider popup) and a
/// dropdown in the set, the all-in-one template would build every one of those for every row and
/// then hide most of them - hundreds of sliders for a record that has one colour. The selector
/// builds only what a row actually is.
/// </remarks>
public sealed class MgbRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Text { get; set; }
    public DataTemplate? Bool { get; set; }
    public DataTemplate? ReadOnly { get; set; }
    public DataTemplate? Color { get; set; }
    public DataTemplate? Enum { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
        => item is not MgbPropertyRow row
            ? base.SelectTemplate(item, container)
            : row.Kind switch
            {
                MgbRowKind.Bool => Bool,
                MgbRowKind.ReadOnly => ReadOnly,
                MgbRowKind.Color => Color,
                MgbRowKind.Enum => Enum,
                _ => Text,
            };
}
