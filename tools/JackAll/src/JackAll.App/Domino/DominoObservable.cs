using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JackAll.App.Domino;

/// <summary>
/// The usual hand-rolled <see cref="INotifyPropertyChanged"/>, shared by the Domino graph view models.
/// The app has no MVVM toolkit and repeats this boilerplate per class elsewhere; these four view models
/// sit together and are numerous enough (one per node and one per port, so thousands on a big graph)
/// that a common base is worth it here.
/// </summary>
public abstract class DominoObservable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Assigns and raises only on a real change - nodify writes <c>Anchor</c> back on every
    /// layout pass, so a same-value write is the common case, not the exception.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
