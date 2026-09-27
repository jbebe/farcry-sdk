using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace JackAll.App;

/// <summary>Attached to a TextBox whose binding writes on LostFocus: Enter writes it too.</summary>
public static class CommitOnEnter
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(CommitOnEnter), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBox box)
        {
            return;
        }
        box.KeyDown -= Commit;
        if ((bool)e.NewValue)
        {
            box.KeyDown += Commit;
        }
    }

    private static void Commit(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ((TextBox)sender).GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
    }
}
