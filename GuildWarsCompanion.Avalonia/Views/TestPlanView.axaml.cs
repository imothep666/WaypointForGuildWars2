using Avalonia.Controls;
using Avalonia.Interactivity;

using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.ViewModels;

namespace GuildWarsCompanion.Ava.Views;

public partial class TestPlanView : PageView
{
    public TestPlanView() => InitializeComponent();

    private TestPlanViewModel ViewModel => (TestPlanViewModel)DataContext!;

    public override void OnAppearing() => ViewModel.Reload();

    private void OnCheckClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TestRow row) ViewModel.Toggle(row);
    }
}
