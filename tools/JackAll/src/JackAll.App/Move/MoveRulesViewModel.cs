using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Format.Move;
using JackAll.Core.Format.Move.Rules;
using JackAll.Core.Mods;
using JackAll.Core.Vfs;

namespace JackAll.App.Move;

/// <summary>
/// The Animations tab: a MOVE graph open as rules, narrowed to one weapon and one state, judged
/// against the situation the user sets, and saved as the fragments its edits change.
/// </summary>
public sealed partial class MoveRulesViewModel(MainViewModel vm) : Observable
{
    private readonly MoveSituation _situation = new();
    private readonly Dictionary<uint, string> _typedPaths = [];
    private readonly Dictionary<MoveObject, IReadOnlyList<(MoveRule Rule, string Text)>> _ruleText = [];
    private MoveEditSession? _session;
    private string? _graphPath;
    private IReadOnlyDictionary<int, IReadOnlySet<uint>> _clipsByWeapon = new Dictionary<int, IReadOnlySet<uint>>();
    private IReadOnlyList<MoveStateRow> _stateRows = [];

    /// <summary>Set while a picker's list is replaced, when the control clears its selection on its own.</summary>
    private bool _swapping;

    private IReadOnlyList<string> _graphs = [];
    private string _status = "Pick a graph and Load";
    private string? _message;
    private bool _isBusy;
    private IReadOnlyList<MoveOption> _weapons = [];
    private MoveOption? _weapon;
    private bool _ownSituationsOnly = true;
    private string _stateFilter = string.Empty;
    private IReadOnlyList<MoveStateRow> _states = [];
    private MoveStateRow? _selectedState;
    private IReadOnlyList<MoveSituationRow> _situationRows = [];
    private string _situationSummary = "Situation: any";
    private IReadOnlyList<MoveRuleRow> _rules = [];
    private MoveRuleRow? _selectedRule;
    private IReadOnlyList<MoveClipRow> _clips = [];
    private IReadOnlyList<MoveConditionRow> _conditions = [];
    private MoveConditionRow? _selectedCondition;
    private MoveClipForm _clipForm = new([]);
    private MoveConditionForm? _conditionForm;
    private string _detailTitle = string.Empty;
    private string _detailNote = string.Empty;
    private string _techs = string.Empty;
    private string? _goToName;
    private string _cloneTarget = string.Empty;
    private string _clonePackage = string.Empty;

    public IReadOnlyList<string> Graphs
    {
        get => _graphs;
        private set
        {
            if (Set(ref _graphs, value))
            {
                OnPropertyChanged(nameof(CanLoad));
            }
        }
    }

    public string Status { get => _status; private set => Set(ref _status, value); }

    /// <summary>The outcome of the last edit, shown until the next one: a refusal or a warning.</summary>
    public string? Message
    {
        get => _message;
        private set
        {
            if (Set(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanLoad));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool CanLoad => Graphs.Count > 0 && !IsBusy;

    public bool CanSave => IsDirty && !IsBusy;

    public bool IsLoaded => _session is not null;

    public bool IsDirty => _session?.IsDirty == true;

    public MoveChannels? Channels => _session?.Channels;

    public MoveFile? File => _session?.File;

    public IReadOnlyList<MoveOption> Weapons { get => _weapons; private set => Set(ref _weapons, value); }

    public MoveOption? Weapon
    {
        get => _weapon;
        set
        {
            if (_swapping || !Set(ref _weapon, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasWeapon));
            _situation[MoveWeapons.EquippedWeaponChannel] = value?.Value;
            _situation[MoveWeapons.DesiredWeaponChannel] = value?.Value;
            RefreshStates();
            RefreshRules();
        }
    }

    public bool HasWeapon => Weapon?.Value is not null;

    /// <summary>Only the states where the picked weapon has animations of its own.</summary>
    public bool OwnSituationsOnly
    {
        get => _ownSituationsOnly;
        set
        {
            if (Set(ref _ownSituationsOnly, value) && RefreshStates())
            {
                RefreshRules();
            }
        }
    }

    public string StateFilter
    {
        get => _stateFilter;
        set
        {
            if (Set(ref _stateFilter, value) && RefreshStates())
            {
                RefreshRules();
            }
        }
    }

    public IReadOnlyList<MoveStateRow> States { get => _states; private set => Set(ref _states, value); }

    public MoveStateRow? SelectedState
    {
        get => _selectedState;
        set
        {
            if (!_swapping && Set(ref _selectedState, value))
            {
                RefreshRules();
            }
        }
    }

    public IReadOnlyList<MoveSituationRow> SituationRows
    {
        get => _situationRows;
        private set
        {
            if (Set(ref _situationRows, value))
            {
                OnPropertyChanged(nameof(HasSituationRows));
            }
        }
    }

    public bool HasSituationRows => SituationRows.Count > 0;

    /// <summary>The situation button's caption: how many of the rows are set.</summary>
    public string SituationSummary { get => _situationSummary; private set => Set(ref _situationSummary, value); }

    public IReadOnlyList<MoveRuleRow> Rules { get => _rules; private set => Set(ref _rules, value); }

    public MoveRuleRow? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (!_swapping && Set(ref _selectedRule, value))
            {
                OnPropertyChanged(nameof(HasRule));
                RefreshDetail();
            }
        }
    }

