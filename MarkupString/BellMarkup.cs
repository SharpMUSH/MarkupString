namespace MarkupString;

/// <summary>
/// A bell: the client is asked to get someone's attention. It is written as U+0007 for a client that
/// reads one — a terminal, Pueblo, MXP — and as a marked span for HTML, where the page decides what a
/// bell means. <see cref="MarkupText.Bell"/> builds one.
/// </summary>
/// <remarks>
/// A bell rides on the single U+0007 it marks, which is a real position in the text and measures zero
/// display cells (<see cref="DisplayWidth"/>), so it survives slicing, concatenation and padding as a
/// point in the string without moving anything that is laid out around it. A format with no bell drops
/// the character with every other control, and nothing is left behind.
/// </remarks>
public sealed class BellMarkup : IPointMarkup
{
	/// <summary>The character a bell is carried on.</summary>
	public const string Character = "\u0007";

	/// <inheritdoc/>
	public string Carrier => Character;

	/// <summary>The one instance; a bell carries no state.</summary>
	public static readonly BellMarkup Instance = new();

	private BellMarkup()
	{
	}
}
