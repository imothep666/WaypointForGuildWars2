using CommunityToolkit.Mvvm.ComponentModel;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// One heading in a page's own tab strip.
///
/// Shell's bottom tabs belong to a flyout area, and a character opened from the roster is a
/// pushed page rather than an area — so a detail page that needs tabs carries its own.
/// </summary>
public sealed partial class PageTab(string label, int index) : ObservableObject
{
    public string Label { get; } = label;
    public int Index { get; } = index;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tint))]
    private bool _isSelected;

    public Color Tint => IsSelected ? Theme.Vellum : Theme.MistColor;
}
