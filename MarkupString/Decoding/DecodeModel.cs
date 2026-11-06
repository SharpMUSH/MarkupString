using MarkupString.Display;

namespace MarkupString.Decoding;

/// <summary>What a decoded line is.</summary>
public enum LineKind
{
	/// <summary>Output that ended with a line break.</summary>
	Output,

	/// <summary>A prompt: output the server marked with telnet GA or EOR, or left without a line break.</summary>
	Prompt,
}

/// <summary>One decoded line, ready for scrollback.</summary>
/// <param name="Content">The line's text and markup, without its line break.</param>
/// <param name="Kind">Whether it is output or a prompt.</param>
public sealed record DisplayLine(MarkupText Content, LineKind Kind)
{
	/// <summary>The line's number within its decoder, from 0, for keying a view of it.</summary>
	public long Sequence { get; init; }

	/// <summary>The pane the server sent the line to (MXP <c>&lt;DEST&gt;</c>, Pueblo <c>xch_pane</c>), or null for the main output.</summary>
	public string? Destination { get; init; }
}

/// <summary>Something a decoder found that is not text to show.</summary>
public abstract record DecodeEvent;

/// <summary>Text the client should send back to the server: an answer to a query.</summary>
/// <param name="Text">The answer, exactly as it is to be sent.</param>
public sealed record ReplyEvent(string Text) : DecodeEvent;

/// <summary>The server set the window or tab title (OSC 0, 1 or 2).</summary>
/// <param name="Title">The title.</param>
/// <param name="Icon">Whether it named the icon (OSC 1) rather than, or as well as, the window.</param>
public sealed record TitleEvent(string Title, bool Icon) : DecodeEvent;

/// <summary>Which colour a <see cref="PaletteEvent"/> changes.</summary>
public enum PaletteSlot
{
	/// <summary>An entry of the 256-colour palette (OSC 4).</summary>
	Index,

	/// <summary>The default foreground (OSC 10).</summary>
	Foreground,

	/// <summary>The default background (OSC 11).</summary>
	Background,

	/// <summary>The cursor (OSC 12).</summary>
	Cursor,
}

/// <summary>The server redefined a colour, or reset it (OSC 4, 10, 11, 12, 104, 110, 111, 112).</summary>
/// <param name="Slot">Which colour.</param>
/// <param name="Index">The palette entry, for <see cref="PaletteSlot.Index"/>; -1 with a null colour resets every entry.</param>
/// <param name="Color">The new colour, or null for a reset.</param>
public sealed record PaletteEvent(PaletteSlot Slot, int Index, RgbColor? Color) : DecodeEvent;

/// <summary>The server asked to write the clipboard (OSC 52). Refuse it unless the player allowed it.</summary>
/// <param name="Selection">The selection it named, such as <c>c</c> for the clipboard.</param>
/// <param name="Text">The text, decoded, or null when it asked to read the clipboard instead.</param>
public sealed record ClipboardEvent(string Selection, string? Text) : DecodeEvent;

/// <summary>The server set a variable (MXP <c>&lt;VAR&gt;</c> or an entity), for gauges and status bars.</summary>
/// <param name="Name">The variable.</param>
/// <param name="Value">Its value, or null when it was deleted.</param>
public sealed record VariableEvent(string Name, string? Value) : DecodeEvent;

/// <summary>The server offered a file to save (iTerm2 <c>inline=0</c>). It is never saved by the decoder.</summary>
/// <param name="Name">The name it gave, if any.</param>
/// <param name="Size">Its size in bytes.</param>
public sealed record DownloadEvent(string? Name, int Size) : DecodeEvent;

/// <summary>A text protocol such as MXP was turned on or off.</summary>
/// <param name="Name">The protocol.</param>
/// <param name="Active">Whether it is now on.</param>
public sealed record ProtocolEvent(string Name, bool Active) : DecodeEvent;

/// <summary>Who the client is, for answering a server that asks, and what its output can show.</summary>
public sealed record ClientIdentity
{
	/// <summary>The client's name, as it answers XTVERSION and MXP <c>&lt;VERSION&gt;</c>.</summary>
	public string Name { get; init; } = "MarkupString";

	/// <summary>The client's version.</summary>
	public string Version { get; init; } = "1.0";

	/// <summary>What the output the session is shown on can draw.</summary>
	public OutputCapabilities Output { get; init; } = OutputCapabilities.PlainText;

	/// <summary>The pane's width in cells.</summary>
	public int Columns { get; init; } = 80;

