using GuildWarsCompanion.Bridge.Protocol;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The bridge's wire contract.
///
/// This is the one place in the project that listens on a network, so the tests lean on the
/// refusals rather than the happy path: a bridge that accepts something it should not is a
/// program on the user's LAN writing into their clipboard.
/// </summary>
public class BridgeProtocolTests
{
    private const string Code = "H9HP-ZXJ6";

    private static RequestHead Post(string path = "/paste", string? code = Code, int length = 6)
    {
        var raw = $"POST {path} HTTP/1.1\r\nHost: pc:47623\r\nContent-Length: {length}\r\n" +
                  (code is null ? string.Empty : $"X-Bridge-Code: {code}\r\n");

        return BridgeProtocol.ParseHead(raw)!;
    }

    // --- parsing -----------------------------------------------------------

    [Fact]
    public void The_request_line_and_headers_are_read()
    {
        var head = Post();

        Assert.Equal("POST", head.Method);
        Assert.Equal("/paste", head.Path);
        Assert.Equal(6, head.ContentLength);
        Assert.Equal(Code, head.Header("X-Bridge-Code"));
    }

    [Fact]
    public void Header_names_are_matched_without_regard_to_case()
    {
        // HTTP says they are case-insensitive, and a hand-written client will spell them
        // however it likes.
        var head = BridgeProtocol.ParseHead("POST /paste HTTP/1.1\r\nX-BRIDGE-CODE: abc\r\n")!;

        Assert.Equal("abc", head.Header("x-bridge-code"));
        Assert.Equal("abc", head.Header("X-Bridge-Code"));
    }

    [Fact]
    public void Nonsense_parses_to_nothing_rather_than_throwing()
    {
        Assert.Null(BridgeProtocol.ParseHead(string.Empty));
        Assert.Null(BridgeProtocol.ParseHead("hello"));
    }

    [Fact]
    public void A_missing_content_length_is_no_body_rather_than_an_error()
    {
        Assert.Equal(0, BridgeProtocol.ParseHead("POST /paste HTTP/1.1\r\n")!.ContentLength);
        Assert.Equal(0, BridgeProtocol.ParseHead("POST /paste HTTP/1.1\r\nContent-Length: no\r\n")!.ContentLength);
    }

    // --- what gets through -------------------------------------------------

    [Fact]
    public void A_correct_request_reaches_the_clipboard()
    {
        var result = BridgeProtocol.Check(Post(), "/dance", Code);

        Assert.Equal(PasteVerdict.Accepted, result.Verdict);
        Assert.Equal("/dance", result.Text);
        Assert.Equal(204, result.Status);
    }

    [Fact]
    public void The_code_is_forgiving_about_case_and_dashes()
    {
        // It is read off a screen and typed on a phone. Rejecting "h9hpzxj6" would be correct
        // and useless.
        Assert.True(BridgeProtocol.CodesMatch("h9hpzxj6", Code));
        Assert.True(BridgeProtocol.CodesMatch("H9HP-ZXJ6", Code));
        Assert.True(BridgeProtocol.CodesMatch(" h9hp zxj6 ", Code));
    }

