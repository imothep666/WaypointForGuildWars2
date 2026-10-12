using CommunityToolkit.Mvvm.ComponentModel;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>One view inside an area, as the strip at the top of the page shows it.</summary>
public sealed partial class AreaTab(string label, string route) : ObservableObject
{
    public string Label { get; } = label;

    /// <summary>Absolute, because moving between the views of an area is a switch rather
    /// than a push — a back stack of sibling tabs is nobody's mental model.</summary>
    public string Route { get; } = route;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tint))]
    private bool _isSelected;

    public Color Tint => IsSelected ? Theme.Vellum : Theme.MistColor;
}

/// <summary>
/// Which views each area has.
///
/// Kept in one table rather than repeated on four pages: the strip has to agree with the Shell
/// routes exactly, and two lists of the same thing drift.
/// </summary>
public static class Areas
{
    public static IReadOnlyList<AreaTab> For(string? area) => area switch
    {
        "account" =>
        [
            new(L.T("Wallet"), "//account/wallet"),
            new(L.T("Materials"), "//account/materials"),
            new(L.T("Find"), "//account/find"),
            new(L.T("Standing"), "//account/standing"),
            new(L.T("Masteries"), "//account/masteries")
        ],
        "tradingpost" =>
        [
            new(L.T("Orders"), "//tradingpost/orders"),
            new(L.T("Pickup"), "//tradingpost/delivery"),
            new(L.T("Watch"), "//tradingpost/watch"),
            new(L.T("History"), "//tradingpost/history")
        ],
        _ => []
    };
}
