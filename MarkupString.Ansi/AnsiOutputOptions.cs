namespace MarkupString.Ansi;

/// <summary>What one client is sent in <see cref="MarkupFormat.Ansi"/>: how much colour, and what else its terminal can do.</summary>
/// <param name="ColorDepth">The colour the client can display; every colour is written at this depth.</param>
/// <param name="Features">What the terminal can do beyond colour.</param>
public sealed record AnsiOutputOptions(
	AnsiColorDepth ColorDepth = AnsiColorDepth.TrueColor,
	TerminalFeatures Features = TerminalFeatures.Hyperlinks)
{
	/// <summary>
	/// Where pictures' pixels come from, for a client with any of <see cref="TerminalFeatures.Pictures"/>. Without
	/// one, a picture is its text art whatever the client can draw.
	/// </summary>
	public ITerminalPictureSource? Pictures { get; init; }

	/// <summary>
	/// The width of the terminal's character cell in pixels, for a picture drawn in pixels (sixel). 10 when unknown,
	/// which a terminal that does not know its own reports as 0: a value below 1 is taken as unknown.
	/// </summary>
	public int CellWidth { get; init => field = value > 0 ? value : 10; } = 10;

	/// <summary>The height of the terminal's character cell in pixels, for a picture drawn in pixels (sixel). 20 when unknown, as is a value below 1.</summary>
	public int CellHeight { get; init => field = value > 0 ? value : 20; } = 20;

	/// <summary>
	/// The client's terminal, when it is known, for how it wants what it is sent (how long an inline image may be,
	/// and whether one is sent in parts). Without one, an inline image is held to iTerm2's limit and sent whole.
	/// It sends nothing <see cref="Features"/> does not name.
	/// </summary>
	public TerminalProfile? Terminal { get; init; }

	/// <summary>
	/// What a client using <paramref name="terminal"/> is sent: of what it can do, only what the player turned on
	/// (<paramref name="chosen"/>).
	/// </summary>
	public static AnsiOutputOptions For(TerminalProfile terminal, TerminalFeatures chosen, AnsiColorDepth colorDepth = AnsiColorDepth.TrueColor)
	{
		ArgumentNullException.ThrowIfNull(terminal);
		return new AnsiOutputOptions(colorDepth, terminal.Allow(chosen)) { Terminal = terminal };
	}
}
