namespace MarkupString;

/// <summary>
/// The shared vocabulary: things a game puts in its text once — a sound, a picture, a pane — and that
/// each format writes in its own dialect, or leaves out, or stands something else in for.
/// </summary>
/// <remarks>
/// The types live here and say what a thing is; the packages for each format say how it is written
/// (<c>WithAnsi()</c>, <c>WithHtml()</c>, <c>WithMxp()</c>, <c>WithPueblo()</c>). A format with no
/// emitter for a thing does what it always does with markup it does not know: a point
/// (<see cref="IPointMarkup"/>) writes nothing, and a markup wrapping text writes the text.
/// </remarks>
public sealed partial class MarkupText
{
	/// <summary>
	/// The character a point rides on: a zero-width space. It measures nothing, is left out of
	/// <see cref="ToPlainText"/>, and is never written — a format either writes the point or writes
	/// nothing.
	/// </summary>
	public const string PointCarrier = "\u200b";

	/// <summary>A point in the text, riding on <see cref="PointCarrier"/>.</summary>
	public static MarkupText Point(IPointMarkup markup)
	{
		ArgumentNullException.ThrowIfNull(markup);
		return Wrap(markup, PointCarrier);
	}

	/// <summary>Plays a sound effect. See <see cref="SoundMarkup"/>.</summary>
	/// <param name="source">A file name in the game's sound directory, or an absolute address.</param>
	/// <param name="volume">From 0 to 100; the client's own level when null.</param>
	/// <param name="repeats">How many times to play it, or <see cref="SoundMarkup.Forever"/>; once when null.</param>
	/// <exception cref="ArgumentException"><paramref name="source"/> is empty.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="volume"/> or <paramref name="repeats"/> is out of range.</exception>
	public static MarkupText Sound(string source, int? volume = null, int? repeats = null) =>
		Point(Checked(new SoundMarkup(source, SoundChannel.Effects, volume, repeats)));

	/// <summary>Plays background music, replacing what was playing. See <see cref="SoundMarkup"/>.</summary>
	/// <param name="source">A file name in the game's sound directory, or an absolute address.</param>
	/// <param name="volume">From 0 to 100; the client's own level when null.</param>
	/// <param name="repeats">How many times to play it, or <see cref="SoundMarkup.Forever"/>; once when null.</param>
	/// <param name="continues">Carry on, rather than restart, when this piece is already playing.</param>
	/// <exception cref="ArgumentException"><paramref name="source"/> is empty.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="volume"/> or <paramref name="repeats"/> is out of range.</exception>
	public static MarkupText Music(string source, int? volume = null, int? repeats = null, bool continues = false) =>
		Point(Checked(new SoundMarkup(source, SoundChannel.Music, volume, repeats, continues)));

	/// <summary>Stops what is playing on <paramref name="channel"/>, or on both when null. See <see cref="SoundStopMarkup"/>.</summary>
	public static MarkupText StopSound(SoundChannel? channel = null) => Point(new SoundStopMarkup(channel));

	/// <summary>
	/// A picture, standing in <paramref name="description"/> — or the address, when there is none — for
	/// a client that shows no pictures. See <see cref="ImageMarkup"/>.
	/// </summary>
	/// <exception cref="ArgumentException"><paramref name="source"/> is empty.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
	public static MarkupText Image(
		string source,
		string? description = null,
		int? width = null,
		int? height = null,
		ImageAlign? align = null)
	{
		ArgumentException.ThrowIfNullOrEmpty(source);
		if (width is { } w) ArgumentOutOfRangeException.ThrowIfNegativeOrZero(w, nameof(width));
		if (height is { } h) ArgumentOutOfRangeException.ThrowIfNegativeOrZero(h, nameof(height));

		var shown = string.IsNullOrEmpty(description) ? source : description;
		return Wrap(new ImageMarkup(source, description, width, height, align), Plain(shown));
	}

	/// <summary>Sends <paramref name="content"/> to the pane named <paramref name="name"/>. See <see cref="PaneMarkup"/>.</summary>
	/// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
	public static MarkupText Pane(MarkupText content, string name, string? title = null)
	{
		ArgumentNullException.ThrowIfNull(content);
		ArgumentException.ThrowIfNullOrEmpty(name);
		return Wrap(new PaneMarkup(name, title), content);
	}

