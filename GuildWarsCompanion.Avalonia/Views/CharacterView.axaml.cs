using Avalonia.Controls;
using Avalonia.Interactivity;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Ava.Ui;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace GuildWarsCompanion.Ava.Views;

public partial class CharacterView : PageView
{
    public CharacterView()
    {
        InitializeComponent();

        Strip.Selected += tab => ViewModel.Select(tab);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is CharacterViewModel model) model.PropertyChanged += OnModelChanged;
        };
    }

    /// <summary>
    /// The page is reused from character to character, so the art follows the model: the elite
    /// specialization's painting, or the profession's where there is no elite.
    /// </summary>
    private async void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(CharacterViewModel.EliteName) or nameof(CharacterViewModel.Profession))) return;

        var elite = ViewModel.EliteName;
        var profession = ViewModel.Profession;

        Art = null;

        // The masthead's wash and strokes in the profession's colour, the one on the roster's spine.
        WashTint = profession is null ? null : Paint.BrushOf(GuildWarsCompanion.Theme.ProfessionColor(profession));

        var art = elite is not null
            ? await WikiArt.ForEliteAsync(elite)
            : profession is not null
                ? await WikiArt.ForProfessionAsync(profession)
                : null;

        // A different character may have been opened while the painting was on its way.
        if (ViewModel.EliteName == elite && ViewModel.Profession == profession) Art = art;
    }

    private CharacterViewModel ViewModel => (CharacterViewModel)DataContext!;

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => ViewModel.LoadAsync();

    /// <summary>
    /// The tapped tile is the sender's own DataContext, which is reliable where a walk up through
    /// nested templates is not.
    /// </summary>
    private async void OnSkillClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SkillTile tile)
            await ViewModel.ExplainAsync(tile);
    }

    /// <summary>A trait tile explains itself, the same way a skill tile does.</summary>
    private async void OnTraitClicked(object? sender, RoutedEventArgs e)
    {
        var context = (sender as Control)?.DataContext;

        // The minor belongs to the tier rather than to a tile of its own.
        var tile = context as TraitTile ?? (context as TraitTier)?.Minor;

        if (tile is not null)
            await ViewModel.ExplainTraitAsync(tile);
    }

    private async void OnLoadoutClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is LoadoutOption option)
            await ViewModel.ShowLoadoutAsync(option);
    }

    /// <summary>
    /// A doll tile, a trinket or an equipment row — all three carry an item id, and all three open
    /// the same item screen every other list in the app opens. An empty slot has nothing to show,
    /// so it stays inert.
    /// </summary>
    private async void OnGearClicked(object? sender, RoutedEventArgs e)
    {
        var id = (sender as Control)?.DataContext switch
        {
            LoadoutSlot slot when slot.CanOpen => slot.ItemId,
            EquipmentRow row when row.CanOpen => row.ItemId,
            _ => 0
        };

        if (id > 0)
            await App.Services.GetRequiredService<INavigator>().GoToAsync($"item?id={id}");
    }

    private async void OnStoryStepClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is StoryStep step)
            await ViewModel.RecountAsync(step);
    }

    private async void OnBackstoryClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is BackstoryRow answer)
            await ViewModel.TellAsync(answer);
    }
}
