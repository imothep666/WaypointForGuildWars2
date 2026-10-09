using Avalonia.Threading;

using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Bridge;

/// <summary>Avalonia's UI thread, for the updater the bridge shares with the app.</summary>
internal sealed class BridgeMainThread : IMainThread
{
    public void BeginInvokeOnMainThread(Action action) => Dispatcher.UIThread.Post(action);

    public Task InvokeOnMainThreadAsync(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();

    public Task InvokeOnMainThreadAsync(Func<Task> action) => Dispatcher.UIThread.InvokeAsync(action);
}
