namespace GuildWarsCompanion.Services;

/// <summary>
/// World bosses and raid encounters as the game names them in the phone's language.
///
/// The API names neither: /v2/worldbosses and /v2/raids give ids, and <see cref="EventSchedule"/>
/// writes the bosses out in English. The German names are filed in Strings.de.resx under
/// "boss|…" and "raid|…" and were taken from the game where it has them (achievement and mini
/// names asked of the API in German: "Schlachtzug-Mentor: Tal-Wächter", "Mini Faultierion") and from
/// the German wiki's raid and world boss pages for the rest. Separate from EventSchedule on
/// purpose: that file is compiled into the PC Bridge too, which has no L.
/// </summary>
public static class GameNames
{
    /// <summary>A world boss by the English name <see cref="EventSchedule"/> gives it.</summary>
    public static string Boss(string english) => L.TIn("boss", english);

    /// <summary>A world boss by its /v2/worldbosses id, which the schedule maps to its name. The two
    /// it does not time keep the id's own words.</summary>
    public static string BossById(string apiId) =>
        Boss(EnglishBosses.Value.GetValueOrDefault(apiId) ?? Ids.ToDisplayName(apiId));

    private static readonly Lazy<IReadOnlyDictionary<string, string>> EnglishBosses = new(() =>
        EventSchedule.NextAppearances(DateTimeOffset.UnixEpoch)
            .ToDictionary(boss => boss.ApiId, boss => boss.Name));

    /// <summary>
    /// A raid encounter by its /v2/raids event id. The ids are not always the name: "escort" is
    /// "Siege the Stronghold" in the game, and "voice_in_the_void" is Dhuum.
    /// </summary>
    public static string Raid(string apiId) =>
        L.TIn("raid", Raids.GetValueOrDefault(apiId) ?? Ids.ToDisplayName(apiId));

    /// <summary>The encounters by their short names, as the game's raid mentor achievements use them.</summary>
    public static IReadOnlyDictionary<string, string> Raids { get; } = new Dictionary<string, string>
    {
        ["vale_guardian"] = "Vale Guardian",
        ["gorseval"] = "Gorseval",
        ["sabetha"] = "Sabetha",
        ["slothasor"] = "Slothasor",
        ["bandit_trio"] = "Bandit Trio",
        ["matthias"] = "Matthias",
        ["escort"] = "Siege the Stronghold",
        ["keep_construct"] = "Keep Construct",
        ["xera"] = "Xera",
        ["cairn"] = "Cairn",
        ["mursaat_overseer"] = "Mursaat Overseer",
        ["samarog"] = "Samarog",
        ["deimos"] = "Deimos",
        ["soulless_horror"] = "Soulless Horror",
        ["river_of_souls"] = "River of Souls",
        ["statues_of_grenth"] = "Statues of Grenth",
        ["voice_in_the_void"] = "Dhuum",
        ["conjured_amalgamate"] = "Conjured Amalgamate",
        ["twin_largos"] = "Twin Largos",
        ["qadim"] = "Qadim",
        ["adina"] = "Adina",
        ["sabir"] = "Sabir",
        ["qadim_the_peerless"] = "Qadim the Peerless",
        ["greer"] = "Greer",
        ["decima"] = "Decima",
        ["ura"] = "Ura"
    };
}
