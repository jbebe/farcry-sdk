using System.IO;
using System.Reflection;
using System.Windows;

namespace JackAll.App;

/// <summary>When an exception escapes, writes crash.log beside config.ini with the machine and GPU
/// details, and says where it is.</summary>
public static class CrashLog
{
    private const int KeptGpuErrors = 20;

    private static readonly Queue<string> GpuErrors = new();
    private static Exception? _written;

    /// <summary>Adapter, feature level, driver and video memory, once the Map tab has a device.</summary>
    public static string Gpu { get; set; } = "no Direct3D device created";

    public static void Install(Application app)
    {
        app.DispatcherUnhandledException += (_, e) => Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                Report(exception);
            }
        };
    }

    /// <summary>Keeps a Direct3D error for the next crash report.</summary>
    public static void NoteGpuError(string message)
    {
        lock (GpuErrors)
        {
            GpuErrors.Enqueue($"{DateTime.Now:HH:mm:ss} {message}");
            if (GpuErrors.Count > KeptGpuErrors)
            {
                GpuErrors.Dequeue();
            }
        }
    }

    private static void Report(Exception exception)
    {
        // An unhandled UI exception reaches both handlers.
        if (ReferenceEquals(Interlocked.Exchange(ref _written, exception), exception))
        {
            return;
        }

        Write(exception);
        MessageBox.Show($"JackAll crashed. The details are in\n{AppConfig.CrashLogPath}\n\n{exception.Message}",
            "JackAll", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void Write(Exception exception)
    {
        string[] errors;
        lock (GpuErrors)
        {
            errors = [.. GpuErrors];
        }

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        string report =
            $"""
            JackAll {Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}
            {DateTime.Now:yyyy-MM-dd HH:mm:ss}
            {Environment.OSVersion}, .NET {Environment.Version}
            Memory: {process.PrivateMemorySize64 / 1048576} MB private, {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1048576} MB RAM
            GPU: {Gpu}
            Recent Direct3D errors:
            {(errors.Length > 0 ? string.Join(Environment.NewLine, errors) : "none")}

            {exception}
            """;
        try
        {
            File.WriteAllText(AppConfig.CrashLogPath, report);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
