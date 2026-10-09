namespace GuildWarsCompanion.Services;

/// <summary>
/// One thing on the dial: which way it is, how far, and what kind of thing.
///
/// A struct rather than a class, and measured rather than stylistic. Up to thirteen of these are
/// built on every frame and thrown away again; as a record class that was thirteen heap objects
/// thirty times a second, for four fields that fit in a register or two.
/// </summary>
public readonly record struct Blip(TargetKind Kind, double Relative, double Distance, bool IsChosen);
