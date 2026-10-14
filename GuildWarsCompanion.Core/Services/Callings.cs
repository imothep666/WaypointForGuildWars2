using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Services;

/// <summary>What a character is playing as: "Chronomancer", or "Mesmer" without an elite, and the
/// key its emblem is kept under.</summary>
public sealed record Calling(string Name, string Emblem);

/// <summary>
/// The profession and elite specialization MumbleLink reports, put into words and an emblem for
/// the Here widget.
///
/// MumbleLink gives only numbers: a profession from 1 to 9 and the specialization in the third
/// slot, which is the elite when one is equipped and a core line otherwise. The names come from
/// the API in the phone's language, and the emblem is the white one the game shows over a
/// character's head, kept as a file because the widget is drawn by Android from files. Both are
/// static between patches, so each is asked for once per process and the emblem once per install.
/// </summary>
public sealed class Callings(Gw2Client client, AppEnvironment environment)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>MumbleLink's profession numbers, as the API spells them.</summary>
    private static readonly string[] Professions =
        ["Guardian", "Warrior", "Engineer", "Ranger", "Thief", "Elementalist", "Mesmer", "Necromancer", "Revenant"];

    private readonly Dictionary<(int, int, string), Calling> _known = [];

    /// <summary>The API's id for a MumbleLink profession number, or null for one it does not know.</summary>
    public static string? ProfessionFor(int mumbleId) =>
        mumbleId >= 1 && mumbleId <= Professions.Length ? Professions[mumbleId - 1] : null;

    /// <summary>A calling already worked out in this process, without the network.</summary>
    public Calling? Known(int profession, int specialization)
    {
        lock (_known) return _known.GetValueOrDefault((profession, specialization, client.Language));
    }

    /// <summary>
    /// The calling, asked of the API when not known yet, with its emblem fetched. Null when the
    /// profession is not one MumbleLink numbers or the API cannot be reached; the widget then
    /// shows the name alone, as it did before.
    /// </summary>
    public async Task<Calling?> ResolveAsync(int profession, int specialization, CancellationToken ct = default)
    {
        if (Known(profession, specialization) is { } known) return known;
        if (ProfessionFor(profession) is not { } id) return null;

        try
        {
            var elite = specialization > 0
                ? (await client.GetSpecializationsAsync([specialization], ct)).FirstOrDefault(spec => spec.Elite)
                : null;

            Calling calling;
            string? icon;

            if (elite is not null)
            {
                calling = new Calling(elite.Name, $"spec-{elite.Id}");
                icon = elite.ProfessionIconBig;
            }
            else
            {
                if (await client.GetProfessionAsync(id, ct) is not { Name.Length: > 0 } core) return null;

                calling = new Calling(core.Name!, $"prof-{core.Id}");
                icon = core.IconBig;
            }

            if (icon is not null) await KeepEmblemAsync(calling.Emblem, icon, ct);

            lock (_known) _known[(profession, specialization, client.Language)] = calling;

            return calling;
        }
        catch (Exception ex) when (ex is Gw2ApiException or HttpRequestException or TaskCanceledException &&
                                   !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Where an emblem is kept, whether or not it is there yet.</summary>
    public string FileFor(string emblem) =>
        Path.Combine(environment.CacheDirectory, "emblems", string.Concat(emblem.Split(Path.GetInvalidFileNameChars())) + ".png");

    /// <summary>The kept emblem, or null without touching the network.</summary>
    public string? Held(string? emblem) =>
        emblem is { Length: > 0 } && File.Exists(FileFor(emblem)) ? FileFor(emblem) : null;

    private async Task KeepEmblemAsync(string emblem, string url, CancellationToken ct)
    {
        if (Held(emblem) is not null) return;

        try
        {
            var bytes = await Http.GetByteArrayAsync(url, ct);
            var file = FileFor(emblem);

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);

            // Written beside and moved, as MapArt does, so a widget never draws half an emblem.
            var part = file + ".part";
            await File.WriteAllBytesAsync(part, bytes, ct);
            File.Move(part, file, overwrite: true);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Decoration: the name still shows, and the next reading asks again.
        }
    }
}
