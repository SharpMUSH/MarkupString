namespace MarkupString.Ansi;

/// <summary>
/// A terminal this package knows: what it can be sent beyond colour, and how it wants it. A host names the
/// client's terminal (from what it reported, with <see cref="Identify"/>, or from the player, with
/// <see cref="Find"/>) and sends it what the player has turned on of what it can do (<see cref="Allow"/>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Features"/> is what the terminal can do, not what it is sent: every feature here is one a player
/// turns on. A terminal is often reached through something that changes what it can show (a multiplexer, an
/// ssh hop, a setting left off), and only the player sees the result.
/// </para>
/// <para>
/// A host may describe a terminal this package does not know with a profile of its own.
/// </para>
/// </remarks>
/// <param name="Id">The short name a player gives it, such as <c>kitty</c> or <c>windows-terminal</c>.</param>
/// <param name="Name">Its name as written, such as <c>Windows Terminal</c>.</param>
/// <param name="Features">Everything it can be sent.</param>
public sealed record TerminalProfile(string Id, string Name, TerminalFeatures Features)
{
	/// <summary>
	/// The names it reports for itself, compared without case: a telnet terminal type, or the name in its reply to
	/// XTVERSION (<c>CSI &gt; q</c>), which is read up to its first space or parenthesis.
	/// </summary>
	public IReadOnlyList<string> Reports { get; init; } = [];

	/// <summary>
	/// The longest single iTerm2 inline-image sequence it takes, in characters; 0 when it sets none.
	/// </summary>
	public int InlineImageLimit { get; init; }

	/// <summary>Whether it reads an inline image sent in parts (<c>MultipartFile</c>, <c>FilePart</c>, <c>FileEnd</c>).</summary>
	public bool MultipartInlineImages { get; init; }

	/// <summary>What it is sent of <paramref name="chosen"/>, the features the player turned on.</summary>
	public TerminalFeatures Allow(TerminalFeatures chosen) => Features & chosen;

	// What each terminal is listed with was read from its own source and release notes in October 2026; where a
	// feature came in a later release, the release is named. A feature left out is one it lacks or one that could
	// not be confirmed.

	/// <summary>kitty: Kitty graphics with Unicode placeholders (0.28), and their animation.</summary>
	public static TerminalProfile Kitty { get; } = new("kitty", "kitty",
		TerminalFeatures.Hyperlinks | TerminalFeatures.KittyGraphics | TerminalFeatures.MovingPictures | TerminalFeatures.BlockArt)
	{
		Reports = ["kitty", "xterm-kitty"],
	};

	/// <summary>Ghostty: Kitty graphics with Unicode placeholders. No released version animates them (1.3.1 refuses).</summary>
	public static TerminalProfile Ghostty { get; } = new("ghostty", "Ghostty",
		TerminalFeatures.Hyperlinks | TerminalFeatures.KittyGraphics | TerminalFeatures.BlockArt)
	{
		Reports = ["ghostty", "xterm-ghostty"],
	};

	/// <summary>WezTerm: iTerm2 inline images, which play a GIF, and sixel. Its Kitty graphics have no placeholders.</summary>
	public static TerminalProfile WezTerm { get; } = new("wezterm", "WezTerm",
		TerminalFeatures.Hyperlinks | TerminalFeatures.InlineImages | TerminalFeatures.MovingPictures | TerminalFeatures.Sixel
		| TerminalFeatures.BlockArt)
	{
		Reports = ["wezterm"],
	};

	/// <summary>
	/// iTerm2: its own inline images, which play a GIF, one sequence at most a mebibyte, sent in parts since 3.5;
	/// and sixel (3.3). It also reads Kitty graphics now, but does not animate them and its release notes do not
	/// announce them, so it is drawn for with its own protocol.
	/// </summary>
	public static TerminalProfile ITerm2 { get; } = new("iterm2", "iTerm2",
		TerminalFeatures.Hyperlinks | TerminalFeatures.InlineImages | TerminalFeatures.MovingPictures | TerminalFeatures.Sixel
		| TerminalFeatures.BlockArt)
	{
		Reports = ["iterm2"],
		InlineImageLimit = 1_048_576,
		MultipartInlineImages = true,
	};

	/// <summary>
	/// Konsole: iTerm2 inline images (22.04), which show a GIF still, and sixel (22.04). Links are off until the
	/// player allows them in its profile. Its Kitty graphics have no placeholders.
	/// </summary>
	public static TerminalProfile Konsole { get; } = new("konsole", "Konsole",
		TerminalFeatures.Hyperlinks | TerminalFeatures.InlineImages | TerminalFeatures.Sixel | TerminalFeatures.BlockArt)
	{
		Reports = ["konsole"],
	};

	/// <summary>foot: sixel.</summary>
	public static TerminalProfile Foot { get; } = new("foot", "foot",
		TerminalFeatures.Hyperlinks | TerminalFeatures.Sixel | TerminalFeatures.BlockArt)
	{
		Reports = ["foot", "foot-extra"],
	};

	/// <summary>xterm: sixel, when it emulates a terminal that had it (<c>-ti vt340</c>). No links.</summary>
	public static TerminalProfile XTerm { get; } = new("xterm", "xterm",
		TerminalFeatures.Sixel | TerminalFeatures.BlockArt)
	{
		Reports = ["xterm"],
	};

