namespace MarkupString.Ansi;

/// <summary>
/// What a terminal can do beyond colour, each of which <see cref="MarkupFormat.Ansi"/> writes only to a
/// client that has it. What a client has is its host's to work out — from the terminal type it reported,
/// from asking the terminal, or from the player — and belongs to that one connection.
/// </summary>
/// <remarks>
/// Every sequence here is one a terminal that does not know it ignores, but a MUD client that is not a
/// terminal emulator may print it, which is why none of them is sent on a guess.
/// </remarks>
[Flags]
public enum TerminalFeatures
{
	/// <summary>Colour and attributes only: a link is its text, a picture its text art.</summary>
	None = 0,

	/// <summary>A URL link as an OSC 8 hyperlink (<c>ESC ] 8 ; ; url ST</c>).</summary>
	Hyperlinks = 1,

	/// <summary>
	/// A command link as an MSLP link (<c>ESC ] 68 ; 1 ; SEND ; command BEL</c> before the underlined text),
	/// which a client advertising MTTS's MSLP bit sends back to the game when clicked.
	/// </summary>
	CommandLinks = 2,

	/// <summary>
	/// Pictures through the Kitty graphics protocol, drawn with Unicode placeholders: the picture is sent
	/// once per connection, and each cell it covers is a placeholder character, which wraps, scrolls and is
	/// erased like text.
	/// </summary>
	KittyGraphics = 4,

	/// <summary>
	/// Pictures through iTerm2's inline images (<c>ESC ] 1337 ; File=…</c>), drawn over the cells the
	/// picture covers after the cursor has made room for them.
	/// </summary>
	InlineImages = 8,

	/// <summary>Pictures as sixel graphics, drawn over the cells the picture covers like <see cref="InlineImages"/>.</summary>
	Sixel = 16,

	/// <summary>
	/// Pictures as coloured half-block characters (<c>▀</c>, <c>▄</c>), two pixels to a cell: ordinary text
	/// any UTF-8 terminal with colour shows. The lowest rung of pictures, and the only one with no
	/// protocol at all.
	/// </summary>
	BlockArt = 32,

	/// <summary>
	/// A moving picture plays, through whichever of <see cref="KittyGraphics"/> or <see cref="InlineImages"/> draws
	/// it: Kitty is sent its frames, iTerm2 a looping GIF. Without it, and by every other way of drawing, a moving
	/// picture is its first frame. Not a way of drawing on its own, so not part of <see cref="Pictures"/>.
	/// </summary>
	MovingPictures = 64,

	/// <summary>Every way of drawing a picture.</summary>
	Pictures = KittyGraphics | InlineImages | Sixel | BlockArt,
}
