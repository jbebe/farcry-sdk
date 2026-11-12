using System.Windows;

namespace JackAll.App;

public partial class App : Application
{
    /// <summary>The crash log goes in first, before App.xaml loads.</summary>
    public App() => CrashLog.Install(this);
}
