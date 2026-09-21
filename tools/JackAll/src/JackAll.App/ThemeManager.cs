using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace JackAll.App;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Swaps the app's brush dictionary. Every view takes brushes with DynamicResource, so a
/// swap repaints what is already open; anything that paints from code listens to <see cref="Changed"/>.</summary>
public static class ThemeManager
{
    private const int DwmUseImmersiveDarkMode = 20;

    public static bool IsDark { get; private set; }

    public static event EventHandler? Changed;

    public static void Apply(AppTheme theme)
    {
        IsDark = theme == AppTheme.Dark || (theme == AppTheme.System && !WindowsAppsUseLightTheme());

        // App.xaml merges the brush file first, so that slot is the one to replace.
        var brushes = new ResourceDictionary
        {
            Source = new Uri($"/Themes/Brushes.{(IsDark ? "Dark" : "Light")}.xaml", UriKind.Relative),
        };
        Application.Current.Resources.MergedDictionaries[0] = brushes;

        foreach (Window window in Application.Current.Windows)
        {
            ApplyTitleBar(window);
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Asks DWM for a dark caption. A window calls this once its handle exists.</summary>
    public static void ApplyTitleBar(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        int dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
    }

    private static bool WindowsAppsUseLightTheme()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
