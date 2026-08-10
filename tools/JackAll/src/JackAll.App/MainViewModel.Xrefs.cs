using JackAll.Core;
using JackAll.Core.Vfs;
using JackAll.Core.Xrefs;
using JackAll.Tools.Xrefs;

namespace JackAll.App;

/// <summary>One row of the Xrefs panel: the other end of a reference, ready to display and follow.</summary>
/// <param name="Display">The other end's name - a recovered path, or <c>#XXXXXXXX</c> when the
/// filelist never named it (or the hash isn't a file at all).</param>
/// <param name="Space">Which hash space <paramref name="Target"/> lives in.</param>
/// <param name="Target">The hash to navigate to when this row is activated.</param>
/// <param name="CanNavigate">False when nothing in the merged filesystem corresponds to
/// <paramref name="Target"/> - a dangling reference, shown greyed rather than hidden, because "this
/// points at something the game doesn't ship" is itself worth seeing.</param>
public sealed record XrefRow(
    string Display,
    string Site,
    string Kind,
    RefSpace Space,
    uint Target,
    bool CanNavigate)
{
    public string SpaceLabel => Space == RefSpace.FilePath ? string.Empty : Space.ToString();
}

/// <summary>
/// The Xrefs half of <see cref="MainViewModel"/>: building the reference graph in the background and
/// answering the two questions the panel asks of whatever file is selected.
/// </summary>
/// <remarks>
/// Kept in its own partial rather than added to <c>MainViewModel.cs</c>, which is already the largest
/// file in the app - this is a self-contained feature with its own background pass, its own state and
/// its own vocabulary, and nothing in it interleaves with mod layering or file browsing.
/// </remarks>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The base-archive index, kept so a mod toggle can rebuild only the (small) overlay on top of it
    /// instead of re-extracting the whole game.
    /// </summary>
    private ReferenceIndex _xrefIndex = ReferenceIndex.Empty;

    private ReferenceGraph _xrefs = ReferenceGraph.Empty;

    private string _xrefStatus = "Reference index not built yet.";

    /// <summary>What the panel shows in place of empty lists while the index is still being built -
    /// an empty list must never be able to mean "not ready", or every unindexed file would look
    /// like a file with no references.</summary>
    public string XrefStatus
    {
        get => _xrefStatus;
        private set { _xrefStatus = value; OnPropertyChanged(); }
    }

    private bool _xrefsReady;
    public bool XrefsReady
    {
        get => _xrefsReady;
        private set { _xrefsReady = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Phase 3 of startup, after the `.fcb` fragment pass: extract every reference in the game's base
    /// archives (or read back the index a previous launch already wrote), then layer this session's
    /// mod/workspace references on top.
    /// </summary>
    /// <remarks>
    /// Deliberately started after <see cref="LoadFragmentsAsync"/> rather than alongside it. Both are
    /// parallel, IO-heavy passes over the same archives; running them together would have them
    /// competing for the same cores and the same disk to finish neither any sooner. Ordering them
    /// also means the file tree - which fragments feed - is complete first, since that is what the
    /// user is looking at.
    ///
    /// The index lives in the app's own data folder (<see cref="AppConfig.XrefFile"/>), alongside
    /// <see cref="AppConfig.CacheFile"/> and for the same reasons - one place the user can clear when
    /// the game changes underneath the tool, and nothing of ours written into the game's own folders.
    /// It is keyed to whatever install <see cref="Config"/> currently points at; pointing the app at a
    /// different game is the same "delete the file" recovery the cache already documents.
    /// </remarks>
    public async Task BuildXrefsAsync()
    {
        if (_vfs is null || Install is null)
        {
            return;
        }

        GameVfs vfs = _vfs;
        string indexPath = AppConfig.XrefFile;
        var progress = new Progress<string>(s => XrefStatus = s);

        try
        {
            XrefStatus = "Loading reference index…";
            ReferenceIndex previous = await Task.Run(() => ReferenceIndex.Load(indexPath));

            ReferenceBuildResult build = await Task.Run(() =>
                ReferenceIndexer.BuildBaseIndex(vfs, ReferenceExtractors.All, previous, progress));

            _xrefIndex = build.Index;
            if (_xrefIndex.EdgeCount != previous.EdgeCount || _xrefIndex.IndexedFileCount != previous.IndexedFileCount)
            {
                await Task.Run(() => _xrefIndex.Save(indexPath));
            }

            await RefreshXrefOverlayAsync();

            XrefStatus = $"{_xrefs.BaseEdgeCount + _xrefs.OverlayEdgeCount:N0} references indexed"
                       + (build.Failures.Count > 0 ? $"  •  {build.Failures.Count:N0} file(s) wouldn't decode" : "");
            XrefsReady = true;
        }
        catch (Exception ex)
        {
            // The index is an aid, not a prerequisite: a failure here must leave the rest of the app
            // exactly as usable as it was before this feature existed.
            XrefStatus = $"Couldn't build the reference index: {ex.Message}";
        }
    }

    /// <summary>
    /// Re-extracts just the mod/workspace/patch files and rebuilds the overlay - what a mod toggle
    /// needs, and all it needs. The base index is untouched, so this costs a few hundred files rather
    /// than the ~66,000 a full pass reads.
    /// </summary>
    public async Task RefreshXrefOverlayAsync()
    {
        if (_vfs is null || !XrefsReady && _xrefIndex.EdgeCount == 0)
        {
            return;
        }

        GameVfs vfs = _vfs;
        ReferenceHarvest overlay = await Task.Run(() => ReferenceIndexer.HarvestOverlay(vfs, ReferenceExtractors.All));
        _xrefs = new ReferenceGraph(_xrefIndex, overlay);
        OnPropertyChanged(nameof(XrefsReady)); // nudges the panel to re-query the selected file
    }

    /// <summary>Everything that references <paramref name="file"/>.</summary>
    public IReadOnlyList<XrefRow> ReferencesTo(VfsFile file)
        => [.. _xrefs.ReferencesTo(RefSpace.FilePath, file.Hash)
            .Select(edge => new XrefRow(
                Display: DescribeFile(edge.SourceFile),
                Site: DescribeSite(edge),
                Kind: edge.Kind.ToString(),
                Space: RefSpace.FilePath,
                Target: edge.SourceFile,
                CanNavigate: FindByHash(edge.SourceFile) is not null))
            .OrderBy(row => row.Display, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Everything <paramref name="file"/> references.</summary>
    public IReadOnlyList<XrefRow> ReferencesFrom(VfsFile file)
        => [.. _xrefs.ReferencesFrom(file.Hash)
            .Select(edge => new XrefRow(
                Display: DescribeTarget(edge),
                Site: DescribeSite(edge),
                Kind: edge.Kind.ToString(),
                Space: edge.TargetSpace,
                Target: edge.Target,
                CanNavigate: CanNavigateTo(edge.TargetSpace, edge.Target)))
            .OrderBy(row => row.Site, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Display, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Selects whatever <paramref name="space"/>/<paramref name="id"/> refers to. A file hash goes
    /// straight to that file; any other space goes to the file that *defines* the id (an `.spk` bank
    /// for a sound id, say). False when neither is known - the row is rendered as non-navigable in
    /// that case, so this is a guard rather than the usual path.
    /// </summary>
    public bool TryNavigateToReference(RefSpace space, uint id)
    {
        VfsFile? target = ResolveReference(space, id);
        if (target is null)
        {
            return false;
        }
        NavigateTo(target);
        return true;
    }

    private bool CanNavigateTo(RefSpace space, uint id) => ResolveReference(space, id) is not null;

    private VfsFile? ResolveReference(RefSpace space, uint id)
    {
        if (space == RefSpace.FilePath)
        {
            return FindByHash(id);
        }
        return _xrefs.TryGetDefinition(space, id, out RefDefinition definition)
            ? FindByHash(definition.DefiningFile)
            : null;
    }

    /// <summary>
    /// The "where in the source file" text. Almost always a member/slot name straight out of the
    /// index's name table, but a `depload.dat` sites its edges by the *parent resource's hash* (it
    /// has no field names at all), and that hash is a filename the filelist can usually resolve - so
    /// those rows read as a path instead of eight hex digits.
    /// </summary>
    private string DescribeSite(RefEdge edge)
        => RefKinds.SiteIsFileHash(edge.Kind)
            ? DescribeFile(edge.SiteKey)
            : _xrefs.DescribeSite(edge);

    /// <summary>A file's path, or its bare hash when the filelist never named it - a quarter of the
    /// game's entries are in that state and are still perfectly navigable.</summary>
    private string DescribeFile(uint hash)
        => FindByHash(hash)?.Path ?? $"#{hash:X8}";

    /// <summary>
    /// The display text for an outgoing edge's far end. A file reference reads as a path; anything
    /// else reads as its recovered name where the data spelled one out, and <c>#XXXXXXXX</c>
    /// otherwise - the same convention <c>MgbNameLookup.Describe</c> established, so an unresolved
    /// hash looks the same everywhere in the app.
    /// </summary>
    private string DescribeTarget(RefEdge edge)
    {
        if (edge.TargetSpace == RefSpace.FilePath)
        {
            return DescribeFile(edge.Target);
        }

        string? name = _xrefs.Name(edge.Target);
        string rendered = name ?? $"#{edge.Target:X8}";
        return _xrefs.TryGetDefinition(edge.TargetSpace, edge.Target, out RefDefinition definition)
            ? $"{rendered}  (in {DescribeFile(definition.DefiningFile)})"
            : rendered;
    }

    // ------------------------------------------------------------ navigation history

    /// <summary>
    /// Where the user has been, so following a chain of references can be undone. Bounded because an
    /// hour of clicking through xrefs would otherwise pin every visited file's row in memory for the
    /// life of the session, and nobody navigates back more than a handful of steps.
    /// </summary>
    private readonly List<uint> _navigationBack = [];
    private readonly List<uint> _navigationForward = [];
    private const int MaxNavigationHistory = 64;

    /// <summary>True while <see cref="NavigateTo"/> is replaying history, so the replay itself isn't
    /// recorded as a new step - without this, going back once and forward once would leave two copies
    /// of the same entry and the stacks would never drain.</summary>
    private bool _replayingNavigation;

    public bool CanNavigateBack => _navigationBack.Count > 0;
    public bool CanNavigateForward => _navigationForward.Count > 0;

    /// <summary>Records the file being left, called by <see cref="NavigateTo"/> before it moves.</summary>
    private void PushNavigationHistory(VfsFile? leaving)
    {
        if (_replayingNavigation || leaving is null)
        {
            return;
        }

        if (_navigationBack.Count > 0 && _navigationBack[^1] == leaving.Hash)
        {
            return;
        }

        _navigationBack.Add(leaving.Hash);
        if (_navigationBack.Count > MaxNavigationHistory)
        {
            _navigationBack.RemoveAt(0);
        }

        // A fresh navigation invalidates the forward chain, the same way a browser's does.
        _navigationForward.Clear();
        RaiseNavigationState();
    }

    public void NavigateBack() => Step(_navigationBack, _navigationForward);

    public void NavigateForward() => Step(_navigationForward, _navigationBack);

    private void Step(List<uint> from, List<uint> to)
    {
        while (from.Count > 0)
        {
            uint hash = from[^1];
            from.RemoveAt(from.Count - 1);

            VfsFile? target = FindByHash(hash);
            if (target is null)
            {
                continue; // a mod toggle removed it since - skip rather than dead-end on it
            }

            if (SelectedFile is { } current)
            {
                to.Add(current.Hash);
            }

            _replayingNavigation = true;
            try
            {
                NavigateTo(target);
            }
            finally
            {
                _replayingNavigation = false;
            }
            break;
        }
        RaiseNavigationState();
    }

    private void RaiseNavigationState()
    {
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(CanNavigateForward));
    }
}
