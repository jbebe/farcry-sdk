using System.Collections.ObjectModel;
using JackAll.App;
using SoundBench.Vehicle;

namespace SoundBench.Views;

/// <summary>A top row of the voice tree: one event the car plays, listed whether or not it is playing, holding
/// its plays still sounding and the last one that ended.</summary>
public sealed class SlotView(SoundSlot slot) : Observable
{
    private int _plays;

    public SoundSlot Slot { get; } = slot;
    public string Title => $"{Slot.Field}: 0x{Slot.Id:x8}";
    public ObservableCollection<NodeView> Plays { get; } = [];
    public string State => _plays == 0 ? "not played yet" : _plays == 1 ? "played once" : $"played {_plays} times";

    public void Add(NodeView play)
    {
        Plays.Insert(0, play);
        _plays++;
        OnPropertyChanged(nameof(State));
        Prune();
    }

    /// <summary>Drops every ended play but the latest.</summary>
    public void Prune()
    {
        NodeView? latest = Plays.FirstOrDefault(p => p.Ended);
        foreach (NodeView old in Plays.Where(p => p.Ended && p != latest).ToList())
        {
            Plays.Remove(old);
        }
    }
}
