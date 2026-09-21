using JackAll.App.FileHandlers.Fcb;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.Sav;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JackAll.App.FileHandlers.Sav;

/// <summary>The selected save's whole <c>PersistenceDB</c> rendered as XML for the Saves tab's sidebar. Read-only.</summary>
public sealed class SaveDetailsViewModel : INotifyPropertyChanged
{
    private static Lazy<FcbClassDefinitions> Definitions => FcbDefinitionsProvider.Value;

    public SaveRow Save { get; }

    private bool _isLoading = true;
    private string _statusText = "Decoding this save's data…";
    private string? _documentXml;

    public SaveDetailsViewModel(SaveRow save)
    {
        Save = save;
        _ = LoadAsync();
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowDocument)); }
    }

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    public string? DocumentXml
    {
        get => _documentXml;
        private set { _documentXml = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowDocument)); }
    }

    public bool ShowDocument => !IsLoading && DocumentXml is not null;

    private async Task LoadAsync()
    {
        try
        {
            FcbObject root = await Task.Run(() => SaveGameDocument.ReadFcbRoot(Save.Info));
            string xml = await Task.Run(() => FcbXml.ToXml(root, Definitions.Value, SaveGameNames.Shared.Value));
            DocumentXml = xml;
            StatusText = "Ready.";
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't decode this save's data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
