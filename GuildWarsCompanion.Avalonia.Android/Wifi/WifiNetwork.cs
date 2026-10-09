using Android.Content;
using Android.Net;

namespace GuildWarsCompanion.Platforms.Android;

/// <summary>
/// The connected Wi-Fi network, as Android reports it coming and going.
///
/// Asked for by transport rather than by "is it the default", because the whole point is the
/// case where it is emphatically not the default: a PC hotspot with no internet, beside mobile
/// data. That is why the request drops the internet capability every request carries unless told
/// otherwise; with it, exactly the network the bridge is on would never be reported.
///
/// A callback rather than <c>ConnectivityManager.GetAllNetworks</c>, which Android deprecated in
/// 12 in favour of this. The three Wi-Fi classes each had their own copy of that lookup; they
/// share this one now.
/// </summary>
internal static class WifiNetwork
{
    /// <summary>
    /// How long the first question waits for Android's first report, when it is also the
    /// question that started listening. Android reports a network already up straight after
    /// registering, but on its own thread.
    /// </summary>
    private static readonly TimeSpan FirstReport = TimeSpan.FromMilliseconds(250);

    private static readonly Lock Gate = new();

    /// <summary>Every Wi-Fi network up, oldest first. Almost always one; briefly two while a
    /// phone roams from one access point's network to the next.</summary>
    private static readonly List<Network> Up = [];

    private static readonly ManualResetEventSlim Reported = new();

    private static Watcher? _watcher;

    /// <summary>
    /// Starts listening, if nothing has yet. Called at start-up, so the first send to the bridge
    /// finds the answer already there.
    /// </summary>
    public static void Listen(Context context)
    {
        lock (Gate)
        {
            if (_watcher is not null) return;

            if (context.GetSystemService(Context.ConnectivityService) is not ConnectivityManager manager)
                return;

            var request = new NetworkRequest.Builder()
                .AddTransportType(TransportType.Wifi)!
                .RemoveCapability(NetCapability.Internet)!
                .Build();

            _watcher = new Watcher();

            manager.RegisterNetworkCallback(request!, _watcher);
        }
    }

    /// <summary>The newest Wi-Fi network that is up, or null.</summary>
    public static Network? Current()
    {
        bool started;

        lock (Gate) started = _watcher is null;

        if (started)
        {
            Listen(global::Android.App.Application.Context);

            // Nothing is reported when there is no Wi-Fi, so this waits the full time then, and
            // only this once.
            Reported.Wait(FirstReport);
        }

        lock (Gate) return Up.Count == 0 ? null : Up[^1];
    }

    private sealed class Watcher : ConnectivityManager.NetworkCallback
    {
        public override void OnAvailable(Network network)
        {
            lock (Gate)
            {
                Up.Remove(network);
                Up.Add(network);
            }

            Reported.Set();
        }

        public override void OnLost(Network network)
        {
            lock (Gate) Up.Remove(network);
        }
    }
}