    public bool HasRule => SelectedRule is not null;

    public IReadOnlyList<MoveClipRow> Clips { get => _clips; private set => Set(ref _clips, value); }

    public MoveClipForm ClipForm { get => _clipForm; private set => Set(ref _clipForm, value); }

    public IReadOnlyList<MoveConditionRow> Conditions
    {
        get => _conditions;
        private set => Set(ref _conditions, value);
    }

    public MoveConditionRow? SelectedCondition
    {
        get => _selectedCondition;
        set
        {
            if (Set(ref _selectedCondition, value) && value is not null)
            {
                ConditionForm?.Load(value.Condition);
            }
        }
    }

    public MoveConditionForm? ConditionForm
    {
        get => _conditionForm;
        private set => Set(ref _conditionForm, value);
    }

    public string DetailTitle { get => _detailTitle; private set => Set(ref _detailTitle, value); }

    public string DetailNote { get => _detailNote; private set => Set(ref _detailNote, value); }

    public string Techs { get => _techs; private set => Set(ref _techs, value); }

    /// <summary>The situation a "go to" rule continues in, or null for any other rule.</summary>
    public string? GoToName
    {
        get => _goToName;
        private set
        {
            if (Set(ref _goToName, value))
            {
                OnPropertyChanged(nameof(HasGoTo));
            }
        }
    }

    public bool HasGoTo => GoToName is not null;

    public string CloneTarget { get => _cloneTarget; set => Set(ref _cloneTarget, value); }

    public string ClonePackage { get => _clonePackage; set => Set(ref _clonePackage, value); }

