using JackAll.App.FileHandlers.Sav;
using JackAll.Tools.Sav;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace JackAll.App;

/// <summary>The Saves-tab half of <see cref="MainViewModel"/>: discovering the player's .sav files
/// and keeping the list and its selected row's details fresh.</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<SaveRow> Saves { get; } = [];

    private string _savesStatus = "Looking for saves…";
    public string SavesStatus
    {
        get => _savesStatus;
        private set { _savesStatus = value; OnPropertyChanged(); }
    }

    private SaveRow? _selectedSave;
    public SaveRow? SelectedSave
    {
        get => _selectedSave;
        set
        {
            if (_selectedSave == value) return;
            _selectedSave = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedSave));
            OnPropertyChanged(nameof(NoSelectedSave));
            SelectedSaveDetails = value is null ? null : new SaveDetailsViewModel(value);
        }
    }

    public bool HasSelectedSave => SelectedSave is not null;
    public bool NoSelectedSave => SelectedSave is null;

    private SaveDetailsViewModel? _selectedSaveDetails;
    public SaveDetailsViewModel? SelectedSaveDetails
    {
        get => _selectedSaveDetails;
        private set
        {
            if (_selectedSaveDetails is not null)
            {
                _selectedSaveDetails.PropertyChanged -= SelectedSaveDetails_PropertyChanged;
            }
            _selectedSaveDetails = value;
            if (_selectedSaveDetails is not null)
            {
                _selectedSaveDetails.PropertyChanged += SelectedSaveDetails_PropertyChanged;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(SavesGridEnabled));
        }
    }

    // While the newly-selected save's PersistenceDB tree is still decoding (SaveDetailsViewModel's
    // own background load - can be genuinely slow, see its class remarks), the grid and Delete button
    // stay disabled (see SavesGridEnabled) so that's visibly true, not just a status line easy to miss.
    private void SelectedSaveDetails_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SaveDetailsViewModel.IsLoading))
        {
            OnPropertyChanged(nameof(SavesGridEnabled));
        }
    }

    /// <summary>False while the selected save's details are still decoding - disables the saves grid
    /// and its Delete button for that stretch (see MainWindow.xaml) so a slow decode reads as "loading",
    /// not as an unresponsive UI the user might click through (switching selection or deleting the file
    /// mid-decode).</summary>
    public bool SavesGridEnabled => SelectedSaveDetails is not { IsLoading: true };

    /// <summary>
    /// Populates the Saves tab from <see cref="SaveGameLocator.SavedGamesFolder"/>. Independent of
    /// <see cref="Install"/>/<see cref="_vfs"/> — a save is read from the user's Documents folder, not
    /// the game install — so this can run in parallel with <see cref="InitializeAsync"/> rather than
    /// waiting on it. A .sav that fails to parse is skipped rather than failing the whole tab: the
    /// format (reverse/dunia/savegame_format.md) was derived from one real save, and this is the
    /// bulk-exposure test of whether it generalizes across a player's other saves too.
    /// </summary>
    public async Task LoadSavesAsync()
    {
        List<SaveRow> rows;
        int failed = 0;
        try
        {
            (rows, failed) = await Task.Run(() =>
            {
                var loaded = new List<SaveRow>();
                int failedCount = 0;
                foreach (string path in SaveGameLocator.EnumerateSaveFiles())
                {
                    try
                    {
                        loaded.Add(new SaveRow(SaveGameDocument.Read(path)));
                    }
                    catch
                    {
                        failedCount++; // corrupt file, or a save shaped differently than the one this format was derived from
                    }
                }
                loaded.Sort((a, b) => b.LastWriteTimeLocal.CompareTo(a.LastWriteTimeLocal)); // newest first
                return (loaded, failedCount);
            });
        }
        catch (Exception ex)
        {
            SavesStatus = $"Couldn't read the saves folder: {ex.Message}";
            return;
        }

        Saves.Clear();
        foreach (SaveRow row in rows)
        {
            Saves.Add(row);
        }

        SavesStatus = Saves.Count == 0
            ? $"No saves found in {SaveGameLocator.SavedGamesFolder}"
            : $"{Saves.Count:N0} save(s) found"
              + (failed > 0 ? $"  •  {failed} couldn't be read" : string.Empty);
    }

    /// <summary>Adds a newly written save to the list, newest-first like <see cref="LoadSavesAsync"/>
    /// leaves it - lighter than a full reload, and keeps the current selection and its decoded details
    /// alive.</summary>
    public void AddSaveRow(string filePath)
    {
        var row = new SaveRow(SaveGameDocument.Read(filePath));
        int index = 0;
        while (index < Saves.Count && Saves[index].LastWriteTimeLocal > row.LastWriteTimeLocal)
        {
            index++;
        }
        Saves.Insert(index, row);

        SavesStatus = $"{Saves.Count:N0} save(s) found";
    }

    /// <summary>Drops one save from the list after its file has been deleted from disk (see
    /// MainWindow.xaml.cs's DeleteSave_Click) - lighter than a full <see cref="LoadSavesAsync"/>
    /// reload, and keeps the rest of the list/selection undisturbed.</summary>
    public void RemoveSaveRow(SaveRow row)
    {
        Saves.Remove(row);
        if (ReferenceEquals(SelectedSave, row))
        {
            SelectedSave = null;
        }

        SavesStatus = Saves.Count == 0
            ? $"No saves found in {SaveGameLocator.SavedGamesFolder}"
            : $"{Saves.Count:N0} save(s) found";
    }

    /// <summary>Re-reads one save's own metadata off disk and swaps its <see cref="SaveRow"/> in
    /// <see cref="Saves"/> for a fresh one - called after writing edits back into that save's file (see
    /// <c>MainWindow.xaml.cs</c>'s <c>OpenSaveFcbEditorTab</c>), so the sidebar's persisted-object-count/
    /// thumbnail/etc. don't keep showing stale pre-edit values. Narrower than re-running
    /// <see cref="LoadSavesAsync"/> (which would reset the whole list and selection for an unrelated
    /// reason) - re-selects the refreshed row only if it was already selected, which also re-triggers
    /// <see cref="SelectedSaveDetails"/>'s own reload.</summary>
    public void RefreshSaveRow(string filePath)
    {
        int index = -1;
        for (int i = 0; i < Saves.Count; i++)
        {
            if (string.Equals(Saves[i].Info.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }
        if (index < 0) return;

        var refreshed = new SaveRow(SaveGameDocument.Read(filePath));
        bool wasSelected = ReferenceEquals(SelectedSave, Saves[index]);
        Saves[index] = refreshed;
        if (wasSelected)
        {
            SelectedSave = refreshed;
        }
    }
}
