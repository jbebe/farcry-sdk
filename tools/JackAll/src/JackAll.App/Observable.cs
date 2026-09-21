using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JackAll.App;

/// <summary>The hand-rolled <see cref="INotifyPropertyChanged"/> the app's view models share; it has
/// no MVVM toolkit.</summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Assigns and raises only on a real change.</summary>
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
