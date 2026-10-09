using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Which texts may be typed into the game on the player's behalf, and nothing else. The Blish
/// HUD module does the typing; everything that decides whether it is asked lives here.
/// </summary>
public class EmoteGateTests
{
    [Theory]
    [InlineData("/wave")]
    [InlineData("/WAVE")]
    [InlineData("/dance")]
    public void An_emote_from_the_list_is_an_emote(string text) =>
        Assert.True(Emotes.IsCommand(text));

    [Fact]
    public void An_alias_the_game_accepts_is_an_emote_too()
    {
        var alias = Emotes.All.SelectMany(emote => emote.Aliases).First();

        Assert.True(Emotes.IsCommand(alias));
    }

    [Theory]
    [InlineData("/wave hello")]    // a chat line that starts with an emote
    [InlineData("/gg")]            // forfeits a PvP match
    [InlineData("/wave\n/gg")]     // two lines
    [InlineData("wave")]
    [InlineData("[&AgEJTQAA]")]    // a chat link
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_not(string? text) =>
        Assert.False(Emotes.IsCommand(text));
}

/// <summary>/emote on the wire: the same pairing rules as /paste, and a separate answer for done.</summary>
public class EmoteProtocolTests
{
    private const string Code = "123456";

    private static RequestHead Post(string path) =>
        BridgeProtocol.ParseHead($"POST {path} HTTP/1.1\r\n{BridgeProtocol.CodeHeader}: {Code}\r\nContent-Length: 5")!;

    [Fact]
    public void An_emote_request_is_checked_like_a_paste_and_marked_as_an_emote()
    {
        var result = BridgeProtocol.Check(Post(BridgeProtocol.EmotePath), "/wave", Code);

        Assert.True(result.IsAccepted);
        Assert.True(result.IsEmote);
        Assert.Equal("/wave", result.Text);
    }

    [Fact]
    public void A_paste_is_not_an_emote_even_when_it_looks_like_one()
    {
        Assert.False(BridgeProtocol.Check(Post(BridgeProtocol.PastePath), "/wave", Code).IsEmote);
    }

    [Fact]
    public void An_emote_with_the_wrong_code_is_refused_like_a_paste()
    {
        var head = BridgeProtocol.ParseHead(
            $"POST {BridgeProtocol.EmotePath} HTTP/1.1\r\n{BridgeProtocol.CodeHeader}: 999999")!;

        Assert.Equal(PasteVerdict.WrongCode, BridgeProtocol.Check(head, "/wave", Code).Verdict);
    }

    [Fact]
    public void Done_in_game_and_copied_answer_differently()
    {
        // The phone tells the two apart by status alone.
        Assert.Equal(200, new PasteResult(PasteVerdict.Performed, "/wave", true).Status);
        Assert.Equal(204, new PasteResult(PasteVerdict.Accepted, "/wave", true).Status);
    }

    [Fact]
    public void Blish_hud_gets_the_module_path_quoted()
    {
        Assert.Equal(
            "--module \"C:\\Users\\A B\\GuildWarsCompanion.Blish.bhm\"",
            BlishHud.StartArguments(@"C:\Users\A B\GuildWarsCompanion.Blish.bhm"));

        Assert.Equal(string.Empty, BlishHud.StartArguments(null));
    }
}

/// <summary>The line protocol between the bridge and the module. Both ends compile this file.</summary>
public class ModuleWireTests
{
    [Fact]
    public void Every_line_reads_back_as_what_was_written()
    {
        Assert.Equal(ModuleLineKind.Ok, ModuleWire.Parse(ModuleWire.Ok)!.Kind);

        var hello = ModuleWire.Parse(ModuleWire.Hello("abc123"))!;
        Assert.Equal((ModuleLineKind.Hello, "abc123"), (hello.Kind, hello.Text));

        var emote = ModuleWire.Parse(ModuleWire.Emote(7, "/wave"))!;
        Assert.Equal((ModuleLineKind.Emote, 7, "/wave"), (emote.Kind, emote.Id, emote.Text));

        var done = ModuleWire.Parse(ModuleWire.Done(7))!;
        Assert.Equal((ModuleLineKind.Done, 7), (done.Kind, done.Id));

        var busy = ModuleWire.Parse(ModuleWire.Busy(7, "a text box is open"))!;
        Assert.Equal((ModuleLineKind.Busy, 7, "a text box is open"), (busy.Kind, busy.Id, busy.Text));
    }