	/// <summary>Windows Terminal: sixel (1.22). It does not say what it is, so only a player can name it.</summary>
	public static TerminalProfile WindowsTerminal { get; } = new("windows-terminal", "Windows Terminal",
		TerminalFeatures.Hyperlinks | TerminalFeatures.Sixel | TerminalFeatures.BlockArt);

	/// <summary>mintty: iTerm2 inline images (3.1), up to its 4,444,444-byte image limit by default, and sixel.</summary>
	public static TerminalProfile Mintty { get; } = new("mintty", "mintty",
		TerminalFeatures.Hyperlinks | TerminalFeatures.InlineImages | TerminalFeatures.Sixel | TerminalFeatures.BlockArt)
	{
		Reports = ["mintty"],
		InlineImageLimit = 4_444_444,
	};

	/// <summary>
	/// Visual Studio Code's terminal (xterm.js): iTerm2 inline images, which show a GIF still, and sixel, once the
	/// player turns on its <c>terminal.integrated.enableImages</c> setting.
	/// </summary>
	public static TerminalProfile VsCode { get; } = new("vscode", "Visual Studio Code",
		TerminalFeatures.Hyperlinks | TerminalFeatures.InlineImages | TerminalFeatures.Sixel | TerminalFeatures.BlockArt)
	{
		Reports = ["xterm.js"],
		InlineImageLimit = 32 * 1_048_576,
	};

	/// <summary>Contour: sixel. Its Kitty graphics have no placeholders.</summary>
	public static TerminalProfile Contour { get; } = new("contour", "Contour",
		TerminalFeatures.Hyperlinks | TerminalFeatures.Sixel | TerminalFeatures.BlockArt)
	{
		Reports = ["contour"],
	};

	/// <summary>Rio: Kitty graphics with Unicode placeholders (0.3), iTerm2 inline images shown still, and sixel.</summary>
	public static TerminalProfile Rio { get; } = new("rio", "Rio",
		TerminalFeatures.Hyperlinks | TerminalFeatures.KittyGraphics | TerminalFeatures.InlineImages | TerminalFeatures.Sixel
		| TerminalFeatures.BlockArt)
	{
		Reports = ["rio"],
	};

	/// <summary>mlterm: iTerm2 inline images and sixel. No links.</summary>
	public static TerminalProfile Mlterm { get; } = new("mlterm", "mlterm",
		TerminalFeatures.InlineImages | TerminalFeatures.Sixel | TerminalFeatures.BlockArt)
	{
		Reports = ["mlterm"],
	};

	/// <summary>Alacritty: links, and no pictures but half blocks.</summary>
	public static TerminalProfile Alacritty { get; } = new("alacritty", "Alacritty",
		TerminalFeatures.Hyperlinks | TerminalFeatures.BlockArt)
	{
		Reports = ["alacritty"],
	};

	/// <summary>
	/// A VTE terminal (GNOME Terminal, Tilix, Terminator and others): links where the application allows them, and
	/// half blocks. Its sixel is off unless both the library and the application were built for it.
	/// </summary>
	public static TerminalProfile Vte { get; } = new("vte", "GNOME Terminal (VTE)",
		TerminalFeatures.Hyperlinks | TerminalFeatures.BlockArt)
	{
		Reports = ["vte"],
	};

	/// <summary>
	/// tmux, which stands between the client and its terminal: links (3.4) and half blocks. A picture protocol has
	/// to be passed through it, which this package does not write.
	/// </summary>
	public static TerminalProfile Tmux { get; } = new("tmux", "tmux",
		TerminalFeatures.Hyperlinks | TerminalFeatures.BlockArt)
	{
		Reports = ["tmux"],
	};

	/// <summary>Every terminal this package knows.</summary>
	public static IReadOnlyList<TerminalProfile> Known { get; } =
		[Kitty, Ghostty, WezTerm, ITerm2, Konsole, Foot, XTerm, WindowsTerminal, Mintty, VsCode, Contour, Rio, Mlterm, Alacritty, Vte, Tmux];

	/// <summary>The known terminal a player calls <paramref name="id"/>, compared without case, or null.</summary>
	public static TerminalProfile? Find(string? id) =>
		string.IsNullOrWhiteSpace(id) ? null : Known.FirstOrDefault(profile => string.Equals(profile.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// The known terminal that reported <paramref name="reported"/> (a terminal type, or an XTVERSION reply such
	/// as <c>kitty(0.35.2)</c>, <c>WezTerm 20240203-110809-5046fc22</c> or <c>VTE(8000)</c>), or null. Only the name is read, up to
	/// its first space or parenthesis, and it must be the whole of one of <see cref="Reports"/>: the
	/// <c>xterm-256color</c> nearly every client sends names no terminal.
	/// </summary>
	public static TerminalProfile? Identify(string? reported)
	{
		if (string.IsNullOrWhiteSpace(reported)) return null;
		var name = reported.Trim();
		var end = name.IndexOfAny([' ', '(']);
		if (end > 0) name = name[..end];
		return Known.FirstOrDefault(profile => profile.Reports.Any(report => string.Equals(report, name, StringComparison.OrdinalIgnoreCase)));
	}
}
