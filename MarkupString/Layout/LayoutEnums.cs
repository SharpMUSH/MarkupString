namespace MarkupString.Layout;

/// <summary>How a column turns text into lines.</summary>
public enum WrapMode
{
	/// <summary>One line; a newline in the text passes through untouched.</summary>
	None,

	/// <summary>Break on newlines only, keeping every line inside the column.</summary>
	HardBreaks,

	/// <summary>Break at the column width, mid-word if need be. Newlines also break.</summary>
	Cell,

	/// <summary>
	/// Break at the last space that fits, falling back to a width break for a word longer than
	/// the column. Newlines also break.
	/// </summary>
	Word,
}

/// <summary>What happens to the space a <see cref="WrapMode.Word"/> break lands on.</summary>
public enum BreakSpace
{
	/// <summary>The break consumes the space, along with the rest of its run.</summary>
	Drop,

	/// <summary>
	/// The space stays at the end of the line, which may then exceed the column by one cell.
	/// </summary>
	Keep,
}

/// <summary>Where a line's text sits inside its column.</summary>
public enum Alignment
{
	/// <summary>Against the left edge, fill after it.</summary>
	Left,

	/// <summary>Against the right edge, fill before it.</summary>
	Right,

	/// <summary>Fill split either side, the odd cell going to the right.</summary>
	Center,

	/// <summary>Gaps between words widen so the text spans the column.</summary>
	Full,

	/// <summary>
	/// As <see cref="Full"/>, except a line that ends a paragraph, which is left-aligned.
	/// </summary>
	Paragraph,
}

/// <summary>How a fill pattern wider than one cell is sampled.</summary>
public enum FillPhase
{
	/// <summary>
	/// Indexed by absolute cell position in the column, so one unbroken pattern runs behind the
	/// text and shows through every gap.
	/// </summary>
	Continuous,

	/// <summary>Restarted at the beginning of each run of fill.</summary>
	Restart,
}

/// <summary>What fills a line that has no text on it.</summary>
public enum BlankLineFill
{
	/// <summary>The column's fill, like any other line.</summary>
	Pattern,

	/// <summary>Spaces, leaving blank lines unpatterned.</summary>
	Spaces,
}

/// <summary>Which end of an over-wide line is discarded.</summary>
public enum CutFrom
{
	/// <summary>Cut the tail, keeping the beginning.</summary>
	End,

	/// <summary>Cut the head, keeping the end.</summary>
	Start,
}

/// <summary>What a column does on a row where it has no text.</summary>
public enum WhenEmpty
{
	/// <summary>Nothing; the column occupies its cells as usual.</summary>
	None,

	/// <summary>The column's cells are absorbed by the column on its left, which widens in place.</summary>
	GiveSpaceToLeft,

	/// <summary>The column's cells are absorbed by the column on its right, which widens in place.</summary>
	GiveSpaceToRight,

	/// <summary>The column on the right is drawn at this column's position instead of its own.</summary>
	PullRightColumnLeft,

	/// <summary>The column on the left is drawn at this column's position instead of its own.</summary>
	PushLeftColumnRight,
}

/// <summary>Whether a separator is drawn on continuation rows or only on the first.</summary>
public enum SeparatorRows
{
	/// <summary>Drawn on every row.</summary>
	EveryRow,

	/// <summary>Drawn on the first row; later rows get blanks of the same width.</summary>
	FirstRowOnly,
}
