using System.Windows;

namespace JackAll.App;

/// <summary>Attached to a tree or grid row: true when something under it differs from the base game.</summary>
public static class RowState
{
    public static readonly DependencyProperty IsChangedProperty = DependencyProperty.RegisterAttached(
        "IsChanged", typeof(bool), typeof(RowState), new PropertyMetadata(false));

    public static bool GetIsChanged(DependencyObject element) => (bool)element.GetValue(IsChangedProperty);

    public static void SetIsChanged(DependencyObject element, bool value) => element.SetValue(IsChangedProperty, value);
}
