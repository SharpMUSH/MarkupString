namespace MarkupString.Layout;

/// <summary>
/// The characters a <see cref="Tree"/>'s guide lines are drawn with. Each piece is drawn before an
/// item's lines, all four the same width so the levels line up.
/// </summary>
public sealed record TreeGuide
{
	/// <summary>The style's name: a preset's, or <c>custom</c>.</summary>
	public string Name { get; init; } = "custom";

	/// <summary>Before the first line of an item with more below it at its level: <c>├─ </c>.</summary>
	public MarkupText Branch { get; init; } = MarkupText.Empty;

	/// <summary>Before the first line of the last item at its level: <c>└─ </c>.</summary>
	public MarkupText Last { get; init; } = MarkupText.Empty;

	/// <summary>Before every other line, where the level carries on below: <c>│  </c>.</summary>
	public MarkupText Pipe { get; init; } = MarkupText.Empty;

	/// <summary>Before every other line, where the level has ended.</summary>
	public MarkupText Blank { get; init; } = MarkupText.Empty;

	/// <summary>Light lines: <c>├─ └─ │</c>.</summary>
	public static TreeGuide Line { get; } = Of("line", "├─ ", "└─ ", "│  ");

	/// <summary>Light lines with a rounded last corner: <c>├─ ╰─ │</c>.</summary>
	public static TreeGuide Rounded { get; } = Of("rounded", "├─ ", "╰─ ", "│  ");

	/// <summary>Heavy lines: <c>┣━ ┗━ ┃</c>.</summary>
	public static TreeGuide Heavy { get; } = Of("heavy", "┣━ ", "┗━ ", "┃  ");

	/// <summary>Double lines: <c>╠═ ╚═ ║</c>.</summary>
	public static TreeGuide Double { get; } = Of("double", "╠═ ", "╚═ ", "║  ");

	/// <summary>ASCII, which any terminal shows: <c>|- `- |</c>.</summary>
	public static TreeGuide Ascii { get; } = Of("ascii", "|- ", "`- ", "|  ");

	/// <summary>Indentation alone.</summary>
	public static TreeGuide None { get; } = Of("none", "   ", "   ", "   ");

	/// <summary>Every preset, in the order a picker lists them.</summary>
	public static IReadOnlyList<TreeGuide> Presets { get; } = [Line, Rounded, Heavy, Double, Ascii, None];

	/// <summary>The preset named <paramref name="name"/>, ignoring case, or null.</summary>
	public static TreeGuide? Preset(string name)
	{
		foreach (var preset in Presets)
			if (string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase)) return preset;
		return null;
	}

	/// <summary>Whether every piece is printable ASCII.</summary>
	public bool IsAscii => IsAsciiText(Branch.Text) && IsAsciiText(Last.Text) && IsAsciiText(Pipe.Text) && IsAsciiText(Blank.Text);

	/// <summary>
	/// This guide for a client that shows nothing beyond ASCII: a piece that is not ASCII becomes the
	/// matching <see cref="Ascii"/> piece, so <c>└─ </c> stays distinct from <c>├─ </c> as <c>`- </c>.
	/// </summary>
	public TreeGuide ToAscii()
	{
		if (IsAscii) return this;
		static MarkupText Pick(MarkupText piece, MarkupText fallback) => IsAsciiText(piece.Text) ? piece : fallback;
		return this with
		{
			Branch = Pick(Branch, Ascii.Branch),
			Last = Pick(Last, Ascii.Last),
			Pipe = Pick(Pipe, Ascii.Pipe),
			Blank = Pick(Blank, Ascii.Blank),
		};
	}

	private static bool IsAsciiText(string text)
	{
		foreach (var c in text)
			if (c is < ' ' or > '~') return false;
		return true;
	}

	private static TreeGuide Of(string name, string branch, string last, string pipe) => new()
	{
		Name = name,
		Branch = MarkupText.Plain(branch),
		Last = MarkupText.Plain(last),
		Pipe = MarkupText.Plain(pipe),
		Blank = MarkupText.Plain(new string(' ', pipe.Length)),
	};
}
