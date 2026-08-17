using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JackAll.App;

/// <summary>
/// The bindable plumbing every tree in the app needs: the three row flags a <c>TreeViewItem</c> style
/// binds two-way, the parent/child links a filter or reveal walk needs, and change notification.
/// </summary>
/// <remarks>
/// The flags have to raise <see cref="PropertyChanged"/> even though the binding also pushes them up
/// from user interaction, because a filter and a reveal both set them from code.
/// </remarks>
public abstract class TreeNodeBase<T> : INotifyPropertyChanged
    where T : TreeNodeBase<T>
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isVisible = true;

    public ObservableCollection<T> Children { get; } = [];

    /// <summary>Null for a root; set by <see cref="AddChild"/>.</summary>
    public T? Parent { get; private set; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }

    /// <summary>Drives the row's <c>Visibility</c> - see <see cref="ApplyFilter"/>.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set { if (_isVisible == value) return; _isVisible = value; OnPropertyChanged(); }
    }

    protected void AddChild(T child)
    {
        child.Parent = (T)this;
        Children.Add(child);
    }

    /// <summary>Hides every node that neither matches nor holds a matching descendant.</summary>
    protected static bool ApplyFilter(T node, Func<T, bool> matches)
    {
        bool anyChildVisible = false;
        foreach (T child in node.Children)
        {
            anyChildVisible |= ApplyFilter(child, matches);
        }

        node.IsVisible = matches(node) || anyChildVisible;
        return node.IsVisible;
    }

    /// <summary>Selects the first matching node, expanding the path down to it.</summary>
    protected static T? Reveal(T node, Func<T, bool> matches)
    {
        if (matches(node))
        {
            node.IsSelected = true;
            return node;
        }

        foreach (T child in node.Children)
        {
            if (Reveal(child, matches) is { } found)
            {
                node.IsExpanded = true;
                return found;
            }
        }
        return null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
