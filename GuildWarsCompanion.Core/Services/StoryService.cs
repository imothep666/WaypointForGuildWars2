using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>One step this character completed, as they would tell it.</summary>
public sealed record StoryStep(string Name, string LevelText, string Account)
{
    public bool HasAccount => !string.IsNullOrWhiteSpace(Account);
}

/// <summary>
/// One arc, with the steps this character actually went through.
///
/// Derives from List so the grouped layout can bind straight to it, the same way material
/// storage and the vault do.
/// </summary>
public sealed class StoryArc(string name, string summary, IEnumerable<StoryStep> steps)
    : List<StoryStep>(steps)
{
    public string Name { get; } = name;
    public string Summary { get; } = summary;
}

/// <summary>The character's story as the API can tell it, plus what it cannot.</summary>
public sealed record StoryJournal(IReadOnlyList<StoryArc> Arcs, string Note, bool HasAny);

/// <summary>
/// A character's personal story: which steps they went through, in order, each with the
/// account the game wrote for them.
///
/// There is deliberately no completion percentage. A story's quest list covers every branch —
/// the human personal story has 44 quests and no character completes more than a fraction —
/// so a denominator would be nonsense. What the character completed <em>is</em> the record of
/// the decisions they took: the branch they walked is the branch that appears.
/// </summary>
public sealed class StoryService(Gw2Client client, OfflineReader offline)
{
    private List<Story>? _stories;
    private Dictionary<string, StorySeason>? _seasons;

    public async Task<StoryJournal> GetAsync(string character, CancellationToken ct = default)
    {
        var completed = await client.GetCharacterQuestsAsync(character, ct);

        if (completed.Count == 0)
            return new StoryJournal([], NothingNote, false);

        // Only the steps this character took, so one request covers a full playthrough.
        var quests = await client.GetQuestsAsync(completed.Distinct(), ct);

        await EnsureReferenceAsync(ct);
        var stories = _stories!.ToDictionary(s => s.Id);

        var arcs = quests
            .GroupBy(q => q.Story)
            .Select(group => (story: stories.GetValueOrDefault(group.Key), steps: group))
            .Where(x => x.story is not null)
            .OrderBy(x => SeasonOrder(x.story!.Season))
            .ThenBy(x => x.story!.Order)
            .Select(x => new StoryArc(
                Title(x.story!),
                Summarise(x.story!, x.steps.Count()),
                x.steps
                    .OrderBy(q => q.Level)
                    .ThenBy(q => q.Id)
                    .Select(Describe)))
            .ToList();

        return new StoryJournal(arcs, KnownGapsNote, arcs.Count > 0);
    }

    private static StoryStep Describe(Quest quest) =>
        new(quest.Name,
            quest.Level > 0 ? $"level {quest.Level}" : string.Empty,
            // A step can have several goals; the accounts read as consecutive sentences.
            Prose.Clean(string.Join(" ",
                (quest.Goals ?? [])
                    .Select(g => g.Complete)
                    .Where(text => !string.IsNullOrWhiteSpace(text)))) ?? string.Empty);

    /// <summary>"My Story · 1325 AE" — the season name carries more than the arc name alone.</summary>
    private string Title(Story story)
    {
        var season = story.Season is null ? null : _seasons!.GetValueOrDefault(story.Season)?.Name;

        // Every racial personal story is just called "My Story", so the season name alone
        // would be no help; the arc name alone would not say which chapter of the game.
        return season is null || season == story.Name ? story.Name : $"{season} · {story.Name}";
    }

    private static string Summarise(Story story, int steps)
    {
        var count = $"{steps} {(steps == 1 ? "step" : "steps")}";

        return string.IsNullOrWhiteSpace(story.Timeline) ? count : $"{story.Timeline} · {count}";
    }

    private int SeasonOrder(string? season) =>
        season is not null && _seasons!.TryGetValue(season, out var found) ? found.Order : int.MaxValue;

    /// <summary>
    /// Story and season names never change between builds, so they are cached and answer
    /// offline once seen.
    /// </summary>
    private async Task EnsureReferenceAsync(CancellationToken ct)
    {
        _stories ??= (await offline.ReadAsync(
            "stories", Gw2JsonContext.Default.ListStory, client.GetStoriesAsync, ct)).Value;

        _seasons ??= (await offline.ReadAsync(
                "stories/seasons", Gw2JsonContext.Default.ListStorySeason,
                client.GetStorySeasonsAsync, ct))
            .Value.ToDictionary(season => season.Id);
    }

    private const string NothingNote =
        "The API reports no completed steps for this character. It is also known to miss " +
        "some — see below.";

    private const string KnownGapsNote =
        "What the API reports. It is known to omit some Heart of Thorns and Icebrood Saga " +
        "steps even when they are finished, and replaying a chapter can disturb what follows " +
        "it — so a gap here is not proof you missed something.";
}
