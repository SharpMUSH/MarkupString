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

	/// <summary>The width of the terminal's character cell in pixels, for a picture drawn in pixels (sixel). 10 when unknown.</summary>
	public int CellWidth { get; init; } = 10;

	/// <summary>The height of the terminal's character cell in pixels, for a picture drawn in pixels (sixel). 20 when unknown.</summary>
	public int CellHeight { get; init; } = 20;
}
