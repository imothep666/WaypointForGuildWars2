using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// Shared behaviour for the three trading post screens: same permission gate, same shape of
/// data, different question asked of the API.
/// </summary>
public abstract partial class TradingPostViewModelBase(ApiKeySession session) : BaseViewModel
{
    public ObservableCollection<OrderSection> Sections { get; } = [];

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async ct =>
    {
        Sections.Clear();

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
        await LoadAsync(ct);
    });

    protected abstract Task LoadAsync(CancellationToken ct);
}

public sealed class OrdersViewModel(TradingPostService trading, ApiKeySession session)
    : TradingPostViewModelBase(session)
{
    protected override string DefaultEmptyMessage => "No open orders. Pull down to refresh.";

    protected override async Task LoadAsync(CancellationToken ct)
    {
        foreach (var section in await trading.GetOrdersAsync(ct))
            Sections.Add(section);
    }
}

public sealed class HistoryViewModel(TradingPostService trading, ApiKeySession session)
    : TradingPostViewModelBase(session)
{
    protected override string DefaultEmptyMessage => "No trades in the last 90 days.";

    protected override async Task LoadAsync(CancellationToken ct)
    {
        foreach (var section in await trading.GetHistoryAsync(ct))
            Sections.Add(section);
    }
}
