namespace GuildWarsCompanion.Host;

/// <summary>
/// The UI thread, for work that arrives on another one — a stream reading, a sensor, a timer —
/// and has to touch bound state.
///
/// Named and shaped after MAUI's own MainThread so the calls read the same as before.
/// </summary>
public interface IMainThread
{
    void BeginInvokeOnMainThread(Action action);

    Task InvokeOnMainThreadAsync(Action action);

    Task InvokeOnMainThreadAsync(Func<Task> action);
}
