namespace MarkupString.Ansi;

/// <summary>
/// How much colour a client can display. A registry built with
/// <see cref="AnsiRegistration.WithAnsiOutput(MarkupRegistry, AnsiOutputOptions)"/> writes every colour at the depth it is given, so a client
/// is never sent a sequence it cannot read.
/// </summary>
public enum AnsiColorDepth
{
	/// <summary>Every colour as it is: 24-bit RGB as <c>38;2;r;g;b</c>.</summary>
	TrueColor,

	/// <summary>The xterm 256-colour palette: an RGB colour becomes its nearest palette entry.</summary>
	Xterm256,

	/// <summary>
	/// The sixteen standard colours: a palette entry or an RGB colour becomes the nearest of them. Entries 0-15
	/// of the xterm palette are those sixteen, and map to them exactly.
	/// </summary>
	Standard,

	/// <summary>
	/// No colour, attributes only. A bright standard foreground is written as bold, which is how it reaches
	/// the client at every other depth.
	/// </summary>
	Attributes,

	/// <summary>No SGR at all.</summary>
	None,
}
