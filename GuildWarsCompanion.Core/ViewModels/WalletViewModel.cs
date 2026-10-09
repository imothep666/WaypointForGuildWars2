using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

public sealed partial class WalletViewModel(
    AccountDataService data, WorthService worth, ApiKeySession session) : BaseViewModel
{
    private const int CoinCurrencyId = 1;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasWorth))] private CoinPurse? _worthTotal;
    [ObservableProperty] private string? _worthCaveat;
    [ObservableProperty] private bool _isCounting;

    public ObservableCollection<WorthRow> WorthParts { get; } = [];

    public bool HasWorth => WorthTotal is not null;

    /// <summary>
    /// Hidden once the answer is on screen, since the number does not move on its own.
    /// </summary>
    public bool CanCount => WorthTotal is null && !IsCounting;

    // --- the gem exchange, priced for this account's own coin ----------------

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasGems))] private string? _gemBuys;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSellBack))] private string? _gemSellsBack;
    [ObservableProperty] private string? _gemNote;
    [ObservableProperty] private bool _isQuoting;

    public bool HasGems => GemBuys is not null;

    public bool HasSellBack => GemSellsBack is not null;

    public bool CanQuote => GemBuys is null && GemNote is null && !IsQuoting;

    /// <summary>Coin leads the screen on its own; the rest of the wallet lists below it.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasCoin))] private CoinPurse? _coin;

    public bool HasCoin => Coin is not null;

    public ObservableCollection<WalletRow> Currencies { get; } = [];

    protected override string DefaultEmptyMessage => "Pull down to load your currencies.";

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async ct =>
    {
        Currencies.Clear();
        Coin = null;

        // A stored key is only known to be good once the restore has answered.
        await session.EnsureRestoredAsync(ct);

        if (!session.IsAuthenticated)
        {
            Notice = "Add your API key under Settings to see your wallet.";
            return;
        }

        if (!session.HasPermission(Gw2Permissions.Wallet))
        {
            Notice = "This key has no 'wallet' permission. Create a new key that includes it.";
            return;
        }

        Notice = null;

        var wallet = await data.GetWalletAsync(ct);

        foreach (var row in wallet.Value)
        {
            if (row.CurrencyId == CoinCurrencyId)
                Coin = new CoinPurse(row.RawValue);
            else
                Currencies.Add(row);
        }

        // Said once, at the top: these numbers are from the last time there was a signal.
        if (wallet.FetchedAt is { } taken)
            Notice = Stale.Describe("your wallet", taken, DateTimeOffset.UtcNow);
    });

    /// <summary>
    /// Asks the exchange what this account's coin is worth in gems, and what it would be
    /// worth on the way back. Two public requests, so no key is needed for it.
    /// </summary>
    [RelayCommand]
    public async Task QuoteGemsAsync()
    {
        if (IsQuoting) return;

        IsQuoting = true;
        OnPropertyChanged(nameof(CanQuote));

        try
        {
            var view = await worth.GetGemsAsync();

            GemBuys = view.HasQuote ? view.Buys : null;
            GemSellsBack = view.SellsBack;
            GemNote = view.Note;
        }
        catch (Gw2ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsQuoting = false;
            OnPropertyChanged(nameof(CanQuote));
        }
    }

    /// <summary>
    /// Adds the whole account up.
    ///
    /// Asked for rather than done on arrival: reading every character's bags is the expensive
    /// half, and a wallet that took ten seconds to show coin would be a worse wallet. Once the
    /// storage screen has been open it is nearly free, and this is quick either way.
    /// </summary>
    [RelayCommand]
    public async Task CountAsync()
    {
        if (IsCounting) return;

        IsCounting = true;
        OnPropertyChanged(nameof(CanCount));

        try
        {
            var view = await worth.GetAsync();

            WorthParts.Clear();
            foreach (var part in view.Parts)
                WorthParts.Add(part);

            WorthTotal = view.HasTotal ? view.Total : null;
            WorthCaveat = view.Caveat;

            if (!view.HasTotal)
                ErrorMessage = "Nothing added up — the key may be missing a permission.";
        }
        catch (Gw2ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsCounting = false;
            OnPropertyChanged(nameof(CanCount));
        }
    }
}