    [Fact]
    public void A_wrong_code_is_refused_and_nothing_is_carried_with_it()
    {
        var result = BridgeProtocol.Check(Post(code: "AAAA-BBBB"), "/dance", Code);

        Assert.Equal(PasteVerdict.WrongCode, result.Verdict);
        Assert.Equal(403, result.Status);

        // The refused text must not travel any further, including into the window's log.
        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public void A_missing_code_is_refused_like_a_wrong_one()
    {
        Assert.Equal(PasteVerdict.WrongCode, BridgeProtocol.Check(Post(code: null), "/dance", Code).Verdict);
    }

    [Fact]
    public void A_bridge_with_no_code_of_its_own_is_closed_not_open()
    {
        // The dangerous default. An unpaired bridge that waved everything through would be a
        // clipboard open to the whole network.
        foreach (var nothing in new[] { null, "", "   " })
            Assert.Equal(PasteVerdict.NotPaired, BridgeProtocol.Check(Post(), "/dance", nothing).Verdict);
    }

    [Fact]
    public void Only_a_post_to_the_paste_path_is_a_paste()
    {
        var get = BridgeProtocol.ParseHead("GET /paste HTTP/1.1\r\n")!;
        var elsewhere = BridgeProtocol.ParseHead("POST /admin HTTP/1.1\r\n")!;

        Assert.Equal(PasteVerdict.NotUnderstood, BridgeProtocol.Check(get, "x", Code).Verdict);
        Assert.Equal(PasteVerdict.NotUnderstood, BridgeProtocol.Check(elsewhere, "x", Code).Verdict);
        Assert.Equal(PasteVerdict.NotUnderstood, BridgeProtocol.Check(null, "x", Code).Verdict);
    }

    [Fact]
    public void The_path_ignores_a_query_string_and_a_trailing_slash()
    {
        Assert.True(BridgeProtocol.PathIs("/paste", "/paste"));
        Assert.True(BridgeProtocol.PathIs("/paste/", "/paste"));
        Assert.True(BridgeProtocol.PathIs("/paste?v=1", "/paste"));
        Assert.False(BridgeProtocol.PathIs("/pasteboard", "/paste"));
    }

    [Fact]
    public void Nothing_and_whitespace_are_not_worth_pasting()
    {
        Assert.Equal(PasteVerdict.Empty, BridgeProtocol.Check(Post(), string.Empty, Code).Verdict);
        Assert.Equal(PasteVerdict.Empty, BridgeProtocol.Check(Post(), "   \r\n ", Code).Verdict);
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_off_what_is_kept()
    {
        // A trailing newline from the sender would otherwise become a stray return in chat.
        Assert.Equal("/dance", BridgeProtocol.Check(Post(), "  /dance\n", Code).Text);
    }

    [Fact]
    public void An_oversized_body_is_refused_whole()
    {
        var huge = new string('x', BridgeProtocol.MaxTextLength + 1);
        var result = BridgeProtocol.Check(Post(), huge, Code);

        Assert.Equal(PasteVerdict.TooLong, result.Verdict);
        Assert.Equal(413, result.Status);
        Assert.Equal(string.Empty, result.Text);
    }

    // --- the code itself ---------------------------------------------------

    [Fact]
    public void A_pairing_code_is_readable_and_not_guessable()
    {
        var code = BridgeProtocol.NewPairingCode();

        Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{4}$", code);

        // No I, L, O or U: nothing in a printed code can be read as something else.
        Assert.DoesNotContain(code, c => c is 'I' or 'L' or 'O' or 'U');

        // Not a constant, and not drawn from a handful of values.
        var many = Enumerable.Range(0, 200).Select(_ => BridgeProtocol.NewPairingCode()).ToHashSet();
        Assert.True(many.Count > 190, $"only {many.Count} distinct codes in 200 draws");
    }

    [Fact]
    public void Nothing_matches_an_absent_expectation()
    {
        Assert.False(BridgeProtocol.CodesMatch("anything", null));
        Assert.False(BridgeProtocol.CodesMatch(null, null));
        Assert.False(BridgeProtocol.CodesMatch(null, Code));
    }

    [Fact]
    public void A_clipboard_that_refused_is_not_a_paste_that_was_accepted()
    {
        // The verdict the handler reaches after the check has already passed. It has to carry a
        // status of its own, because 204 to the phone means "it is on your PC's clipboard now"
        // and that is exactly what did not happen.
        var refused = new PasteResult(PasteVerdict.ClipboardRefused, "/dance");

        Assert.Equal(500, refused.Status);
        Assert.False(refused.IsAccepted);
        Assert.NotEqual(new PasteResult(PasteVerdict.Accepted, "/dance").Status, refused.Status);
    }

    [Fact]
    public void The_two_failures_of_this_PC_do_not_share_a_status()
    {
        // Both are the PC's fault rather than the phone's, and the phone still has to tell them
        // apart: one is fixed by pairing, the other by finding what is holding the clipboard.
        // Reusing 503 for the second would have collapsed them into one message.
        Assert.Equal(503, new PasteResult(PasteVerdict.NotPaired, string.Empty).Status);
        Assert.Equal(500, new PasteResult(PasteVerdict.ClipboardRefused, string.Empty).Status);
    }

    [Fact]
    public void Every_verdict_says_something_a_person_could_act_on()
    {
        // A new verdict with no sentence of its own falls through to "Not a paste request",
        // which would be a lie about the one case that is not a bad request at all.
        foreach (var verdict in Enum.GetValues<PasteVerdict>())
        {
            var reason = new PasteResult(verdict, "/dance").Reason;

            Assert.False(string.IsNullOrWhiteSpace(reason));

            if (verdict != PasteVerdict.NotUnderstood)
                Assert.NotEqual("Not a paste request", reason);
        }
    }
}
