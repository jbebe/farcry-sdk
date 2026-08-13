namespace JackAll.Core;

/// <summary>
/// An <see cref="IProgress{T}"/> that reports on the calling thread.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="Progress{T}"/>: with no synchronization context - which is exactly the
/// situation in a console app - that one marshals every report onto the thread pool, so lines arrive
/// out of order and often after the operation they described has finished. Here the reports are the
/// only sign of life during a multi-second archive mount, so they have to print as they happen.
/// </remarks>
public sealed class SyncProgress(Action<string> report) : IProgress<string>
{
    public void Report(string value) => report(value);
}
