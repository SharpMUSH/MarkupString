namespace MarkupString.Layout;

/// <summary>
/// A hanging indent: <paramref name="Amount"/> cells from line <paramref name="FromLine"/> on,
/// with the column widened by <paramref name="Widen"/> cells from that same line. Line numbers
/// are zero-based, so the default indents every line but the first.
/// </summary>
public readonly record struct Indent(int Amount, int FromLine = 1, int Widen = 0);

/// <summary>
/// Everything about one column: how its text is shaped into lines, how those lines are drawn,
/// and how the column behaves among its neighbours. Compose with <c>with</c>.
/// </summary>
/// <remarks>
/// The vocabulary is behavioural rather than syntactic on purpose. PennMUSH and RhostMUSH use
/// the same characters for different things — <c>-</c> centres in one and left-aligns in the
/// other, and <c>`</c> and <c>'</c> mean opposite things — so no flag character appears here.
/// A caller emulating a server maps that server's characters onto these properties.
/// </remarks>
public sealed record ColumnFormat
{
	/// <summary>The column's width in display cells.</summary>
	public int Width { get; init; }

	/// <summary>How text becomes lines. PennMUSH columns wrap on words; RhostMUSH fields do not wrap.</summary>
	public WrapMode Wrap { get; init; } = WrapMode.None;

	/// <summary>What becomes of the space a word break lands on. PennMUSH drops it, RhostMUSH keeps it.</summary>
	public BreakSpace BreakSpace { get; init; } = BreakSpace.Drop;

	/// <summary>Spaces to substitute for each tab. Zero leaves tabs alone.</summary>
	public int TabWidth { get; init; }

	/// <summary>Display cells of input to keep before wrapping. Zero is unlimited.</summary>
	public int MaxCells { get; init; }

	/// <summary>Lines to keep after wrapping. Zero is unlimited.</summary>
	public int MaxLines { get; init; }

	/// <summary>An indent applied to continuation lines.</summary>
	public Indent Indent { get; init; }

	/// <summary>Where a line's text sits in the column.</summary>
	public Alignment Alignment { get; init; } = Alignment.Left;

	/// <summary>What fills the cells the text does not occupy. Its own markup survives.</summary>
	public MarkupText Fill { get; init; } = MarkupText.Space;

	/// <summary>
	/// The fill used after the text when <see cref="Alignment.Center"/> wants two different ones.
	/// <see cref="Fill"/> is used on both sides when this is null.
	/// </summary>
	public MarkupText? FillRight { get; init; }

	/// <summary>How a fill wider than one cell is sampled.</summary>
	public FillPhase FillPhase { get; init; } = FillPhase.Continuous;

	/// <summary>What fills a line with no text on it.</summary>
	public BlankLineFill BlankLineFill { get; init; } = BlankLineFill.Pattern;

	/// <summary>Whether text wider than the column is cut or left to overflow.</summary>
	public TruncationType Truncation { get; init; } = TruncationType.Truncate;

	/// <summary>Which end of an over-wide line is cut.</summary>
	public CutFrom CutFrom { get; init; } = CutFrom.End;

	/// <summary>Emit nothing after the text instead of filling out to the width.</summary>
	public bool NoFill { get; init; }

	/// <summary>Markup applied to every rendered line of the column, fill included.</summary>
	public IMarkup? Markup { get; init; }

	/// <summary>What the column does on a row where it has no text.</summary>
	public WhenEmpty WhenEmpty { get; init; }

	/// <summary>Repeat the column's lines for as long as another column still has text.</summary>
	public bool Repeat { get; init; }

	/// <summary>Suppress the separator that follows this column.</summary>
	public bool NoSeparatorAfter { get; init; }

	/// <summary>
	/// Allow a final all-blank row to be dropped. The row goes only when every column in the
	/// layout carries this.
	/// </summary>
	public bool SuppressBlankLast { get; init; }
}