	/// <summary>Clears what the player has been shown. See <see cref="ClearScreenMarkup"/>.</summary>
	public static MarkupText ClearScreen() => Point(ClearScreenMarkup.Instance);

	/// <summary>Makes the links in <paramref name="group"/>, or every link, stop working. See <see cref="ExpireLinksMarkup"/>.</summary>
	public static MarkupText ExpireLinks(string? group = null) => Point(new ExpireLinksMarkup(group));

	/// <summary>Asks the client to fetch <paramref name="source"/> ahead of time. See <see cref="PrefetchMarkup"/>.</summary>
	/// <exception cref="ArgumentException"><paramref name="source"/> is empty.</exception>
	public static MarkupText Prefetch(string source)
	{
		ArgumentException.ThrowIfNullOrEmpty(source);
		return Point(new PrefetchMarkup(source));
	}

	/// <summary>Marks the prompt that follows as the login's <paramref name="field"/>. See <see cref="LoginPromptMarkup"/>.</summary>
	public static MarkupText LoginPrompt(LoginField field) => Point(new LoginPromptMarkup(field));

	/// <summary>Asks the client to reconnect to <paramref name="host"/>:<paramref name="port"/>. See <see cref="RelocateMarkup"/>.</summary>
	/// <exception cref="ArgumentException"><paramref name="host"/> is empty.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> is not a port.</exception>
	public static MarkupText Relocate(string host, int port, bool quiet = false)
	{
		ArgumentException.ThrowIfNullOrEmpty(host);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
		return Point(new RelocateMarkup(host, port, quiet));
	}

	/// <summary>Names <paramref name="content"/> as the variable <paramref name="name"/>. See <see cref="VariableMarkup"/>.</summary>
	/// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
	public static MarkupText Variable(MarkupText content, string name)
	{
		ArgumentNullException.ThrowIfNull(content);
		ArgumentException.ThrowIfNullOrEmpty(name);
		return Wrap(new VariableMarkup(name), content);
	}

	/// <summary>
	/// A gauge of <paramref name="variable"/> against <paramref name="maximum"/>, standing in
	/// <paramref name="content"/> for a client with no gauges. See <see cref="GaugeMarkup"/>.
	/// </summary>
	/// <exception cref="ArgumentException"><paramref name="variable"/> or <paramref name="maximum"/> is empty.</exception>
	public static MarkupText Gauge(MarkupText content, string variable, string maximum, string? caption = null, string? color = null)
	{
		ArgumentNullException.ThrowIfNull(content);
		ArgumentException.ThrowIfNullOrEmpty(variable);
		ArgumentException.ThrowIfNullOrEmpty(maximum);
		return Wrap(new GaugeMarkup(variable, maximum, caption, color), content);
	}

	/// <summary>
	/// <paramref name="variable"/> in the client's status bar, standing in <paramref name="content"/>
	/// for a client with none. See <see cref="StatusMarkup"/>.
	/// </summary>
	/// <exception cref="ArgumentException"><paramref name="variable"/> is empty.</exception>
	public static MarkupText Status(MarkupText content, string variable, string? maximum = null, string? caption = null)
	{
		ArgumentNullException.ThrowIfNull(content);
		ArgumentException.ThrowIfNullOrEmpty(variable);
		return Wrap(new StatusMarkup(variable, maximum, caption), content);
	}

	private static SoundMarkup Checked(SoundMarkup sound)
	{
		ArgumentException.ThrowIfNullOrEmpty(sound.Source, "source");
		if (sound.Volume is { } volume && volume is < 0 or > 100)
			throw new ArgumentOutOfRangeException("volume", volume, "A volume is from 0 to 100.");
		if (sound.Repeats is { } repeats && repeats is 0 or < SoundMarkup.Forever)
			throw new ArgumentOutOfRangeException("repeats", repeats, "A sound plays at least once, or forever.");
		return sound;
	}
}
