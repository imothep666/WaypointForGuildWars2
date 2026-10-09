using System.Runtime.CompilerServices;

using GuildWarsCompanion.Bridge.Protocol;

using Microsoft.AspNetCore.SignalR;

namespace GuildWarsCompanion.Bridge.Stream;

/// <summary>
/// What the game is doing, pushed frame by frame for as long as a phone is listening.
///
/// Deliberately outside <c>Protocol/</c>. Everything in that folder is compiled into the phone
/// app and the test project by <c>&lt;Compile Include&gt;</c>, and neither of those can take an
/// ASP.NET Core dependency. What crosses the wire — <see cref="GameReport"/> — does live there,
/// so both ends still agree about the shape from one file.
///
/// The hub knows nothing about objectives, bearings or the compass. It reports position and
/// facing; the phone holds the API key, resolves where the objective is and works out the angle.
/// That keeps the rule the rest of the bridge follows: this machine does not know what Guild
/// Wars 2 is.
/// </summary>
public sealed class GameHub(GameStreamHost host) : Hub
{
    /// <summary>
    /// Refuses anything not carrying the pairing code.
    ///
    /// The same credential and the same comparison as <c>/game</c>, for the same reason: this
    /// says which character is logged in and where they are standing. The header rides along on
    /// the negotiate request and on every transport that follows, so it is readable here
    /// whichever one SignalR settles on.
    ///
    /// Aborting rather than throwing. A wrong credential should see a closed connection, not an
    /// error it could learn something from.
    /// </summary>
    public override Task OnConnectedAsync()
    {
        var offered = Context.GetHttpContext()?.Request.Headers[BridgeProtocol.CodeHeader].ToString();

        var config = host.Config();

        if (!BridgeProtocol.Accepts(offered, config.PairingCode, config.Secret))
        {
            Context.Abort();

            return Task.CompletedTask;
        }

        host.Joined();

        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        host.Left();

        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// One frame per tick until the phone stops listening.
    ///
    /// Reads the readout the window already polls rather than MumbleLink directly. One reader
    /// means the window and the phone can never disagree about which character is logged in,
    /// and it means the liveness tracking in <see cref="GameWatch"/> has one owner. The poll is
    /// sped up while anyone is streaming; see <see cref="GameStreamHost.Listeners"/>.
    ///
    /// Frames are sent even when the game is closed. A compass that goes quiet is
    /// indistinguishable from a compass whose connection died, and the phone has to be able to
    /// say "Guild Wars 2 is not running" rather than just stop moving.
    /// </summary>
    public async IAsyncEnumerable<GameReport> Stream([EnumeratorCancellation] CancellationToken ct)
    {
        using var timer = new PeriodicTimer(GameStreamHost.FrameInterval);

        // Immediately, rather than after the first interval: the screen opening should draw
        // something now, not in a tenth of a second.
        yield return host.Report();

        while (await timer.WaitForNextTickAsync(ct)) yield return host.Report();
    }
}