    [Fact]
    public void A_reason_can_never_become_a_second_line()
    {
        Assert.DoesNotContain('\n', ModuleWire.Busy(1, "one\ntwo"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("emote /wave")]      // no id
    [InlineData("emote 0 /wave")]    // ids start at one
    [InlineData("emote 7")]          // no command
    [InlineData("done x")]
    [InlineData("ok please")]
    [InlineData("type /wave")]
    [InlineData(null)]
    public void Anything_else_is_not_a_line(string? line) => Assert.Null(ModuleWire.Parse(line));

    [Fact]
    public void An_arrival_names_its_objective_the_way_both_ends_write_it()
    {
        var trail = new GuidedTrail(1015, (1204.5, 402.5, -8812.1), [(0, 0, 0), (1, 0, 1)]);

        // The module sends it, the bridge compares it with its own copy of the trail.
        var line = ModuleWire.Parse(ModuleWire.Arrived(trail))!;

        Assert.Equal(ModuleLineKind.Arrived, line.Kind);
        Assert.Equal("1015 1204.5 -8812.1", line.Text);

        var readBack = GuideWire.Parse(GuideWire.Write(trail), out _)!;

        Assert.Equal(ModuleWire.Arrived(trail), ModuleWire.Arrived(readBack));
        Assert.Null(ModuleWire.Parse("arrived"));
    }

    [Fact]
    public void An_overlong_line_is_refused() =>
        Assert.Null(ModuleWire.Parse("emote 1 /" + new string('a', ModuleWire.MaxLine)));

    [Fact]
    public void The_card_carries_the_port_and_the_token()
    {
        Assert.True(ModuleWire.TryReadCard(ModuleWire.Card(47626, "f00d") + "\r\n", out var port, out var token));
        Assert.Equal((47626, "f00d"), (port, token));

        Assert.False(ModuleWire.TryReadCard("47626", out _, out _));
        Assert.False(ModuleWire.TryReadCard("port f00d", out _, out _));
        Assert.False(ModuleWire.TryReadCard(null, out _, out _));
    }

    [Fact]
    public void The_module_port_sits_after_the_other_three() =>
        Assert.Equal(BridgeProtocol.DefaultPort + 3, ModuleWire.PortFor(BridgeProtocol.DefaultPort));
}

/// <summary>The phone's side: done in game, copied, or an old bridge asked the old way.</summary>
public class EmoteClientTests
{
    private static readonly string Secret = BridgeProtocol.NewPairingSecret();

    private static async Task<(BridgeClient Client, FakeNetwork Network)> Build(int? emoteStatus)
    {
        var desk = new FakePc("DESKID", "Desktop", Secret, "192.168.1.20:47623") { EmoteStatus = emoteStatus };
        var network = new FakeNetwork().With(desk);
        var known = new KnownBridges(new MemoryBridgeStore());
        var journal = new Journal(Path.Combine(Path.GetTempPath(), "gw2-bridge-tests.log"));

        await known.PairAsync(desk.Card);

        return (new BridgeClient(network, new BridgeFinder(network, network, known, journal), journal), network);
    }

    [Fact]
    public async Task Done_in_game_says_so()
    {
        var (client, _) = await Build(emoteStatus: 200);

        var sent = await client.EmoteAsync("/wave");

        Assert.True(sent.IsSent);
        Assert.True(sent.InGame);
        Assert.Equal("done in game on Desktop", sent.Text);
    }

    [Fact]
    public async Task Copied_is_sent_but_not_in_game()
    {
        var (client, _) = await Build(emoteStatus: 204);

        var sent = await client.EmoteAsync("/wave");

        Assert.True(sent.IsSent);
        Assert.False(sent.InGame);
    }

    [Fact]
    public async Task A_bridge_from_before_emotes_is_asked_to_paste_instead()
    {
        var (client, network) = await Build(emoteStatus: null);

        var sent = await client.EmoteAsync("/wave");

        Assert.True(sent.IsSent);
        Assert.False(sent.InGame);
        Assert.Contains(network.Asked, url => url.EndsWith(BridgeProtocol.PastePath));
    }
}
