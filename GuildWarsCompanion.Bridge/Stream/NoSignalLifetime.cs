using Microsoft.Extensions.Hosting;

namespace GuildWarsCompanion.Bridge.Stream;

/// <summary>
/// A host lifetime that listens for nothing. The hub's host starts and stops when the bridge says
/// so; process signals are the bridge's to answer, not a hosting detail's. See GameStreamHost.
/// </summary>
internal sealed class NoSignalLifetime : IHostLifetime
{
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
