using CommunityToolkit.Mvvm.ComponentModel;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// Busy/error plumbing shared by every screen, plus one place that turns an API failure
/// into a sentence a player can act on.
/// </summary>
public abstract partial class BaseViewModel : ObservableObject
{
    private CancellationTokenSource? _inFlight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    private bool _isBusy;

    // A Converter or StringFormat in XAML forces a reflection-based binding, which the
    // trimmer cannot see through. Screens bind plain properties; formatting lives here.
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasError))] private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    private string? _notice;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);

    /// <summary>
    /// What an empty list says. A screen states its situation once: if something is blocking
    /// the data, that reason IS the empty state rather than a second line above it. While the
    /// data is on its way it says so, rather than inviting a pull that is already happening.
    /// </summary>
    public string EmptyMessage => Notice ?? (IsBusy ? "Loading…" : DefaultEmptyMessage);

    protected virtual string DefaultEmptyMessage => "Pull down to refresh.";

    /// <summary>
    /// Runs one unit of work, cancelling whatever the screen was doing before. Errors land
    /// in <see cref="ErrorMessage"/> rather than crashing the page.
    /// </summary>
    protected async Task RunAsync(Func<CancellationToken, Task> work)
    {
        await (_inFlight?.CancelAsync() ?? Task.CompletedTask);
        _inFlight?.Dispose();
        _inFlight = new CancellationTokenSource();
        var ct = _inFlight.Token;

        IsBusy = true;
        // Both messages belong to the operation that set them; a new one starts clean,
        // otherwise a stale "Key accepted" sits next to a fresh failure.
        ErrorMessage = null;
        Notice = null;
        try
        {
            await work(ct);
        }
        catch (Gw2ApiException ex)
        {
            ErrorMessage = Describe(ex);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer refresh.
        }
        catch (Exception ex)
        {
            // Anything else, and this is the important one. A [RelayCommand] that returns a Task
            // becomes an AsyncRelayCommand, and that rethrows a faulted task onto the UI thread -
            // which on Android is the process, not the screen. Without this catch the summary
            // above ("errors land in ErrorMessage rather than crashing the page") was only true
            // for the two kinds named.
            ErrorMessage = ex.Message;
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsBusy = false;
        }
    }

    /// <summary>
    /// Drops whatever the screen has in flight, for a screen that is going away.
    ///
    /// Separate from the cancellation <see cref="RunAsync"/> does for itself, which only fires
    /// when a newer piece of work supersedes an older one. A screen that is closing has no newer
    /// work to do the cancelling, so without this the last load runs to completion and updates a
    /// page nobody is looking at any more.
    /// </summary>
    protected void CancelInFlight() => _inFlight?.Cancel();

    /// <summary>
    /// One sentence a player can act on, for work that runs outside <see cref="RunAsync"/>
    /// and so has to report its own failures.
    /// </summary>
    protected static string Describe(Gw2ApiException ex) => ex.Kind switch
    {
        Gw2ErrorKind.Network => "No connection to the Guild Wars 2 API. Pull to retry.",
        Gw2ErrorKind.InvalidKey => "Your API key was rejected. Add a new one under Settings.",
        Gw2ErrorKind.MissingScope => "Your API key is missing a permission this screen needs.",
        Gw2ErrorKind.RateLimited => "Too many requests. Wait a moment before refreshing again.",
        Gw2ErrorKind.BadRequest => ex.Message,
        Gw2ErrorKind.ServiceUnavailable => "ArenaNet's API is down right now — usually brief, often during a patch.",
        _ => ex.Message
    };
}
