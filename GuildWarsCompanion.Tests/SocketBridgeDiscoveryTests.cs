using System.Net;
using System.Text;

using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Where the portable discovery shouts, and what it accepts as an answer. The sockets themselves
/// are tried against a real bridge, not here: the discovery port is the running bridge's own.
/// </summary>
public class SocketBridgeDiscoveryTests
{
    private static IPAddress Ip(string text) => IPAddress.Parse(text);

    [Fact]
    public void The_gateway_comes_first_then_both_broadcasts()
    {
        var targets = SocketBridgeDiscovery.Targets(
            Ip("192.168.137.1"), Ip("192.168.137.191"), Ip("255.255.255.0")).ToList();

        Assert.Equal([Ip("192.168.137.1"), IPAddress.Broadcast, Ip("192.168.137.255")], targets);
    }

    [Fact]
    public void The_subnet_broadcast_follows_the_mask_not_a_guessed_slash_24()
    {
        // Hyper-V's Default Switch hands out a /20: a ".255" guess would be a host on it, not the
        // broadcast.
        var targets = SocketBridgeDiscovery.Targets(
            Ip("172.23.96.1"), Ip("172.23.105.151"), Ip("255.255.240.0")).ToList();

        Assert.Equal(Ip("172.23.111.255"), targets[^1]);
    }

    [Fact]
    public void No_gateway_still_leaves_the_broadcasts()
    {
        var targets = SocketBridgeDiscovery.Targets(null, Ip("10.0.0.5"), Ip("255.0.0.0")).ToList();

        Assert.Equal([IPAddress.Broadcast, Ip("10.255.255.255")], targets);
    }

    [Fact]
    public void A_bridge_reply_is_read_and_anything_else_is_ignored()
    {
        var reply = Encoding.UTF8.GetBytes("""{"id":"abc","name":"GKS-0432","port":47623}""");

        Assert.Equal("abc", SocketBridgeDiscovery.Read(reply)?.Id);
        Assert.Equal(47623, SocketBridgeDiscovery.Read(reply)?.Port);
        Assert.Null(SocketBridgeDiscovery.Read(Encoding.UTF8.GetBytes("gw2bridge?")));
    }
}
