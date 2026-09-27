using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using JackAll.Core.Format;
using JackAll.Core.Vfs;

namespace JackAll.App.Picker;

/// <summary>The picker's folder tree, file list and typed file name, over a snapshot of the merged filesystem.</summary>
public sealed class FilePickerViewModel : Observable
{
    private readonly MainViewModel _vm;
    private readonly FilePickerRequest _request;
    private readonly string? _folderPrefix;

    /// <summary>Every file the request allows, sorted by name so a filtered list needs no sort of its own.</summary>
    private readonly List<VfsFile> _universe;

    private readonly Dictionary<string, FolderNode> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<VfsFile>> _byFolder = new(StringComparer.OrdinalIgnoreCase);
    private bool _onlyExtension;
    private FolderNode? _selectedFolder;
    private string _filterText = string.Empty;
    private IReadOnlyList<VfsFile> _files = [];
    private VfsFile? _selectedFile;
    private string _fileName = string.Empty;
    private VfsFile? _resolved;

    public FilePickerViewModel(MainViewModel vm, FilePickerRequest request)
    {
        _vm = vm;
        _request = request;
        _folderPrefix = request.Folder is { } folder ? folder.TrimEnd('\\') + "\\" : null;
        _onlyExtension = request.Extension is not null;
        _universe = [.. vm.AllFiles.Where(IsAllowed).OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)];

        VfsFile? initial = request.InitialHash is { } hash
            ? Allowed(vm.FindByHash(hash))
            : request.InitialPath is { Length: > 0 } path ? Resolve(path) : null;
        BuildTree(initial?.Directory);
        _selectedFolder = initial is null ? Roots.FirstOrDefault() : _index.GetValueOrDefault(initial.Directory);
        RefreshFiles();
        SelectedFile = initial;
        FileName = initial?.Path ?? request.InitialPath ?? string.Empty;
    }

    public string Title => _request.Title;

    public bool HasExtension => _request.Extension is not null;

    public string OnlyExtensionLabel => $"Only *.{_request.Extension}";

    /// <summary>Limits the tree and the list to the requested extension.</summary>
    public bool OnlyExtension
    {
        get => _onlyExtension;
        set
        {
            if (Set(ref _onlyExtension, value))
            {
                // Clearing the tree pushes a null selection back in, so the folder is kept by path.
                string? folder = _selectedFolder?.FullPath;
                BuildTree(folder);
                SelectedFolder = folder is null ? null : _index.GetValueOrDefault(folder);
            }
        }
    }

    public ObservableCollection<FolderNode> Roots { get; } = [];

    public FolderNode? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (Set(ref _selectedFolder, value))
            {
                RefreshFiles();
            }
        }
    }

    public IReadOnlyList<FolderNode> AncestorChain(FolderNode node) => FolderTree.AncestorChain(_index, node);

    /// <summary>The Files tab's filter syntax; while it holds anything the list spans every folder.</summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (Set(ref _filterText, value))
            {
                RefreshFiles();
            }
        }
    }

    public IReadOnlyList<VfsFile> Files
    {
        get => _files;
        private set => Set(ref _files, value);
    }

    public VfsFile? SelectedFile
    {
        get => _selectedFile;
        set
        {
            // A list swap pushes null here; the typed name has to survive it.
            if (Set(ref _selectedFile, value) && value is not null)
            {
                FileName = value.Path;
            }
        }
    }

    /// <summary>A typed path, or a file's 8-digit hash; <see cref="Resolved"/> follows it.</summary>
    public string FileName
    {
        get => _fileName;
        set
        {
            if (Set(ref _fileName, value))
            {
                Resolved = Resolve(value);
            }
        }
    }

    public VfsFile? Resolved
    {
        get => _resolved;
        private set
        {
            if (Set(ref _resolved, value))
            {
                OnPropertyChanged(nameof(CanAccept));
                OnPropertyChanged(nameof(Status));
            }
        }
    }

    public bool CanAccept => Resolved is not null;

    /// <summary>The picked file's hash, or why nothing is picked.</summary>
    public string Status => Resolved is { } file ? $"CRC32 {file.EngineHash:X8}"
        : FileName.Trim().Length > 0 ? "No file here has this path."
        : string.Empty;

    private bool IsAllowed(VfsFile file)
        => !file.IsSynthetic
           && (!_request.NeedsRealPath || file.NameIsKnown)
           && (_folderPrefix is null || file.Path.StartsWith(_folderPrefix, StringComparison.OrdinalIgnoreCase));

    private VfsFile? Allowed(VfsFile? file) => file is not null && IsAllowed(file) ? file : null;

    private VfsFile? Resolve(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return null;
        }
        if (Allowed(_vm.FindByPath(text)) is { } named)
        {
            return named;
        }

        // An unnamed file's own _unknown\...\<hash>.<ext> path, or a bare hash.
        string stem = Path.GetFileNameWithoutExtension(text);
        return stem.Length == 8 && uint.TryParse(stem, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hash)
            ? Allowed(_vm.FindByHash(hash))
            : null;
    }

    private IEnumerable<VfsFile> Pickable()
        => _onlyExtension
            ? _universe.Where(f => string.Equals(f.Type.Extension, _request.Extension, StringComparison.OrdinalIgnoreCase))
            : _universe;

    /// <summary>Rebuilds the tree and the per-folder lists over what is pickable, opening the way to <paramref name="reveal"/>.</summary>
    private void BuildTree(string? reveal)
    {
        _byFolder.Clear();
        FolderNode root = FolderTree.Build(Pickable(), _index, reveal, (node, file) =>
        {
            if (!_byFolder.TryGetValue(node.FullPath, out List<VfsFile>? files))
            {
                _byFolder[node.FullPath] = files = [];
            }
            files.Add(file);
        });

        Roots.Clear();
        foreach (FolderNode child in root.Children)
        {
            Roots.Add(child);
        }
    }

    private void RefreshFiles()
    {
        FileFilter filter = FileFilter.Parse(_filterText);
        Files = filter.IsEmpty
            ? _selectedFolder is { } folder ? _byFolder.GetValueOrDefault(folder.FullPath) ?? [] : []
            : [.. Pickable().Where(f => filter.Matches(f, _vm.ModuleNameFor))];
    }
}
