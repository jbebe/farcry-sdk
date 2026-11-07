using System.IO;
using System.Reflection;
using System.Windows;

namespace JackAll.App;

/// <summary>Writes crash.log beside config.ini when an exception escapes, with the machine and GPU details.</summary>
public static class CrashLog
{
    private const int KeptGlErrors = 20;

    private static readonly Queue<string> GlErrors = new();
    private static Exception? _written;

    /// <summary>Renderer, GL version, vendor and video memory, once a viewport has a context.</summary>
    public static string Gpu { get; set; } = "no GL context created";

    public static void Install(Application app)
    {
        app.DispatcherUnhandledException += (_, e) =>
        {
            Write(e.Exception);
            MessageBox.Show($"JackAll crashed. The details are in\n{AppConfig.CrashLogPath}\n\n{e.Exception.Message}",
                "JackAll", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                Write(exception);
            }
        };
    }

    /// <summary>Keeps a driver error for the next crash report; called from the driver's thread.</summary>
    public static void NoteGlError(string message)
    {
        lock (GlErrors)
        {
            GlErrors.Enqueue($"{DateTime.Now:HH:mm:ss} {message}");
            if (GlErrors.Count > KeptGlErrors)
            {
                GlErrors.Dequeue();
            }
        }
    }

    private static void Write(Exception exception)
    {
        // An unhandled UI exception reaches both handlers.
        if (ReferenceEquals(Interlocked.Exchange(ref _written, exception), exception))
        {
            return;
        }

        string[] errors;
        lock (GlErrors)
        {
            errors = [.. GlErrors];
        }

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        string report =
            $"""
            JackAll {Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}
            {DateTime.Now:yyyy-MM-dd HH:mm:ss}
            {Environment.OSVersion}, .NET {Environment.Version}
            Memory: {process.PrivateMemorySize64 / 1048576} MB private, {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1048576} MB RAM
            GPU: {Gpu}
            Recent GL errors:
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
