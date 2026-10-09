using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>The pickup box: coins from sales and items from purchases, waiting in game.</summary>
public sealed partial class DeliveryViewModel(TradingPostService trading, ApiKeySession session)
    : BaseViewModel
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasCoins))] private CoinPurse? _coins;

    public bool HasCoins => Coins is not null;

    public ObservableCollection<OrderRow> Items { get; } = [];

    protected override string DefaultEmptyMessage => "Nothing waiting for collection.";

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async ct =>
    {
        Items.Clear();
        Coins = null;

        await session.EnsureRestoredAsync(ct);

        if (!session.IsAuthenticated)
        {
            Notice = "Add your API key under Settings to see the trading post.";
            return;
        }

        if (!session.HasPermission(Gw2Permissions.TradingPost))
        {
            Notice = "This key has no 'tradingpost' permission. Create a new key that includes it.";
            return;
        }

        Notice = null;

        var view = await trading.GetDeliveryAsync(ct);
        Coins = view.HasCoins ? view.Coins : null;

        foreach (var row in view.Items)
            Items.Add(row);

        // One statement per screen: the coin panel already says coins are waiting, so the
        // empty list must not answer "nothing is waiting" underneath it.
        if (view.Items.Count == 0)
            Notice = view.HasCoins ? "No items waiting — only coins." : "Nothing waiting for collection.";
    });
}
