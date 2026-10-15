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
        OnSectionsChanged();

        await session.EnsureRestoredAsync(ct);

        if (!session.IsAuthenticated)
        {
            Notice = L.T("Add your API key under Settings to see the trading post.");
            return;
        }

        if (!session.HasPermission(Gw2Permissions.TradingPost))
        {
            Notice = L.T("This key has no 'tradingpost' permission. Create a new key that includes it.");
            return;
        }

        Notice = null;
        await LoadAsync(ct);
        OnSectionsChanged();
    });

    protected abstract Task LoadAsync(CancellationToken ct);

    /// <summary>For the figures a screen works out from its sections, to be read again.</summary>
    protected virtual void OnSectionsChanged()
    {
    }
}

public sealed class OrdersViewModel(TradingPostService trading, ApiKeySession session)
    : TradingPostViewModelBase(session)
{
    protected override string DefaultEmptyMessage => L.T("No open orders. Pull down to refresh.");

    /// <summary>Orders the market has moved past, on both sides; null until there are orders.</summary>
    public int? Beaten => Sections.Count == 0 ? null : Sections.Sum(section => section.Count(row => row.IsBeaten));

    public string BeatenLabel => Beaten == 1
        ? L.T("order undercut or outbid")
        : L.T("orders undercut or outbid");

    protected override void OnSectionsChanged()
    {
        OnPropertyChanged(nameof(Beaten));
        OnPropertyChanged(nameof(BeatenLabel));
    }

    protected override async Task LoadAsync(CancellationToken ct)
    {
        foreach (var section in await trading.GetOrdersAsync(ct))
            Sections.Add(section);
    }
}

public sealed class HistoryViewModel(TradingPostService trading, ApiKeySession session)
    : TradingPostViewModelBase(session)
{
    protected override string DefaultEmptyMessage => L.T("No trades in the last 90 days.");

    public OrderSection? Sold => Sections.FirstOrDefault(section => section.Name == "Sold");

    public OrderSection? Bought => Sections.FirstOrDefault(section => section.Name == "Bought");

    protected override void OnSectionsChanged()
    {
        OnPropertyChanged(nameof(Sold));
        OnPropertyChanged(nameof(Bought));
    }

    protected override async Task LoadAsync(CancellationToken ct)
    {
        foreach (var section in await trading.GetHistoryAsync(ct))
            Sections.Add(section);
    }
}
