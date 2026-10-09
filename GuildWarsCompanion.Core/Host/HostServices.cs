namespace GuildWarsCompanion.Host;

/// <summary>
/// The app's services, for the few pieces the operating system creates itself and no container
/// can hand anything to: a WorkManager worker, a home screen widget's broadcast receiver.
///
/// Everything else takes its services through its constructor; this is not a way round that.
/// The front end sets it once its container exists — or sets <see cref="Build"/>, so a process
/// that Android starts cold for a background poll, with no screen ever shown, can still make one.
/// </summary>
public static class HostServices
{
    private static IServiceProvider? _current;
    private static readonly Lock Gate = new();

    /// <summary>How to make the container when nothing has yet, for a front end whose container
    /// is otherwise only built with its first screen.</summary>
    public static Func<IServiceProvider>? Build { get; set; }

    /// <summary>The container, built on first use if need be. Null only when there is neither a
    /// container nor a way to make one.</summary>
    public static IServiceProvider? Current
    {
        get
        {
            if (_current is not null) return _current;

            lock (Gate)
            {
                return _current ??= Build?.Invoke();
            }
        }
        set
        {
            lock (Gate)
            {
                _current = value;
            }
        }
    }

    /// <summary>A service by type, or null — the shape the receivers and workers want.</summary>
    public static T? Get<T>() where T : class => Current?.GetService(typeof(T)) as T;
}
