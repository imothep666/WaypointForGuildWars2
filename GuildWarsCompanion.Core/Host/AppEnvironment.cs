namespace GuildWarsCompanion.Host;

/// <summary>
/// Where the app keeps its files, and which build it is.
/// </summary>
/// <param name="DataDirectory">Private storage that survives until the app is removed: the
/// databases, the log, imported marker packs.</param>
/// <param name="CacheDirectory">Storage the system may clear: map tiles, icons.</param>
/// <param name="Version">The installed version, as Directory.Build.targets composes it:
/// 1.0.42+ee30fd6.</param>
public sealed record AppEnvironment(string DataDirectory, string CacheDirectory, string Version);
