using System.ComponentModel;
using System.IO;
using JackAll.Tools.Ai;

namespace JackAll.App.Ai;

public enum AiSection
{
    Soldiers,
    Weapons,
    Behaviors,
    Brains,
}

/// <summary>The AI tab: soldier and weapon archetypes, behaviour odds and brain workspaces, saved together.</summary>
public sealed class AiTabViewModel : Observable
{
    private string _status = "";
    private bool _isBusy;

    public AiTabViewModel(MainViewModel vm)
    {
        Soldiers = new AiArchetypesModel(vm, SoldierFields.Catalog,
            "Every soldier type the single-player worlds declare. Tick several to tune them together; each edit goes to both worlds' copies.",
            "Hover a setting to see what it does. Bold rows differ from the base game. Marksmanship is the aim model: every shot's hit chance is the product of the own and player's factors, gated by the reaction time.");
        Weapons = new AiArchetypesModel(vm, WeaponFields.Catalog,
            "How soldiers fire each weapon. The player's own weapon behaviour is not affected. Tick several to tune them together.",
            "After a hit on the player the shooter is forced to miss a number of shots drawn between the two values for the game's difficulty - on Infamous the shipped weapons use 0, so hits can chain. Bold rows differ from the base game.");
        Behaviors = new AiBehaviorsModel(vm);
        Brains = new AiBrainsModel(vm);
        foreach (INotifyPropertyChanged section in (INotifyPropertyChanged[])[Soldiers, Weapons, Behaviors, Brains])
        {
            section.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IsDirty))
                {
                    RaiseDirty();
                }
            };
        }
    }

    public AiArchetypesModel Soldiers { get; }

    public AiArchetypesModel Weapons { get; }

    public AiBehaviorsModel Behaviors { get; }

    public AiBrainsModel Brains { get; }

    public string Status { get => _status; set => Set(ref _status, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool IsDirty => Soldiers.IsDirty || Weapons.IsDirty || Behaviors.IsDirty || Brains.IsDirty;

    public bool CanSave => IsDirty && !IsBusy;

    /// <summary>Loads a section the first time it is shown.</summary>
    public Task ShowAsync(AiSection section) => section switch
    {
        AiSection.Soldiers when !Soldiers.IsLoaded => Busy(() => Soldiers.LoadAsync(new Progress<string>(s => Status = s))),
        AiSection.Weapons when !Weapons.IsLoaded => Busy(() => Weapons.LoadAsync(new Progress<string>(s => Status = s))),
        AiSection.Behaviors when !Behaviors.IsLoaded => Busy(() =>
        {
            Behaviors.Load();
            Status = $"{Behaviors.Rows.Count} adaptive behaviours";
            return Task.CompletedTask;
        }),
        AiSection.Brains when Brains.Brains.Count == 0 => Busy(() =>
        {
            Brains.Initialize();
            return Task.CompletedTask;
        }),
        _ => Task.CompletedTask,
    };

    public Task LoadBrainAsync(string path) => Busy(async () =>
    {
        Status = $"Reading {path}…";
        await Brains.LoadAsync(path);
        Status = $"{Brains.NodeCount:N0} nodes in {Path.GetFileName(path)}";
    });

    /// <summary>Stages every section's edits; the text is why they could not be, or null.</summary>
    public async Task<string?> SaveAsync()
    {
        List<string> saved = [];
        string? error = null;
        await Busy(async () =>
        {
            Status = "Saving…";
            foreach (AiArchetypesModel archetypes in (AiArchetypesModel[])[Soldiers, Weapons])
            {
                if (archetypes.IsDirty)
                {
                    saved.Add($"{await archetypes.SaveAsync()} archetype(s)");
                }
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
        }, ex => error = ex.Message);
        return error;
    }

    /// <summary>Throws away the edits made since the last save.</summary>
    public Task RevertAsync() => Busy(async () =>
    {
        foreach (AiArchetypesModel archetypes in (AiArchetypesModel[])[Soldiers, Weapons])
        {
            if (archetypes.IsDirty)
            {
                await archetypes.LoadAsync(new Progress<string>(s => Status = s));
            }
        }
        if (Behaviors.IsDirty)
        {
            Behaviors.Load();
        }
        if (Brains is { IsDirty: true, LoadedPath: { } path })
        {
            await Brains.LoadAsync(path);
        }
        Status = "Reverted to the last save";
    });

    /// <summary>Runs one action at a time, reporting a failure in the status line.</summary>
    private async Task Busy(Func<Task> action, Action<Exception>? failed = null)
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Status = $"Failed: {ex.Message}";
            failed?.Invoke(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RaiseDirty()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanSave));
    }
}
