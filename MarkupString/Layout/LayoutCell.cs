namespace MarkupString.Layout;

/// <summary>One item in a layout: a column of text, or the literal that separates two columns.</summary>
/// <remarks>
/// Modelling separators as cells rather than as one layout-wide setting covers both servers with
/// the same shape. PennMUSH's <c>align()</c> passes the same separator between every column;
/// RhostMUSH's <c>printf()</c> passes whatever literal text stood between its fields.
/// </remarks>
public abstract record LayoutCell;

/// <summary>A column: its content, and how that content is shaped, drawn and assembled.</summary>
public sealed record LayoutColumn(MarkupText Content, ColumnFormat Format) : LayoutCell;

/// <summary>
/// Literal text between two columns, drawn on every row or only on the first.
/// </summary>
/// <remarks>
/// The two servers disagree here. PennMUSH inserts its separator "between every column, on every
/// row". RhostMUSH does not: its continuation rows emit spaces the width of the literal that
/// preceded each field, not the literal itself.
/// </remarks>
public sealed record LayoutSeparator(MarkupText Text, SeparatorRows Rows = SeparatorRows.EveryRow) : LayoutCell;

/// <summary>Settings that belong to a layout rather than to any one column.</summary>
public sealed record LayoutOptions
{
	/// <summary>What joins rows in <see cref="TextLayout.Render"/>. A newline by default.</summary>
	public MarkupText RowSeparator { get; init; } = MarkupText.NewLine;
}