    /// <summary>The graphs a mod can stage fragments of, the base one first.</summary>
    public void Initialize()
    {
        Graphs =
        [
            .. vm.AllKnownPaths
                .Where(p => MoveContainerSplitter.IsMoveGraph(Path.GetFileName(p)))
                .OrderBy(p => !Path.GetFileName(p).StartsWith("movemgr", StringComparison.OrdinalIgnoreCase))
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase),
        ];
        Status = Graphs.Count > 0 ? "Pick a graph and Load" : "No MOVE graphs found";
    }

    public async Task LoadAsync(string path)
    {
        IsBusy = true;
        Status = "Loading…";
        try
        {
            byte[] graph = vm.ReadByPath(path) ?? throw new MoveFormatException($"{path} could not be read");
            byte[]? vanilla = vm.FindByHash(NameHash.Compute(path)) is { } file ? vm.ReadOriginal(file) : null;

            // Channel names live only in the base graph's authoring twin; an expansion has no table of its own.
            byte[]? named = vm.ReadByPath(Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, "movemgrnamed.bin"));
            List<string> clips = [.. vm.AllKnownPaths.Where(p => p.EndsWith(".mab", StringComparison.OrdinalIgnoreCase))];

            (MoveEditSession session, string[] library, IReadOnlyDictionary<int, IReadOnlySet<uint>> byWeapon) =
                await Task.Run(() =>
                {
                    MoveEditSession opened = new(
                        graph, vanilla, BundledAssets.LoadMoveNames(), named is null ? null : MoveCodec.ChannelTable(named));
                    string[] sorted = [.. clips.Order(StringComparer.OrdinalIgnoreCase)];
                    return (opened, sorted, MoveWeapons.ClipsByWeapon(opened.File));
                });

            _session = session;
            _graphPath = path;
            _clipsByWeapon = byWeapon;
            ClipForm = new MoveClipForm(library);
            ConditionForm = new MoveConditionForm(session.Channels);
            OnPropertyChanged(nameof(IsLoaded));
            Status = $"{session.Rules.States.Count:N0} states, "
                     + $"{session.Rules.States.Sum(s => session.Rules.RulesOf(s).Count):N0} rules"
                     + (session.Channels.Named is null ? " - no channel names beside it" : string.Empty);
            Reload();
        }
        catch (Exception ex) when (ex is MoveFormatException or IOException or InvalidDataException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Picks the situation the selected "go to" rule continues in.</summary>
    public void OpenGoTo()
    {
        if (SelectedRule?.Rule.Target is not { } target || _stateRows.FirstOrDefault(s => s.State == target) is not { } row)
        {
            return;
        }

        if (!States.Contains(row))
        {
            _stateFilter = string.Empty;
            _ownSituationsOnly = false;
            OnPropertyChanged(nameof(StateFilter));
            OnPropertyChanged(nameof(OwnSituationsOnly));
            RefreshStates();
        }

        SelectedState = row;
    }

    public void ResetSituation()
    {
        foreach (MoveSituationRow row in SituationRows)
        {
            _situation[row.Channel] = null;
        }

        RefreshRules();
    }

    public void ApplyClip()
    {
        if (_session is not { } session || ClipForm.Clip is not { } clip)
        {
            return;
        }

        MoveClipForm form = ClipForm;
        Edit(() =>
        {
            MoveObject owner = clip.Site.Owner;
            string path = form.Path.Trim();
            if (path != clip.Path && NameHash.Compute(path) is var hash && hash != clip.Site.Hash)
            {
                session.SetClip(owner, hash);
                _typedPaths[hash] = path;
            }

            SetTiming(owner, "m_flBlendTime", form.Blend);
            SetTiming(owner, "m_flMultiplier", form.Speed);
            SetTiming(owner, "m_flStartTime", form.Start);
            SetTiming(owner, "m_flStopTime", form.Stop);
            if (form.HasTiming && (owner.Field("m_fInterruptible") is > 0) != form.Interruptible)
            {
                session.SetNumber(owner, "m_fInterruptible", form.Interruptible ? 1u : 0u);
            }

            return path == clip.Path || form.IsShipped(path)
                ? null
                : "No shipped archive has this path. A clip at an invented path does not load in game - reuse a "
                  + "path the game already has.";
        });
    }

    public void UpdateCondition()
    {
        if (SelectedCondition is { } row && ReadConditionForm() is { } spec)
        {
            Edit(() => _session!.SetCondition(row.Owner, row.Condition, spec));
        }
    }

    public void AddCondition()
    {
        if (SelectedRule is { } rule && ReadConditionForm() is { } spec)
        {
            Edit(() => _session!.AddCondition(rule.Rule.Node, spec));
        }
    }

    public void RemoveCondition()
    {
        if (SelectedCondition is { } row)
        {
            Edit(() => _session!.RemoveCondition(row.Owner, row.Condition));
        }
    }

    public void Duplicate()
    {
        if (SelectedRule is { } rule)
        {
            MoveObject? copy = null;
            Edit(
                () =>
                {
                    copy = _session!.Duplicate(rule.Rule);
                    return "The copy sits just above the original and wins while their conditions are the same. "
                           + "Add a condition to it so it only plays where the new animation should.";
                },
                () => copy);
        }
    }

    public void Move(int delta)
    {
        if (SelectedRule is { } rule)
        {
            Edit(() => _session!.Move(rule.Rule, delta));
        }
    }

    public void Delete()
    {
        if (SelectedRule is { } rule)
        {
            Edit(() => _session!.Delete(rule.Rule), () => null);
        }
    }

    public void CloneWeapon()
    {
        if (_session is not { } session || Weapon?.Value is not { } donor)
        {
            return;
        }

        if (!int.TryParse(CloneTarget, NumberStyles.Integer, CultureInfo.InvariantCulture, out int target) || target < 0)
        {
            Message = "The new index has to be a whole number, such as the weapon archetype's iAnimationValue.";
            return;
        }

        string? package = string.IsNullOrWhiteSpace(ClonePackage) ? null : ClonePackage.Trim();
        Edit(() =>
        {
            MoveCloneResult result = session.CloneWeapon((int)donor, target, package);
            string skipped = result.Skipped.Count > 0 ? $" Skipped: {string.Join("; ", result.Skipped)}." : string.Empty;
            return $"Copied {result.Branches} branches in {result.States.Count} states to index {target}.{skipped} "
                   + "No graph built this way has been loaded by the game yet.";
        });
        RefreshWeapons();
    }

    /// <summary>Stages the fragments this session changed; the text is a refusal, or null when saved.</summary>
    public async Task<string?> SaveAsync()
    {
        if (_session is not { IsDirty: true } session
            || vm.FindByHash(NameHash.Compute(_graphPath!)) is not { } container)
        {
            return null;
        }

        IsBusy = true;
        try
        {
            MoveSavePlan plan = await Task.Run(session.Plan);
            vm.StageFragments(container, plan.Changes.Select(c => (c.Id, c.Xml, c.IsVanilla)));
            session.Commit(plan);
            Status = $"Staged {plan.Changes.Count} fragment(s) into the workspace";
            RaiseDirty();
            return null;
        }
        catch (MoveEditException ex)
        {
            return ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Revert()
    {
        if (_session is null)
        {
            return;
        }

        _session.Revert();
        _clipsByWeapon = MoveWeapons.ClipsByWeapon(_session.File);
        Message = null;
        Reload();
        RaiseDirty();
    }

    /// <summary>A clip path for a hash, or null when no known path hashes to it.</summary>
    public string? PathOf(uint hash) => _typedPaths.TryGetValue(hash, out string? typed) ? typed : vm.PathOf(hash);

    /// <summary>Everything that depends on which objects the session holds, after a load or a revert.</summary>
    private void Reload()
    {
        MoveEditSession session = _session!;
        uint? picked = SelectedState?.Hash;
        _ruleText.Clear();
        _stateRows =
        [
            .. session.Rules.States
                .Select(s => (State: s, Name: session.NameOf(s)))
                .Select(s => new MoveStateRow(s.State, MoveStateIndex.NameHashOf(s.State) ?? 0, s.Name, GroupOf(s.Name)))
                .OrderBy(s => s.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase),
        ];
        _selectedState = _stateRows.FirstOrDefault(s => s.Hash == picked);
        RefreshWeapons();
        RefreshStates();
        RefreshRules();
    }

    private void Edit(Action edit, Func<MoveObject?>? select = null) => Edit(() =>
    {
        edit();
        return null;
    }, select);

    private void Edit(Func<string?> edit, Func<MoveObject?>? select = null)
    {
        if (_session is null)
        {
            return;
        }

        MoveObject? keep = SelectedRule?.Rule.Node;
        try
        {
            Message = edit();
        }
        catch (MoveEditException ex)
        {
            Message = ex.Message;
            return;
        }

        _clipsByWeapon = MoveWeapons.ClipsByWeapon(_session.File);
        _ruleText.Clear();
        RefreshRules(select is null ? keep : select());
        RaiseDirty();
    }

    private void SetTiming(MoveObject owner, string field, string text)
    {
        if (owner.FieldF32(field) is { } current
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            && value != current)
        {
            _session!.SetFloat(owner, field, value);
        }
    }

    private MoveConditionSpec? ReadConditionForm()
    {
        if (ConditionForm is null)
        {
            return null;
        }

        MoveConditionSpec? spec = ConditionForm.ToSpec(out string? error);
        Message = error;
        return spec;
    }

    private void RefreshWeapons()
    {
        MoveEditSession session = _session!;
        IReadOnlyList<string>? names = session.Channels.ValuesOf(MoveWeapons.EquippedWeaponChannel);
        List<int> indices = [.. Enumerable.Range(0, names?.Count ?? 0).Union(MoveWeapons.Indices(session.File)).Order()];
        double? current = Weapon?.Value;
        Swap(() => Weapons = [new MoveOption(null, "All weapons"), .. indices.Select(i => new MoveOption(i, WeaponName(i)))]);
        _weapon = Weapons.FirstOrDefault(w => w.Value == current) ?? Weapons[0];
        OnPropertyChanged(nameof(Weapon));
        OnPropertyChanged(nameof(HasWeapon));
        CloneTarget = (indices.DefaultIfEmpty(-1).Max() + 1).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Narrows the situation list; true when that changed which state is picked.</summary>
    private bool RefreshStates()
    {
        if (_session is null)
        {
            return false;
        }

        MoveRuleSet rules = _session.Rules;
        int? weapon = (int?)Weapon?.Value;
        string[] words = FilterWords;
        MoveStateRow? before = _selectedState;
        Swap(() => States =
        [
            .. _stateRows.Where(s =>
                (weapon is null || !OwnSituationsOnly || rules.RulesOf(s.State).Any(r => r.Pin?.Weapon == weapon))
                && (Matches(s.Name, string.Empty, words)
                    || TextOf(s.State).Any(t => Shows(t.Rule, weapon) && Matches(s.Name, t.Text, words)))),
        ]);
        _selectedState = before is not null && States.Contains(before) ? before : null;
        OnPropertyChanged(nameof(SelectedState));
        return _selectedState != before;
    }

    private void Swap(Action replace)
    {
        _swapping = true;
        try
        {
            replace();
        }
        finally
        {
            _swapping = false;
        }
    }

    private void RefreshRules() => RefreshRules(SelectedRule?.Rule.Node);

    private void RefreshRules(MoveObject? keep)
    {
        if (_session is null || SelectedState is null)
        {
            SituationRows = [];
            Rules = [];
            SelectedRule = null;
            return;
        }

        int? weapon = (int?)Weapon?.Value;
        MoveRuleSet rules = _session.Rules;
        List<MoveRule> shown = [.. rules.RulesOf(SelectedState.State).Where(r => Shows(r, weapon))];

        // The states its "go to" rules enter test channels too, and those decide what plays here.
        IEnumerable<MoveRule> tested = shown.Concat(
            rules.Reachable(SelectedState.State).SelectMany(rules.RulesOf).Where(r => Shows(r, weapon)));

        Swap(() => Rules = [.. shown.Select(r => new MoveRuleRow(r, WeaponText(r), WhenText(r), PlaysText(r), BlendText(r), SharedText(r)))]);
        SituationRows = [.. SituationChannels(tested, weapon is not null).Select(SituationRow)];
        Reevaluate();

        string[] words = FilterWords;
        string state = SelectedState.Name;
        SelectedRule = Rules.FirstOrDefault(r => r.Rule.Node == keep)
            ?? (words.Length > 0 ? Rules.FirstOrDefault(r => Matches(state, RuleText(r.Rule), words)) : null);
    }

    private string[] FilterWords => StateFilter.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static bool Shows(MoveRule rule, int? weapon) => weapon is null || rule.Pin is null || rule.Pin.Value.Weapon == weapon;

    /// <summary>Every word appears in the situation's name or in one rule's text.</summary>
    private static bool Matches(string state, string rule, string[] words)
        => words.All(w => state.Contains(w, StringComparison.OrdinalIgnoreCase) || rule.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>What the Situations filter matches a rule by: its weapon, conditions and what it plays.</summary>
    private string RuleText(MoveRule rule) => $"{WeaponText(rule)} {WhenText(rule)} {PlaysText(rule)}";

    private IReadOnlyList<(MoveRule Rule, string Text)> TextOf(MoveObject state)
    {
        if (!_ruleText.TryGetValue(state, out IReadOnlyList<(MoveRule, string)>? texts))
        {
            texts = [.. _session!.Rules.RulesOf(state).Select(r => (r, RuleText(r)))];
            _ruleText[state] = texts;
        }

        return texts;
    }

    /// <summary>Reads the situation rows and marks which rules play.</summary>
    private void Reevaluate()
    {
        foreach (MoveSituationRow row in SituationRows)
        {
            _situation[row.Channel] = row.IsChoice ? row.Selected?.Value
                : _session!.Channels.TryParse(row.Channel, row.Text, out double value) ? value : null;
        }

        int set = SituationRows.Count(r => _situation[r.Channel] is not null);
        SituationSummary = set == 0 ? "Situation: any" : $"Situation: {set} set";
        if (_session is null || SelectedState is null)
        {
            return;
        }

        IReadOnlyList<MoveVerdict> verdicts = _situation.Resolve(_session.Rules.RulesOf(SelectedState.State), _session.Rules);
        foreach (MoveRuleRow row in Rules)
        {
            row.Verdict = verdicts[row.Rule.Order];
        }
    }

    private void RefreshDetail()
    {
        if (_session is null || SelectedRule?.Rule is not { } rule)
        {
            DetailTitle = string.Empty;
            DetailNote = string.Empty;
            Clips = [];
            Conditions = [];
            Techs = string.Empty;
            GoToName = null;
            ClipForm.Clip = null;
            return;
        }

        DetailTitle = $"Rule {rule.Order + 1} · {KindText(rule)}";
        List<string> notes = [];
        if (rule.Pin is null)
        {
            notes.Add("Not specific to a weapon: every weapon that reaches this state plays it.");
        }

        if (rule.Entry is not null)
        {
            notes.Add("Only reached through a transition, so it is tried in a search of its own.");
        }

        if (_session.Index.StateOf(rule.Node) is { } home && home != _session.Index.StateOf(rule.State))
        {
            notes.Add($"It is stored in {_session.NameOf(home)}, so a change to it changes that state too.");
        }

        DetailNote = string.Join(" ", notes);

        Clips = [.. rule.Clips.Select(c => new MoveClipRow(c, RoleText(rule, c), ClipPath(c.Hash)))];
        ClipForm.Clip = Clips.FirstOrDefault();

        List<MoveConditionRow> conditions = [];
        IReadOnlyList<MoveRule> siblings = _session.Rules.RulesOf(rule.State);
        foreach (MoveObject node in rule.Chain)
        {
            IReadOnlyList<MoveCondition> list = MoveCondition.Of(node);
            string source = node == rule.Node ? "this rule" : $"group, {siblings.Count(r => r.Chain.Contains(node))} rules";
            for (int i = 0; i < list.Count; i++)
            {
                string join = i == 0 ? string.Empty : list[i].IsOr ? "or " : "and ";
                conditions.Add(new MoveConditionRow(node, list[i], join + list[i].Describe(_session.Channels), source));
            }
        }

        Conditions = conditions;
        SelectedCondition = null;
        Techs = string.Join(Environment.NewLine, TechLines(rule.Node));
        GoToName = rule.Target is { } target ? _session.NameOf(target) : null;
    }

    private IEnumerable<int> SituationChannels(IEnumerable<MoveRule> rules, bool weaponPicked)
        => rules.SelectMany(r => r.Chain)
            .Distinct()
            .SelectMany(MoveCondition.Of)
            .Select(c => c.Channel)
            .Where(c => !weaponPicked || c is not (MoveWeapons.EquippedWeaponChannel or MoveWeapons.DesiredWeaponChannel))
            .Distinct();

    private MoveSituationRow SituationRow(int channel)
    {
        MoveChannels channels = _session!.Channels;
        IReadOnlyList<MoveOption>? options = MoveOption.ValuesOf(channels, channel) is { } values
            ? [new MoveOption(null, "any"), .. values]
            : null;

        MoveSituationRow row = new(channel, channels.NameOf(channel), options);
        if (_situation[channel] is { } value)
        {
            if (options is not null)
            {
                row.Selected = options.FirstOrDefault(o => o.Value == value) ?? options[0];
            }
            else
            {
                row.Text = channels.ToText(channel, value);
            }
        }

        row.PropertyChanged += (_, _) => Reevaluate();
        return row;
    }

    private string WeaponName(int index) => _session!.Channels.Format(MoveWeapons.EquippedWeaponChannel, index);

    private string WeaponText(MoveRule rule) => rule.Pin switch
    {
        null => "any",
        { Channel: MoveWeapons.DesiredWeaponChannel } pin => "→ " + WeaponName(pin.Weapon),
        { } pin => WeaponName(pin.Weapon),
    };

    /// <summary>Every test on the way down, the weapon pin left to the Weapon column.</summary>
    private string WhenText(MoveRule rule)
    {
        List<string> parts = rule.Entry is null ? [] : ["after a transition"];
        foreach (MoveObject node in rule.Chain)
        {
            IReadOnlyList<MoveCondition> list = MoveCondition.Of(node);
            if (list.Count > 0 && !(node == rule.PinOwner && list.Count == 1))
            {
                parts.Add(MoveCondition.Describe(list, _session!.Channels));
            }
        }

        if (rule.IsDisabled)
        {
            parts.Add("disabled");
        }

        return parts.Count == 0 ? "always" : string.Join(" · ", parts);
    }

    private string KindText(MoveRule rule) => rule.Kind switch
    {
        MoveRuleKind.FullBody => "Full body",
        MoveRuleKind.Layered => "Layered",
        MoveRuleKind.Blend => $"Blend on {Axis(rule)}",
        MoveRuleKind.LayeredBlend => $"Layered blend on {Axis(rule)}",
        MoveRuleKind.Variants => "Variants",
        MoveRuleKind.Synced => "Synced",
        MoveRuleKind.Pose => "Pose",
        MoveRuleKind.Nothing => "Nothing",
        MoveRuleKind.EnterState => rule.Node.FieldTarget("m_state") is { } state
            ? $"Go to {_session!.NameOf(state)}"
            : "Go to a state",
        _ => rule.Kind.ToString(),
    };

    private string Axis(MoveRule rule)
        => rule.Node.Field("m_eAxisValueID") is { } channel ? _session!.Channels.NameOf((int)channel) : "an axis";

    private string PlaysText(MoveRule rule)
    {
        List<MoveClipSite> own = [.. rule.Clips.Where(c => c.Role is not (MoveClipRole.Transition or MoveClipRole.GroupTransition))];
        string kind = KindText(rule);
        if (own.Count == 0)
        {
            return kind;
        }

        string first = Path.GetFileNameWithoutExtension(ClipPath(own[0].Hash));
        return own.Count == 1 ? $"{kind} · {first}" : $"{kind} · {first} +{own.Count - 1}";
    }

    private static string BlendText(MoveRule rule)
        => rule.Node.FieldF32("m_flBlendTime")?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>Other weapons whose own branches play any of this rule's clips.</summary>
    private string SharedText(MoveRule rule)
    {
        HashSet<uint> clips = [.. rule.Clips.Select(c => c.Hash)];
        List<string> others =
        [
            .. _clipsByWeapon
                .Where(p => p.Key != rule.Pin?.Weapon && p.Value.Overlaps(clips))
                .OrderBy(p => p.Key)
                .Select(p => WeaponName(p.Key)),
        ];
        return others.Count <= 3 ? string.Join(", ", others) : $"{others.Count} weapons";
    }

    private string RoleText(MoveRule rule, MoveClipSite site) => site.Role switch
    {
        MoveClipRole.Main => "plays",
        MoveClipRole.BlendSample when rule.Node.Field("m_eAxisValueID") is { } axis
            => $"at {Axis(rule)} = {_session!.Channels.Format((int)axis, site.Position)}",
        MoveClipRole.BlendSample => "blend sample",
        MoveClipRole.Variant => "variant",
        MoveClipRole.Synced => "synced part",
        MoveClipRole.Transition => "on leaving, to " + TargetText(site.Link),
        _ => "on leaving the group, to " + TargetText(site.Link),
    };

    private string TargetText(MoveObject? link)
    {
        if (link?.FieldTarget("m_ptr") is not { } target)
        {
            return "?";
        }

        if (target.Field("m_animNameHash") is { } clip)
        {
            return Path.GetFileNameWithoutExtension(ClipPath(clip));
        }

        return _session!.Index.StateOf(target) is { } state ? _session.NameOf(state) : target.ClassName;
    }

    private string ClipPath(uint hash) => PathOf(hash) ?? $"unknown 0x{hash:X8}";

    private IEnumerable<string> TechLines(MoveObject node)
    {
        foreach (MoveOp op in node.Ops)
        {
            if (op.Name != "CAnimTech" || op.Target is not { } tech)
            {
                continue;
            }

            string part = (tech.Field("m_anchorPartName") ?? tech.Field("m_iModelHashNamePartID")) is { } id
                ? _session!.Names.Of(id) ?? $"0x{id:X8}"
                : string.Empty;
            string bone = tech.Ops.FirstOrDefault(o => o.Name == "m_parentBoneName.m_szName").Bytes is { } bytes
                ? MoveText.Printable(bytes) ?? string.Empty
                : string.Empty;
            string what = tech.ClassName.Replace("CAnimTech", string.Empty, StringComparison.Ordinal);
            yield return bone.Length > 0 ? $"{what}: {part} on {bone}" : $"{what}: {part}";
        }
    }

    /// <summary>A heading for the situation list, from the state's naming convention.</summary>
    private static string GroupOf(string name)
    {
        string[] parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || MissionPrefix().IsMatch(parts[0]))
        {
            return "Missions and scenes";
        }

        return parts[0].ToLowerInvariant() switch
        {
            "pawn" when parts.Length > 2 => "Pawn · " + Title(parts[1]),
            "state" => "Unnamed",
            _ => Title(parts[0]),
        };

        static string Title(string word) => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
    }

    [GeneratedRegex(@"^(ml|sm|gm)\d", RegexOptions.IgnoreCase)]
    private static partial Regex MissionPrefix();

    private void RaiseDirty()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanSave));
    }
}
