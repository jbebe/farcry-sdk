using System.Windows;

namespace JackAll.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        CrashLog.Install(this);
        base.OnStartup(e);
    }
}

