namespace GuildWarsCompanion.Gw2;

/// <summary>Shape check for an API key, used to reject QR codes that hold something else.</summary>
public static class Gw2ApiKey
{
    /// <summary>
    /// Keys are long runs of upper-case hex in dash-separated blocks. This only rules out
    /// obvious non-keys — a wifi QR, a URL, someone's vCard — so the user gets "that is not
    /// an API key" instead of a puzzling 401. The API remains the real judge.
    /// </summary>
    public static bool LooksValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var trimmed = value.Trim();
        return trimmed.Length >= 40
               && trimmed.Contains('-')
               && trimmed.All(c => Uri.IsHexDigit(c) || c == '-');
    }
}
