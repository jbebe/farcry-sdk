using System.IO;

namespace JackAll.App.Ai;

/// <summary>The AI tab: soldier archetypes, behaviour odds and brain workspaces, saved together.</summary>
public sealed class AiTabViewModel : Observable
{
    private string _status = "";
    private bool _isBusy;

    public AiTabViewModel(MainViewModel vm)
    {
        Soldiers = new AiSoldiersModel(vm);
        Behaviors = new AiBehaviorsModel(vm);
        Brains = new AiBrainsModel(vm);
        Soldiers.DirtyChanged += RaiseDirty;
        Behaviors.DirtyChanged += RaiseDirty;
        Brains.DirtyChanged += RaiseDirty;
    }

    public AiSoldiersModel Soldiers { get; }

    public AiBehaviorsModel Behaviors { get; }

    public AiBrainsModel Brains { get; }

    public string Status { get => _status; set => Set(ref _status, value); }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (Set(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool IsDirty => Soldiers.IsDirty || Behaviors.IsDirty || Brains.IsDirty;

    public bool CanSave => IsDirty && !IsBusy;

    /// <summary>Stages every section's edits; the text is why they could not be, or null.</summary>
    public async Task<string?> SaveAsync()
    {
        IsBusy = true;
        Status = "Saving…";
        try
        {
            List<string> saved = [];
            if (Soldiers.IsDirty)
            {
                saved.Add($"{await Soldiers.SaveAsync()} archetype(s)");
            }
            if (Behaviors.IsDirty)
            {
                Behaviors.Save();
                saved.Add("behaviour odds");
            }
            if (Brains.IsDirty)
            {
                await Brains.SaveAsync();
                saved.Add(Path.GetFileName(Brains.LoadedPath ?? "brain"));
            }
            Status = saved.Count == 0 ? "Nothing to save" : $"Staged {string.Join(", ", saved)} into the workspace";
            return null;
        }
        catch (Exception ex)
        {
            Status = "Not saved";
            return ex.Message;
        }
        finally
        {
            IsBusy = false;
            RaiseDirty();
        }
    }

    private void RaiseDirty()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanSave));
    }
}