	/// <summary>The pane's height in cells.</summary>
	public int Rows { get; init; } = 24;

	/// <summary>One cell's width in pixels. 8 by 16 keeps a square picture square under both Kitty and half blocks.</summary>
	public int CellWidth { get; init; } = 8;

	/// <summary>One cell's height in pixels.</summary>
	public int CellHeight { get; init; } = 16;

	/// <summary>The default foreground, for a server that asks (OSC 10), or null not to say.</summary>
	public RgbColor? Foreground { get; init; }

	/// <summary>The default background, for a server that asks (OSC 11), or null not to say.</summary>
	public RgbColor? Background { get; init; }

	/// <summary>Which questions the decoder answers.</summary>
	public ClientAnswers Answers { get; init; } = ClientAnswers.Default;

	/// <summary>
	/// The client's MTTS bits (Mud Terminal Type Standard), for the last step of the telnet TTYPE cycle:
	/// ANSI, VT100, UTF-8, 256 colours, OSC colour palette, truecolor and MSLP from <see cref="Output"/>.
	/// </summary>
	/// <param name="screenReader">Whether the player uses a screen reader.</param>
	/// <param name="tls">Whether the connection is encrypted.</param>
	public MttsFlags ToMtts(bool screenReader = false, bool tls = false)
	{
		var flags = MttsFlags.Utf8 | MttsFlags.Vt100;
		if (Output.Colors >= ColorLevel.Standard) flags |= MttsFlags.Ansi;
		if (Output.Colors >= ColorLevel.Xterm256) flags |= MttsFlags.Colors256 | MttsFlags.OscColorPalette;
		if (Output.Colors >= ColorLevel.TrueColor) flags |= MttsFlags.TrueColor;
		if ((Output.Interactions & InteractionKinds.Command) != 0) flags |= MttsFlags.Mslp;
		if (Output.Unicode == UnicodeLevel.Ascii) flags &= ~MttsFlags.Utf8;
		if (screenReader) flags |= MttsFlags.ScreenReader;
		if (tls) flags |= MttsFlags.Ssl;
		return flags;
	}
}

/// <summary>The questions a decoder answers. Each one can be turned off.</summary>
[Flags]
public enum ClientAnswers
{
	/// <summary>None.</summary>
	None = 0,

	/// <summary>Device attributes (<c>CSI c</c>) and the terminal's name and version (XTVERSION, <c>CSI &gt; q</c>).</summary>
	Identity = 1,

	/// <summary>The pane's size in cells and pixels (<c>CSI 14 t</c>, <c>16 t</c>, <c>18 t</c>).</summary>
	Size = 2,

	/// <summary>The default colours (OSC 10, 11 with <c>?</c>).</summary>
	Colors = 4,

	/// <summary>Picture protocol queries (Kitty <c>a=q</c>).</summary>
	Pictures = 8,

	/// <summary>
	/// The cursor's position (<c>CSI 6 n</c>). Off by default: a server that relies on it assumes a full screen,
	/// which a scrolling pane is not.
	/// </summary>
	CursorPosition = 16,

	/// <summary>MXP <c>&lt;SUPPORT&gt;</c> and <c>&lt;VERSION&gt;</c>.</summary>
	Mxp = 32,

	/// <summary>Everything but the cursor position.</summary>
	Default = Identity | Size | Colors | Pictures | Mxp,
}

/// <summary>The Mud Terminal Type Standard's bits.</summary>
[Flags]
public enum MttsFlags
{
	/// <summary>None.</summary>
	None = 0,

	/// <summary>ANSI colour.</summary>
	Ansi = 1,

	/// <summary>VT100 control sequences.</summary>
	Vt100 = 2,

	/// <summary>UTF-8.</summary>
	Utf8 = 4,

	/// <summary>The 256-colour palette.</summary>
	Colors256 = 8,

	/// <summary>Mouse tracking.</summary>
	MouseTracking = 16,

	/// <summary>Redefining the palette with OSC 4.</summary>
	OscColorPalette = 32,

	/// <summary>The player uses a screen reader.</summary>
	ScreenReader = 64,

	/// <summary>The client is a proxy.</summary>
	Proxy = 128,

	/// <summary>24-bit colour.</summary>
	TrueColor = 256,

	/// <summary>The Mud New-Environment Standard.</summary>
	Mnes = 512,

	/// <summary>The Mud Server Link Protocol: command links.</summary>
	Mslp = 1024,

	/// <summary>The connection is encrypted.</summary>
	Ssl = 2048,
}
