using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>One character's detail, addressed by name through the Shell route.</summary>
public sealed partial class CharacterViewModel(
    CharacterService characters, StoryService stories, INavigator navigator)
    : BaseViewModel, IQueryTarget
{
    [ObservableProperty] private string _characterName = string.Empty;
    [ObservableProperty] private string? _subtitle;

    /// <summary>The elite specialization the character runs, whose painting heads the page.</summary>
    [ObservableProperty] private string? _eliteName;

    /// <summary>The character's profession, whose painting heads the page where there is no elite.</summary>
    [ObservableProperty] private string? _profession;
    [ObservableProperty] private string? _playtime;
    [ObservableProperty] private string? _deaths;
    [ObservableProperty] private string? _created;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasHeroChallenges))] private string? _heroChallenges;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasMasteryNote))] private string? _masteryNote;

    public bool HasMasteryNote => MasteryNote is not null;

    /// <summary>Omit the tile rather than show a zero the API cannot vouch for.</summary>
    public bool HasHeroChallenges => HeroChallenges is not null;

    [ObservableProperty] private string? _bodyImage;

    /// <summary>
    /// Which equipment template is on screen. Null means the one being worn — a character
    /// opened fresh should show what it is actually wearing, not tab 1 by position.
    /// </summary>
    private int? _loadout;

    public ObservableCollection<LoadoutOption> LoadoutTabs { get; } = [];

    public ObservableCollection<EquipmentRow> PvpGear { get; } = [];

    public ObservableCollection<DyeSwatch> Dyes { get; } = [];

    /// <summary>Nothing to show for a character with no dyed armour at all.</summary>
    public bool HasDyes => Dyes.Count > 0;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPvpNotice))] private string? _pvpNotice;

    public bool HasPvpNotice => PvpNotice is not null;

    /// <summary>One template is no choice; the strip only earns its space from two.</summary>
    public bool HasLoadouts => LoadoutTabs.Count > 1;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasBuildNotice))] private string? _buildNotice;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasTemplateName))] private string? _templateName;

    public bool HasTemplateName => TemplateName is not null;

    public bool HasBuildNotice => BuildNotice is not null;

    [ObservableProperty] private string? _checkScore;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasAllClear))] private string? _allClear;
    [ObservableProperty] private string? _checkCaveat;

    public bool HasAllClear => AllClear is not null;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasStatPrefix))] private string? _statPrefix;
    [ObservableProperty] private string? _shoppingTotal;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasShoppingNote))] private string? _shoppingNote;
    [ObservableProperty] private string? _shoppingCaveat;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverview))]
    [NotifyPropertyChangedFor(nameof(IsBuild))]
    [NotifyPropertyChangedFor(nameof(IsGear))]
    [NotifyPropertyChangedFor(nameof(IsCheck))]
    [NotifyPropertyChangedFor(nameof(IsStory))]
    private int _selectedTab;

    public bool IsOverview => SelectedTab == 0;
    public bool IsBuild => SelectedTab == 1;
    public bool IsGear => SelectedTab == 2;
    public bool IsCheck => SelectedTab == 3;
    public bool IsStory => SelectedTab == 4;

    public bool HasStatPrefix => StatPrefix is not null;
    public bool HasShoppingNote => ShoppingNote is not null;
    public bool HasShoppingTotal => ShoppingTotal is not null;

    /// <summary>
    /// Four views of one character, because seven sections on a phone is a scroll, not a
    /// page. Same idea as the Account area's tabs, in the only place a pushed page can put
    /// them.
    /// </summary>
    public ObservableCollection<PageTab> Tabs { get; } =
    [
        new(L.T("Overview"), 0), new(L.T("Build"), 1), new(L.T("Gear"), 2), new(L.T("Check"), 3),
        new(L.T("Story"), 4)
    ];

    public ObservableCollection<BackstoryRow> Backstory { get; } = [];
    public ObservableCollection<ShoppingRow> Shopping { get; } = [];
    public ObservableCollection<CheckFinding> Findings { get; } = [];
    public ObservableCollection<SkillRow> SkillRows { get; } = [];
    public ObservableCollection<SpecializationRow> Specializations { get; } = [];

    /// <summary>The specialization lines drawn the way the game draws them.</summary>
    public ObservableCollection<SpecializationPanel> TraitGrid { get; } = [];
    public ObservableCollection<LoadoutSlot> Doll { get; } = [];
    public ObservableCollection<LoadoutSlot> Trinkets { get; } = [];
    public ObservableCollection<EquipmentRow> Equipment { get; } = [];
    public ObservableCollection<StoryArc> Story { get; } = [];

    [ObservableProperty] private string? _storyNote;
    [ObservableProperty] private bool _storyLoaded;

    protected override string DefaultEmptyMessage => L.T("Nothing equipped.");

    public void ApplyQuery(IDictionary<string, object> query)
    {
        Select(Tabs[0]);

        if (RouteQuery.Text(query, "name") is not { } name) return;

        CharacterName = name;
        _ = LoadAsync();
    }

    /// <summary>
    /// Shows a skill's own description. Drawbacks live in that prose and nowhere else in the
    /// API, so the honest move is to put the text in front of the player rather than pretend
    /// to have judged it.
    /// </summary>
    [RelayCommand]
    public async Task ExplainAsync(SkillTile? tile)
    {
        if (tile is null || !tile.IsFilled) return;

        await navigator.AlertAsync(
            tile.Name,
            string.IsNullOrWhiteSpace(tile.Description)
                ? L.T("The API returns no description for this skill.")
                : tile.Description,
            L.T("Close"));
    }

    /// <summary>
    /// A trait's own words, for the same reason skills have them: an icon is a reminder
    /// rather than an explanation.
    /// </summary>
    [RelayCommand]
    public async Task ExplainTraitAsync(TraitTile? tile)
    {
        if (tile is null) return;

        await navigator.AlertAsync(
            tile.Name,
            tile.HasDescription
                ? tile.Description!
                : L.T("The API returns no description for this trait."),
            L.T("Close"));
    }

    /// <summary>Switches which of the five views is on screen.</summary>
    public void Select(PageTab tab)
    {
        SelectedTab = tab.Index;

        foreach (var candidate in Tabs)
            candidate.IsSelected = candidate.Index == tab.Index;

        // The story is three requests of its own and most visits never open it, so it is
        // fetched when it is first asked for rather than with the character.
        if (IsStory && !StoryLoaded)
            _ = LoadStoryAsync();
    }

    private Task LoadStoryAsync() => RunAsync(async ct =>
    {
        try
        {
            var journal = await stories.GetAsync(CharacterName, ct);

            Story.Clear();
            foreach (var arc in journal.Arcs)
                Story.Add(arc);

            StoryNote = journal.Note;
            StoryLoaded = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not Gw2ApiException)
        {
            // API failures fall through to RunAsync, which words them properly. This is for
            // everything else: the load runs detached from the tap that started it, so an
            // unexpected fault would otherwise vanish with the discarded task and leave the
            // tab blank with no explanation.
            StoryNote = L.F("Could not read this character's story — {0}.", ex.GetType().Name);
        }
    });

    /// <summary>The character's own account of a step, which is the whole point of the tab.</summary>
    [RelayCommand]
    public async Task RecountAsync(StoryStep? step)
    {
        if (step is null || !step.HasAccount) return;

        await navigator.AlertAsync(step.Name, step.Account, L.T("Close"));
    }

    /// <summary>The longer version of a backstory answer, for the same reason skills have one.</summary>
    [RelayCommand]
    public async Task TellAsync(BackstoryRow? answer)
    {
        if (answer is null) return;

        await navigator.AlertAsync(
            answer.Title,
            string.IsNullOrWhiteSpace(answer.Description) ? answer.Journal : answer.Description,
            L.T("Close"));
    }

    [RelayCommand]
    public Task LoadAsync() => RunAsync(async ct =>
    {
        Equipment.Clear();
        Doll.Clear();
        Trinkets.Clear();
        LoadoutTabs.Clear();
        PvpGear.Clear();
        Dyes.Clear();
        SkillRows.Clear();
        Findings.Clear();
        Shopping.Clear();
        Backstory.Clear();
        Specializations.Clear();
        TraitGrid.Clear();

        var detail = await characters.GetCharacterAsync(CharacterName, _loadout, ct);
        if (detail is null)
        {
            Notice = L.T("That character is no longer on the account.");
            return;
        }

        Subtitle = detail.Subtitle;
        Profession = detail.Profession;
        EliteName = detail.Build.EliteName;
        Playtime = detail.Playtime;
        Deaths = detail.Deaths;
        Created = detail.Created;
        HeroChallenges = detail.HeroChallenges;
        MasteryNote = detail.MasteryNote;
        Notice = detail.EquipmentNotice;

        BodyImage = detail.BodyImage;

        foreach (var slot in detail.Doll)
            Doll.Add(slot);

        foreach (var slot in detail.Trinkets)
            Trinkets.Add(slot);

        foreach (var answer in detail.Backstory)
            Backstory.Add(answer);

        StatPrefix = detail.StatPrefix;
        ShoppingTotal = detail.Shopping.GrandTotal;
        ShoppingNote = detail.Shopping.Nothing;
        ShoppingCaveat = detail.Shopping.Caveat;

        foreach (var row in detail.Shopping.Rows)
            Shopping.Add(row);

        CheckScore = detail.Check.Score;
        AllClear = detail.Check.AllClear;
        CheckCaveat = detail.Check.Caveat;

        foreach (var finding in detail.Check.Findings)
            Findings.Add(finding);

        BuildNotice = detail.Build.Notice;
        TemplateName = detail.Build.TemplateName;

        foreach (var row in detail.Build.Rows)
            SkillRows.Add(row);

        foreach (var line in detail.Build.Specializations)
            Specializations.Add(line);

        foreach (var panel in detail.Build.TraitGrid)
            TraitGrid.Add(panel);

        foreach (var piece in detail.Equipment)
            Equipment.Add(piece);

        foreach (var option in detail.Loadouts)
            LoadoutTabs.Add(option);

        foreach (var piece in detail.PvpGear)
            PvpGear.Add(piece);

        foreach (var swatch in detail.Dyes)
            Dyes.Add(swatch);

        PvpNotice = detail.PvpNotice;
        OnPropertyChanged(nameof(HasLoadouts));
        OnPropertyChanged(nameof(HasDyes));
    });

    /// <summary>
    /// Shows another equipment template. Reloading is the honest way round: the doll, the
    /// stat prefix and the gear list are all read off the same set of pieces, so half of them
    /// would go stale if only the list were swapped.
    /// </summary>
    [RelayCommand]
    public Task ShowLoadoutAsync(LoadoutOption? option)
    {
        if (option is null || option.IsShown) return Task.CompletedTask;

        _loadout = option.Tab;
        return LoadAsync();
    }
}
