using System.Windows;

namespace JackAll.App;

/// <summary>Attached to a tree row, grid row or document tab: true when what it holds differs from
/// the base game or the saved file. The styles paint it as black-not-grey text or a dirty marker.</summary>
public static class ItemState
{
    public static readonly DependencyProperty IsChangedProperty = DependencyProperty.RegisterAttached(
        "IsChanged", typeof(bool), typeof(ItemState), new PropertyMetadata(false));

    public static bool GetIsChanged(DependencyObject element) => (bool)element.GetValue(IsChangedProperty);

    public static void SetIsChanged(DependencyObject element, bool value) => element.SetValue(IsChangedProperty, value);
}
