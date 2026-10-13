using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// The page behind Today's fractals row: the day's dailies with the tiers done, tomorrow's, and the
/// currencies fractals pay in.
/// </summary>
public sealed partial class FractalsViewModel(FractalService fractals, ApiKeySession session, TestEvidence evidence)
    : BaseViewModel
{
    /// <summary>"Fractal level 87 · tier 4", or what stands in for it before the first fractal.</summary>
    [ObservableProperty] private string? _standing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTomorrow))]
    private string? _tomorrow;

    public bool HasTomorrow => Tomorrow is not null;

    public ObservableCollection<FractalTask> Today { get; } = [];

    public ObservableCollection<WalletRow> Wallet { get; } = [];

    public bool HasWallet => Wallet.Count > 0;

    protected override string DefaultEmptyMessage => L.T("Pull down to see today's fractals.");

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async ct =>
    {
        Today.Clear();
        Wallet.Clear();

        await session.EnsureRestoredAsync(ct);

        if (!session.IsAuthenticated)
        {
            Notice = L.T("Add your API key under Settings to see what is left today.");
            return;
        }

        if (!session.HasPermission(Gw2Permissions.Progression))
        {
            Notice = L.T("This key has no 'progression' permission. Create a new key that includes it.");
            return;
        }

        var view = await fractals.GetAsync(withWallet: true, ct);

        Standing = view.Level > 0
            ? L.F("Fractal level {0} · tier {1}", view.Level, view.Tier)
            : L.T("No fractals run yet: the dailies start at fractal level 1.");

        foreach (var task in view.Today) Today.Add(task);
        foreach (var row in view.Wallet) Wallet.Add(row);

        Tomorrow = view.Tomorrow;

        OnPropertyChanged(nameof(HasWallet));

        Notice = view.IsYesterday
            ? L.T("No connection, and what is stored is from before the reset: these are yesterday's fractals.")
            : null;

        if (Today.Count > 0) evidence.Saw("fractals-page");
    });
}
