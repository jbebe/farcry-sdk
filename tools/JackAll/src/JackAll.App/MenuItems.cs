using System.Windows.Controls;

namespace JackAll.App;

/// <summary>Context-menu items built in code, with a TextBlock header so an underscore is not read as an access key.</summary>
internal static class MenuItems
{
    public static MenuItem Create(string label, Action? run = null, bool enabled = true)
    {
        var item = new MenuItem { Header = new TextBlock { Text = label }, IsEnabled = enabled };
        if (run is not null)
        {
            item.Click += (_, _) => run();
        }
        return item;
    }

    /// <summary>A submenu over <paramref name="children"/>, disabled when there are none.</summary>
    public static MenuItem Submenu(string label, IEnumerable<MenuItem> children)
    {
        MenuItem item = Create(label);
        foreach (MenuItem child in children)
        {
            item.Items.Add(child);
        }
        item.IsEnabled = item.Items.Count > 0;
        return item;
    }
}
