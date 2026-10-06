namespace MarkupString.Layout;

/// <summary>
/// The characters a <see cref="BoxNode"/> or a <see cref="RuleNode"/> is drawn with. Every piece is
/// <see cref="MarkupText"/>, so it can carry colour, and the edges are fill patterns: <c>=-</c>
/// alternates along the whole edge.
/// </summary>
/// <remarks>
/// Start from a preset and replace the pieces you want with <c>with</c>. <see cref="Name"/> tells a
/// format that draws borders itself (HTML) which look was asked for; a style changed piece by piece
/// keeps its preset's name.
/// </remarks>
public sealed record BorderStyle
{
	/// <summary>The preset this style is, or was built from: <c>none</c>, <c>ascii</c>, <c>mush</c>, <c>single</c>, <c>double</c>, <c>heavy</c> or <c>rounded</c>.</summary>
	public required string Name { get; init; }

	/// <summary>The top-left corner.</summary>
	public MarkupText TopLeft { get; init; } = MarkupText.Empty;

	/// <summary>The top edge, a pattern.</summary>
	public MarkupText Top { get; init; } = MarkupText.Space;

	/// <summary>The top-right corner.</summary>
	public MarkupText TopRight { get; init; } = MarkupText.Empty;

	/// <summary>The left side.</summary>
	public MarkupText Left { get; init; } = MarkupText.Empty;

	/// <summary>The right side.</summary>
	public MarkupText Right { get; init; } = MarkupText.Empty;

	/// <summary>The bottom-left corner.</summary>
	public MarkupText BottomLeft { get; init; } = MarkupText.Empty;

	/// <summary>The bottom edge, a pattern.</summary>
	public MarkupText Bottom { get; init; } = MarkupText.Space;

	/// <summary>The bottom-right corner.</summary>
	public MarkupText BottomRight { get; init; } = MarkupText.Empty;

	/// <summary>Where a divider meets the left side.</summary>
	public MarkupText TeeLeft { get; init; } = MarkupText.Empty;

	/// <summary>Where a divider meets the right side.</summary>
	public MarkupText TeeRight { get; init; } = MarkupText.Empty;

	/// <summary>Written before a title set into an edge.</summary>
	public MarkupText TitleOpen { get; init; } = MarkupText.Space;

	/// <summary>Written after a title set into an edge.</summary>
	public MarkupText TitleClose { get; init; } = MarkupText.Space;

	/// <summary>No border: the title alone, and nothing at the sides.</summary>
	public static BorderStyle None { get; } = new() { Name = "none" };

	/// <summary><c>+-|</c>.</summary>
	public static BorderStyle Ascii { get; } = Of("ascii", "+-+", "||", "+-+", "++", "< ", " >");

	/// <summary><c>+=|</c>, the classic MUSH box.</summary>
	public static BorderStyle Mush { get; } = Of("mush", "+=+", "||", "+=+", "++", "< ", " >");

	/// <summary>Light box-drawing lines.</summary>
	public static BorderStyle Single { get; } = Of("single", "┌─┐", "││", "└─┘", "├┤", "┤ ", " ├");

	/// <summary>Double box-drawing lines.</summary>
	public static BorderStyle Double { get; } = Of("double", "╔═╗", "║║", "╚═╝", "╠╣", "╡ ", " ╞");

	/// <summary>Heavy box-drawing lines.</summary>
	public static BorderStyle Heavy { get; } = Of("heavy", "┏━┓", "┃┃", "┗━┛", "┣┫", "┫ ", " ┣");

	/// <summary>Light box-drawing lines with rounded corners.</summary>
	public static BorderStyle Rounded { get; } = Of("rounded", "╭─╮", "││", "╰─╯", "├┤", "┤ ", " ├");

	/// <summary>Every preset, by name.</summary>
	public static IReadOnlyList<BorderStyle> Presets { get; } = [None, Ascii, Mush, Single, Double, Heavy, Rounded];

	/// <summary>The preset named <paramref name="name"/>, ignoring case, or null.</summary>
	public static BorderStyle? Preset(string name)
	{
		foreach (var preset in Presets)
			if (string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase)) return preset;
		return null;
	}

	/// <summary>Whether every piece is printable ASCII, which any terminal can show.</summary>
	public bool IsAscii => All(piece => IsAsciiText(piece.Text));

	/// <summary>
	/// This style with each piece that is not plain ASCII replaced by the matching <see cref="Ascii"/>
	/// piece, for a client that cannot show anything else.
	/// </summary>
	public BorderStyle ToAscii()
	{
		if (IsAscii) return this;
		static MarkupText Pick(MarkupText piece, MarkupText fallback) => IsAsciiText(piece.Text) ? piece : fallback;
		return this with
		{
			TopLeft = Pick(TopLeft, Ascii.TopLeft),
			Top = Pick(Top, Ascii.Top),
			TopRight = Pick(TopRight, Ascii.TopRight),
			Left = Pick(Left, Ascii.Left),
			Right = Pick(Right, Ascii.Right),
			BottomLeft = Pick(BottomLeft, Ascii.BottomLeft),
			Bottom = Pick(Bottom, Ascii.Bottom),
			BottomRight = Pick(BottomRight, Ascii.BottomRight),
			TeeLeft = Pick(TeeLeft, Ascii.TeeLeft),
			TeeRight = Pick(TeeRight, Ascii.TeeRight),
			TitleOpen = Pick(TitleOpen, Ascii.TitleOpen),
			TitleClose = Pick(TitleClose, Ascii.TitleClose),
		};
	}

	private bool All(Func<MarkupText, bool> test) =>
		test(TopLeft) && test(Top) && test(TopRight) && test(Left) && test(Right) && test(BottomLeft)
		&& test(Bottom) && test(BottomRight) && test(TeeLeft) && test(TeeRight) && test(TitleOpen) && test(TitleClose);

	private static bool IsAsciiText(string text)
	{
		foreach (var c in text)
			if (c is < ' ' or > '~') return false;
		return true;
	}

	private static BorderStyle Of(string name, string top, string sides, string bottom, string tees, string open, string close) => new()
	{
		Name = name,
		TopLeft = Piece(top, 0),
		Top = Piece(top, 1),
		TopRight = Piece(top, 2),
		Left = Piece(sides, 0),
		Right = Piece(sides, 1),
		BottomLeft = Piece(bottom, 0),
		Bottom = Piece(bottom, 1),
		BottomRight = Piece(bottom, 2),
		TeeLeft = Piece(tees, 0),
		TeeRight = Piece(tees, 1),
		TitleOpen = MarkupText.Plain(open),
		TitleClose = MarkupText.Plain(close),
	};

	private static MarkupText Piece(string pieces, int index) => MarkupText.Plain(pieces[index].ToString());
}
