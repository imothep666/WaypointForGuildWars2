using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Data;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// One item, opened from wherever it was tapped. Takes its id from the route, so every list
/// in the app can hand off to the same screen.
/// </summary>
public sealed partial class ItemViewModel(
    ItemInspector inspector, BridgeClient bridge, ISystemActions system, GoalStore goals, INavigator navigator)
    : BaseViewModel, IQueryTarget
{
    /// <summary>Whether this item is pinned to Today as a goal.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GoalLabel))]
    private bool _isGoal;

    public string GoalLabel => IsGoal ? L.T("UNPIN GOAL") : L.T("PIN AS GOAL");

    /// <summary>How many the goal is for; shown, and changed, only while pinned.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GoalQuantityText))]
    private int _goalQuantity = 1;

    public string GoalQuantityText => $"× {GoalQuantity}";

    [RelayCommand]
    public Task MoreAsync() => SetQuantityAsync(GoalQuantity + 1);

    [RelayCommand]
    public Task FewerAsync() => GoalQuantity > 1 ? SetQuantityAsync(GoalQuantity - 1) : Task.CompletedTask;

    private async Task SetQuantityAsync(int quantity)
    {
        if (!IsGoal) return;

        await goals.SetQuantityAsync(_itemId, quantity);
        GoalQuantity = quantity;
    }

    /// <summary>
    /// The price watch screen with this item already chosen: a goal's shortfall is a list of these
    /// pages, one tap each from the goal's own, and watching one for a better price was a search
    /// for something already on screen.
    ///
    /// Pushed rather than switched to, so back returns to this item; a switch to the Trading Post
    /// dropped the item page, and back from there left the app.
    /// </summary>
    [RelayCommand]
    public Task WatchPriceAsync() =>
        _itemId > 0 ? navigator.GoToAsync($"watch?item={_itemId}") : Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItem))]
    private ItemView? _item;

    public bool HasItem => Item is not null;

    /// <summary>Where the last chat link went: "Sent [&amp;AgEJTQAA] to Desktop".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLinked))]
    private string? _linked;

    public bool HasLinked => !string.IsNullOrEmpty(Linked);

    private int _itemId;

    public int ItemId
    {
        get => _itemId;
        set
        {
            _itemId = value;
            _ = LoadAsync();
        }
    }

    public void ApplyQuery(IDictionary<string, object> query)
    {
        if (RouteQuery.Int(query, "id") is { } id) ItemId = id;
    }

    protected override string DefaultEmptyMessage => L.T("Nothing to show for this item.");

    [RelayCommand]
    public Task LoadAsync() => RunAsync(async ct =>
    {
        Item = await inspector.GetAsync(_itemId, ct);

        if (Item is null)
            Notice = L.T("The API does not know this item.");

        var quantity = await goals.QuantityAsync(_itemId, ct);
        IsGoal = quantity is not null;
        GoalQuantity = quantity ?? 1;
    });

    /// <summary>
    /// Pins the item to Today as something being worked towards, or takes it off. Today then
    /// counts it against everything the account holds, all the way down its recipe.
    /// </summary>
    [RelayCommand]
    public async Task ToggleGoalAsync()
    {
        if (Item is not { } item) return;

        if (IsGoal)
            await goals.UnpinAsync(item.Id);
        else
            await goals.PinAsync(item.Id, item.Name, item.IconUrl);

        IsGoal = !IsGoal;
        GoalQuantity = 1;
    }

    /// <summary>
    /// Puts the item's chat link where the game can paste it: the PC's clipboard when a bridge is
    /// paired, this phone's when none is or none answers.
    ///
    /// The same route an emote tap takes, and for the same reason. The game has no Android
    /// client, so a link on this phone's clipboard is only of use to somebody who shares their
    /// clipboard across devices, which is why this was dropped before there was a bridge. One
    /// gesture still; Ctrl+V in chat is the player's own.
    /// </summary>
    [RelayCommand]
    public async Task CopyChatLinkAsync()
    {
        if (_itemId <= 0) return;

        var link = ChatLink.Item(_itemId);

        if (!await bridge.IsSetUpAsync())
        {
            await system.CopyAsync(link);

            Linked = L.F("Copied {0}", link);
            return;
        }

        // Said before the network is asked, so the button never looks dead while a PC that is
        // asleep runs out its timeout.
        Linked = L.F("Sending {0}…", link);

        var sent = await bridge.SendAsync(link);

        if (sent.IsSent)
        {
            // Named, not "the PC": with several paired, the wrong machine has to be visible.
            Linked = L.F("Sent {0} to {1}", link, sent.Pc);
            return;
        }

        await system.CopyAsync(link);

        Linked = L.F("{0} · {1} · copied here", link, sent.Text);
    }

    /// <summary>
    /// Hands off to the wiki, which is where everything the API cannot answer lives — drop
    /// sources, recipes, what the thing is actually for.
    /// </summary>
    [RelayCommand]
    public async Task OpenWikiAsync()
    {
        if (Item is null) return;

        try
        {
            await system.OpenLinkAsync(Item.WikiUrl);
        }
        catch (Exception)
        {
            // No browser, or the launch was refused. Nothing is broken here; say so quietly.
            ErrorMessage = L.T("Could not open a browser on this device.");
        }
    }
}
