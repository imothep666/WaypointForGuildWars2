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
            new("Wallet", "//account/wallet"),
            new("Materials", "//account/materials"),
            new("Find", "//account/find"),
            new("Standing", "//account/standing"),
            new("Masteries", "//account/masteries")
        ],
        "tradingpost" =>
        [
            new("Orders", "//tradingpost/orders"),
            new("Pickup", "//tradingpost/delivery"),
            new("Watch", "//tradingpost/watch"),
            new("History", "//tradingpost/history")
        ],
        _ => []
    };
}
