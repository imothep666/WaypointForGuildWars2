using System.ComponentModel;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class UnlockCategoryView : PageView
{
    private UnlockCategoryViewModel? _viewModel;

    public UnlockCategoryView() => InitializeComponent();

    public override bool CanRefresh => true;

    public override Task RefreshAsync() => _viewModel?.LoadAsync() ?? Task.CompletedTask;

    /// <summary>
    /// The category arrives from the route after the page is made, so the list is dressed when
    /// the load that follows it finishes rather than here. The view model is transient, made for
    /// this page and gone with it, so the subscription needs no undoing.
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;

        _viewModel = DataContext as UnlockCategoryViewModel;

        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UnlockCategoryViewModel.IsBusy)) Dress();
    }

    /// <summary>
    /// Points the list at the right items for the category that opened.
    ///
    /// One list rather than two with IsVisible between them: both would be bound to the same
    /// entries, so opening 643 dyes would build the grid and the name list, and the half nobody
    /// could see would be measured along with the half they could. Dyes are the one category
    /// with a rarity to group by, so only they get headings.
    /// </summary>
    private void Dress()
    {
        if (_viewModel is null) return;

        // The groups are rebuilt by each load, so they are flattened once it has finished; the
        // names are a live collection and can be handed over as they are.
        if (!_viewModel.IsGrid)
            Items.ItemsSource = _viewModel.Entries;
        else if (!_viewModel.IsBusy)
            Items.ItemsSource = _viewModel.Groups.SelectMany(group => new object[] { group }.Concat(group)).ToList();
    }
}
