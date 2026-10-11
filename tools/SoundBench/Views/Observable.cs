using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SoundBench.Views;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Tells bindings every property may have changed.</summary>
    protected void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
