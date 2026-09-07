using Cells = MarkupString.DisplayWidth;

namespace MarkupString.Layout;

/// <summary>
/// A column's fill, treated as a pattern indexed by cell position rather than as something
/// repeated from wherever the text happens to stop.
/// </summary>
/// <remarks>
/// This is what makes a multi-cell filler look right behind text. RhostMUSH's
/// <c>$-40:0123456789:s</c> on a fifteen-cell value emits
/// <c>ten char filler5678901234567890123456789</c>: the pattern resumes at 5 because the text
/// consumed cells 0 to 14, rather than restarting at 0. Sampling by absolute position gives
/// that for every alignment, and shows the pattern through the widened gaps of a fully
/// justified line as well.
/// </remarks>
internal static class FillPattern
{
	/// <summary>
	/// The <paramref name="cells"/> columns of <paramref name="pattern"/> that belong at cell
	/// <paramref name="fromCell"/> of the column.
	/// </summary>
	/// <remarks>
	/// Cells the pattern cannot express — the one left over by a two-cell unit, or all of them
	/// when the pattern has no width at all — take spaces, so the width always holds.
	/// </remarks>
	internal static MarkupText Slice(MarkupText pattern, int fromCell, int cells, FillPhase phase)
	{
		if (cells <= 0) return MarkupText.Empty;

		var unit = pattern.DisplayWidth;
		if (unit <= 0) return MarkupText.Space.Repeat(cells);

		var offset = phase == FillPhase.Continuous ? fromCell % unit : 0;
		var repeated = pattern.Repeat((offset + cells) / unit + 1);
		var start = Cells.IndexAtWidth(repeated.Text, offset);
		var slice = repeated.Substring(start, Cells.IndexAtWidth(repeated.Text, offset + cells) - start);

		var residue = cells - slice.DisplayWidth;
		return residue <= 0 ? slice : MarkupText.Concat(slice, MarkupText.Space.Repeat(residue));
	}
}
