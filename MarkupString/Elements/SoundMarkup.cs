namespace MarkupString;

/// <summary>Which player a sound goes to.</summary>
public enum SoundChannel
{
	/// <summary>Effects: a door, a footstep. Several can overlap.</summary>
	Effects,

	/// <summary>Background music: one piece at a time, replacing whatever was playing.</summary>
	Music,
}

/// <summary>
/// Plays a sound. <see cref="MarkupText.Sound"/> and <see cref="MarkupText.Music"/> build one.
/// </summary>
/// <remarks>
/// MXP writes <c>&lt;SOUND&gt;</c> or <c>&lt;MUSIC&gt;</c>, Pueblo <c>&lt;img xch_sound&gt;</c>, and HTML an
/// <c>&lt;audio&gt;</c> element that the page decides whether to play. A terminal is sent nothing.
/// </remarks>
/// <param name="Source">
/// The sound: a file name the client resolves against the game's own sound directory, or an absolute
/// address.
/// </param>
/// <param name="Channel">Effects or music.</param>
/// <param name="Volume">From 0 to 100; the client's own level when null.</param>
/// <param name="Repeats">
/// How many times to play it, or <see cref="Forever"/>; once when null. A client that cannot count
/// plays it once, or loops it for <see cref="Forever"/>.
/// </param>
/// <param name="Continues">
/// For music: carry on, rather than restart, when this piece is already playing.
/// </param>
public sealed record SoundMarkup(
	string Source,
	SoundChannel Channel = SoundChannel.Effects,
	int? Volume = null,
	int? Repeats = null,
	bool Continues = false) : IPointMarkup
{
	/// <summary>The <see cref="Repeats"/> value that loops a sound until it is stopped.</summary>
	public const int Forever = -1;

	/// <summary>Whether this sound loops until it is stopped.</summary>
	public bool Loops => Repeats == Forever;
}

/// <summary>
/// Stops what is playing on one channel, or on both when <see cref="Channel"/> is null.
/// <see cref="MarkupText.StopSound"/> builds one.
/// </summary>
/// <param name="Channel">The channel to silence; both when null.</param>
public sealed record SoundStopMarkup(SoundChannel? Channel = null) : IPointMarkup;
