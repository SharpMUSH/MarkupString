using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using MarkupString.Layout;

// The DisplayWidth instance property below hides the type of the same name, so operations
// reach the measuring helpers through this alias.
using Cells = MarkupString.DisplayWidth;

namespace MarkupString;

/// <summary>
/// One replacement inside a <see cref="MarkupText.Splice"/> batch: the half-open range
/// <c>[Start, Start + Length)</c> of the target is replaced by <see cref="Replacement"/>.
/// </summary>
public readonly record struct Edit(int Start, int Length, MarkupText Replacement);

public sealed partial class MarkupText
{
	private int _displayWidth = -1;

	/// <summary>
	/// Width of the text in terminal cells: East Asian wide and fullwidth characters count two,
	/// combining marks and controls count zero. Unlike <see cref="Length"/> this is what a
	/// column layout has to align on.
	/// </summary>
	public int DisplayWidth => _displayWidth >= 0 ? _displayWidth : _displayWidth = Cells.Of(Text);

	/// <summary>
	/// <see cref="DisplayWidth"/>, with each C0 or C1 control character counted as
	/// <paramref name="controls"/> says: under <see cref="ControlCharacterWidth.One"/> a tab or a
	/// newline is one cell, as a character count such as a MUSH's <c>strlen</c> wants it.
	/// </summary>
	public int GetDisplayWidth(ControlCharacterWidth controls)
		=> controls == ControlCharacterWidth.Zero ? DisplayWidth : Cells.Of(Text, controls);

	/// <summary>
	/// The remainder of the text from <paramref name="start"/>, whose index snaps back to the start
	/// of the cluster it lands in.
	/// </summary>
	public MarkupText Substring(int start) => Substring(start, Length);

	/// <summary>
	/// The text from <paramref name="start"/> for at most <paramref name="length"/> code units,
	/// clamped to the text.
	/// </summary>
	/// <remarks>
	/// This is an extraction, so both ends snap <em>inward</em> to a grapheme cluster boundary:
	/// <paramref name="start"/> moves back to the start of the cluster it lands in, and — unless the
	/// requested range reaches the end of the text, in which case the end always stays at
	/// <see cref="Length"/> — the end, measured as <c>snapped start + <paramref name="length"/></c>
	/// so the count is always taken from where the result actually begins, moves back as well. The
	/// result therefore never splits a cluster: for an interior range it never exceeds
	/// <paramref name="length"/> code units and may be shorter, and is empty when the whole
	/// requested window sits inside one cluster; a range that reaches the end instead keeps the
	/// whole tail from the snapped start, which may exceed <paramref name="length"/> by the start's
	/// snap-back distance. Edits (<see cref="Splice"/> and the operations built on it) snap outward
	/// instead.
	/// </remarks>
	public MarkupText Substring(int start, int length)
	{
		if (length <= 0 || start >= Length) return Empty;
		var clampedStart = Math.Max(0, start);
		var from = Graphemes.SnapStart(Text, clampedStart);
		// Whether the caller's own range reaches the end decides the shortcut - deciding it from
		// the post-snap `from` instead would let an inward start-snap swallow code units off the
		// tail of "take the rest" calls like x.Substring(n, x.Length - n).
		var to = length >= Length - clampedStart ? Length : Graphemes.SnapStart(Text, from + length);
		if (to <= from) return Empty;
		return ExtractRange(from, to);
	}

	// Call only with known cluster boundaries; avoids re-scanning long clusters in enumeration.
	private MarkupText ExtractRange(int from, int to)
	{
		if (from == 0 && to == Length) return this;
		// Text with no runs has none to clip: no builder to fill, and an empty array to share.
		if (Runs.IsDefaultOrEmpty) return new MarkupText(Text[from..to], ImmutableArray<Run>.Empty);

		// Runs are sorted and disjoint, so the ones overlapping [from, to) are a contiguous slice:
		// size the result exactly instead of growing a default builder and copying it out.
		var first = FirstRunIndexAt(from);
		if (Runs[first].End <= from) first++;
		var last = first;
		while (last < Runs.Length && Runs[last].Start < to) last++;
		if (last == first) return new MarkupText(Text[from..to], ImmutableArray<Run>.Empty);

		var clipped = new Run[last - first];
		for (var i = first; i < last; i++)
		{
			var run = Runs[i];
			var start = Math.Max(run.Start, from);
			clipped[i - first] = new Run(start - from, Math.Min(run.End, to) - start, run.Markups);
		}
		return new MarkupText(Text[from..to], ImmutableCollectionsMarshal.AsImmutableArray(clipped));
	}

	/// <summary>
	/// Breaks this text into lines of at most <paramref name="width"/> display cells, at the last
	/// space that fits.
	/// </summary>
	public MarkupText[] WrapLines(int width) => WrapLines(width, WrapMode.Word);

	/// <summary>
	/// Breaks this text into lines of at most <paramref name="width"/> display cells.
	/// </summary>
	/// <remarks>
	/// Empty text yields no lines, as <see cref="Split(string)"/> does; a
	/// <paramref name="width"/> of zero or less yields this text unbroken. For anything beyond
	/// the width and the mode — an indent, a line budget, the RhostMUSH rule for the space a
	/// break lands on — build a <see cref="ColumnFormat"/> and call <see cref="Shape"/>.
	/// </remarks>
	public MarkupText[] WrapLines(int width, WrapMode mode)
	{
		if (Length == 0) return [];
		if (width <= 0) return [this];

		var shaped = Shape(new ColumnFormat { Width = width, Wrap = mode });
		var lines = new MarkupText[shaped.Length];
		for (var i = 0; i < shaped.Length; i++) lines[i] = shaped[i].Text;
		return lines;
	}

	/// <summary>
	/// Shapes this text and draws it as a column: every line exactly
	/// <see cref="ColumnFormat.Width"/> display cells wide, filled and aligned.
	/// </summary>
	/// <remarks>
	/// A line runs wider than the column only under <see cref="TruncationType.Overflow"/>, or
	/// where a single grapheme cluster is wider than the column and had to go somewhere.
	/// </remarks>
	public MarkupText[] FormatColumn(ColumnFormat format) =>
		ColumnRenderer.Render(Shape(format), format);

	/// <summary>
	/// Shapes this text into the lines of a column: tabs expanded, cut to the cell budget, then
	/// wrapped with the indent applied and stopped at the line budget.
	/// </summary>
	/// <remarks>
	/// Always yields at least one line, empty text included, because a column occupies a row
	/// whether or not it has anything to say.
	/// </remarks>
	public ImmutableArray<TextLine> Shape(ColumnFormat format)
	{
		ArgumentNullException.ThrowIfNull(format);

		var source = ExpandTabs(format.TabWidth);
		if (format.MaxCells > 0) source = source.TruncateToWidth(format.MaxCells, CutFrom.End);

		var spans = LineWrapper.Break(source.Text, format);
		var lines = ImmutableArray.CreateBuilder<TextLine>(spans.Count);
		foreach (var span in spans)
			lines.Add(new TextLine(
				source.Substring(span.Start, span.End - span.Start),
				span.Indent,
				span.Width,
				span.EndsParagraph));
		return lines.ToImmutable();
	}

	/// <summary>
	/// Replaces every tab with <paramref name="tabWidth"/> spaces. This is literal substitution,
	/// not alignment to tab stops, which is what the servers this mirrors do: four spaces per tab
	/// turns <c>a\tb</c> into <c>a    b</c>, not into a tab stop at column four.
	/// </summary>
	/// <remarks>A <paramref name="tabWidth"/> of zero or less leaves the text alone.</remarks>
	public MarkupText ExpandTabs(int tabWidth) =>
		tabWidth <= 0 || !Text.Contains('\t') ? this : ReplaceAll("\t", Plain(new string(' ', tabWidth)));

	/// <summary>
	/// The widest prefix or suffix of this text that fits in <paramref name="cells"/> display
	/// cells, cut on a grapheme cluster boundary.
	/// </summary>
	/// <remarks>
	/// Text already within the budget is returned unchanged. Because the cut lands on a cluster
	/// boundary the result may be narrower than <paramref name="cells"/>, and is empty when the
	/// first cluster alone is wider than the budget.
	/// </remarks>
	public MarkupText TruncateToWidth(int cells, CutFrom from)
	{
		if (cells <= 0 || Length == 0) return Empty;
		if (DisplayWidth <= cells) return this;
		return from == CutFrom.End
			? Substring(0, Cells.IndexAtWidth(Text, cells))
			: Substring(Cells.IndexFromWidthEnd(Text, cells));
	}

	/// <summary>Splits on the plain text of <paramref name="delimiter"/>; its markup is ignored.</summary>
	public MarkupText[] Split(MarkupText delimiter) => Split(delimiter.Text);

	/// <summary>
	/// Splits on the plain text of <paramref name="delimiter"/>, its markup ignored, as
	/// <see cref="Split(string, StringSplitOptions)"/> does.
	/// </summary>
	public MarkupText[] Split(MarkupText delimiter, StringSplitOptions options) => Split(delimiter.Text, options);

	/// <summary>
	/// Splits on every non-overlapping ordinal occurrence of <paramref name="delimiter"/>. An
	/// empty text yields no segments; an empty delimiter yields this text unsplit.
	/// </summary>
	public MarkupText[] Split(string delimiter) => Split(delimiter, StringSplitOptions.None);

	/// <summary>
	/// Splits on every non-overlapping ordinal occurrence of <paramref name="delimiter"/>, as
	/// <see cref="Split(string)"/> does, then applies <paramref name="options"/> to each segment in
	/// the same pass: <see cref="StringSplitOptions.TrimEntries"/> drops white space
	/// (<see cref="char.IsWhiteSpace(char)"/>) from both ends of a segment, and
	/// <see cref="StringSplitOptions.RemoveEmptyEntries"/> leaves out segments that are empty, after
	/// trimming when both are given. Splitting a list on spaces with
	/// <see cref="StringSplitOptions.RemoveEmptyEntries"/> gives its words without filtering a second
	/// array.
	/// </summary>
	/// <exception cref="ArgumentException"><paramref name="options"/> holds an undefined flag.</exception>
	public MarkupText[] Split(string delimiter, StringSplitOptions options)
	{
		ArgumentNullException.ThrowIfNull(delimiter);
		const StringSplitOptions all = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;
		if ((options & ~all) != 0) throw new ArgumentException($"Undefined flags: {options & ~all}.", nameof(options));
		var removeEmpty = (options & StringSplitOptions.RemoveEmptyEntries) != 0;
		var trim = (options & StringSplitOptions.TrimEntries) != 0;

		if (Length == 0) return [];
		var text = Text;
		// Counting first sizes the result exactly, so there is no list of positions to grow and copy.
		var count = delimiter.Length == 0 ? 0 : text.AsSpan().Count(delimiter.AsSpan());
		if (count == 0 && !trim) return [this];

		var segments = new MarkupText[count + 1];
		var kept = 0;
		var cursor = 0;
		for (var i = 0; i <= count; i++)
		{
			var found = i < count ? text.IndexOf(delimiter, cursor, StringComparison.Ordinal) : text.Length;
			var start = cursor;
			var end = found;
			if (trim)
			{
				while (start < end && char.IsWhiteSpace(text[start])) start++;
				while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
			}
			// The last segment runs to the end of the text, so take it as Substring(start) does.
			var segment = start == end ? Empty : end == text.Length ? Substring(start) : Substring(start, end - start);
			if (!removeEmpty || segment.Length > 0) segments[kept++] = segment;
			cursor = found + delimiter.Length;
		}
		return kept == segments.Length ? segments : segments[..kept];
	}

	/// <summary>Removes leading and/or trailing spaces from the requested end(s).</summary>
	public MarkupText Trim(TrimType type) => Trim(type, " ");

	/// <summary>Trims the plain text of <paramref name="chars"/> off the requested end(s).</summary>
	public MarkupText Trim(TrimType type, MarkupText chars) => Trim(type, chars.Text);

	/// <summary>
	/// Removes any leading and/or trailing characters that appear in <paramref name="chars"/>.
	/// </summary>
	public MarkupText Trim(TrimType type, string chars)
	{
		ArgumentNullException.ThrowIfNull(chars);
		if (Length == 0 || chars.Length == 0) return this;

		var start = 0;
		var end = Length;
		// CharAt rather than Text: trimming an accumulator that has nothing to trim must not join it.
		if (type is TrimType.TrimStart or TrimType.TrimBoth)
			while (start < end && chars.Contains(CharAt(start))) start++;
		if (type is TrimType.TrimEnd or TrimType.TrimBoth)
			while (end > start && chars.Contains(CharAt(end - 1))) end--;

		return start == 0 && end == Length ? this : Substring(start, end - start);
	}

	/// <summary>
	/// Brings the text to <paramref name="width"/> display cells by adding <paramref name="fill"/>.
	/// Text already at or beyond the width is either cut on a cluster boundary
	/// (<see cref="TruncationType.Truncate"/>) or returned unchanged
	/// (<see cref="TruncationType.Overflow"/>).
	/// </summary>
	/// <remarks>
	/// The result is exactly <paramref name="width"/> display cells wide unless
	/// <see cref="TruncationType.Overflow"/> keeps text that is already wider. A multi-cell
	/// <paramref name="fill"/> is a pattern indexed by position in the result rather than
	/// something restarted where the text stops, so it reads as one unbroken run behind the
	/// text; <see cref="Layout.FillPhase.Restart"/> on a <see cref="ColumnFormat"/> restores the
	/// older behaviour. Cells the fill cannot express, such as the single cell left over by a
	/// two-cell fill, are taken by spaces.
	/// </remarks>
	public MarkupText Pad(MarkupText fill, int width, PadType type, TruncationType truncation)
	{
		ArgumentNullException.ThrowIfNull(fill);
		return AsColumn(fill, null, width, Aligned(type), truncation);
	}

	/// <summary>
	/// Centres the text in <paramref name="width"/> display cells between two different fills,
	/// the odd cell going to the right.
	/// </summary>
	/// <remarks>
	/// The result is exactly <paramref name="width"/> display cells wide unless
	/// <see cref="TruncationType.Overflow"/> keeps text that is already wider. Cells neither fill
	/// can express are taken by spaces.
	/// </remarks>
	public MarkupText Center(MarkupText fillLeft, MarkupText fillRight, int width, TruncationType truncation)
	{
		ArgumentNullException.ThrowIfNull(fillLeft);
		ArgumentNullException.ThrowIfNull(fillRight);
		return AsColumn(fillLeft, fillRight, width, Alignment.Center, truncation);
	}

	/// <summary>Draws this text as a single-line column of <paramref name="width"/> cells.</summary>
	private MarkupText AsColumn(
		MarkupText fill, MarkupText? fillRight, int width, Alignment alignment, TruncationType truncation) =>
		FormatColumn(new ColumnFormat
		{
			Width = width,
			Alignment = alignment,
			Fill = fill,
			FillRight = fillRight,
			Truncation = truncation,
		})[0];

	/// <summary>
	/// Where the fill goes, expressed as where the text goes. <see cref="PadType"/> names the
	/// side the fill lands on; <see cref="Alignment"/> names the side the text lands on, so the
	/// two are mirrored.
	/// </summary>
	private static Alignment Aligned(PadType type) => type switch
	{
		PadType.Left => Alignment.Right,
		PadType.Right => Alignment.Left,
		PadType.Center => Alignment.Center,
		PadType.Full => Alignment.Full,
		_ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported pad type."),
	};

	/// <summary>This text repeated <paramref name="count"/> times.</summary>
	public MarkupText Repeat(int count)
	{
		if (count <= 0 || Length == 0) return Empty;
		if (count == 1) return this;
		var text = Length == 1
			? new string(Text[0], count)
			: string.Create(checked(Length * count), Text, static (span, unit) =>
			{
				// Each copy doubles what is written, so n copies take log n block copies.
				unit.AsSpan().CopyTo(span);
				for (var filled = unit.Length; filled < span.Length; filled *= 2)
					span[..Math.Min(filled, span.Length - filled)].CopyTo(span[filled..]);
			});
		if (Runs.IsDefaultOrEmpty) return new MarkupText(text, ImmutableArray<Run>.Empty);
		// One style over the whole text: the copies would coalesce into one run anyway. A point
		// marks a single carrier, so its copies stay apart.
		if (Runs is [{ Start: 0 } whole] && whole.Length == Length && !HasPoint(whole.Markups))
			return new MarkupText(text, [new Run(0, text.Length, whole.Markups)]);

		var runs = ImmutableArray.CreateBuilder<Run>(Runs.Length * count);
		for (var copy = 0; copy < count; copy++)
		{
			var offset = copy * Length;
			foreach (var run in Runs) runs.Add(run with { Start = run.Start + offset });
		}
		return new MarkupText(text, runs.MoveToImmutable());
	}

	/// <summary>Removes <paramref name="length"/> code units from <paramref name="index"/>.</summary>
	public MarkupText Remove(int index, int length)
	{
		if (length <= 0 || index >= Length) return this;
		var start = Math.Max(0, index);
		var count = Math.Min(length, Length - start);
		return count <= 0 ? this : Splice([new Edit(start, count, Empty)]);
	}

	/// <summary>
	/// Replaces <paramref name="length"/> code units at <paramref name="index"/>. Unlike
	/// <see cref="Insert"/> the replacement never inherits surrounding markup.
	/// </summary>
	public MarkupText Replace(int index, int length, MarkupText replacement)
	{
		ArgumentNullException.ThrowIfNull(replacement);
		if (index >= Length) return Concat(this, replacement);
		if (index < 0) return Concat(replacement, this);
		return Splice([new Edit(index, Math.Clamp(length, 0, Length - index), replacement)]);
	}

	/// <summary>
	/// Inserts at <paramref name="index"/>. An insertion strictly inside a run inherits that
	/// run's markup as its outer layers, so styled text stays unbroken.
	/// </summary>
	public MarkupText Insert(int index, MarkupText insert)
	{
		ArgumentNullException.ThrowIfNull(insert);
		if (insert.Length == 0) return this;
		if (index <= 0) return Concat(insert, this);
		if (index >= Length) return Concat(this, insert);

		var enclosing = EnclosingMarkups(index);
		return Splice([new Edit(index, 0, enclosing is null ? insert : WrapWith(insert, enclosing))]);
	}

	/// <summary>
	/// Applies every edit in one pass. Edits must be sorted by <see cref="Edit.Start"/> and must
	/// not overlap.
	/// </summary>
	/// <remarks>
	/// These are edits, not extractions, so each range snaps <em>outward</em> to grapheme cluster
	/// boundaries — the start back to the start of its cluster, the end forward to the end of
	/// its — and a replaced range therefore never leaves half a cluster behind. (A zero-length
	/// edit, an insertion, has no end to push and simply lands on the start of the cluster it
	/// falls in.) Where snapping makes one range swallow the next, the later edit applies to what
	/// is left of its range. <see cref="Substring(int, int)"/> and the other extractions snap inward
	/// instead.
	/// </remarks>
	/// <exception cref="ArgumentException">The edits are unsorted or overlapping.</exception>
	/// <exception cref="ArgumentOutOfRangeException">An edit falls outside the text.</exception>
	public MarkupText Splice(ReadOnlySpan<Edit> edits)
	{
		if (edits.Length == 0) return this;

		var source = Text;
		int[]? rented = null;
		var ranges = edits.Length <= 64
			? stackalloc int[edits.Length * 2]
			: (rented = ArrayPool<int>.Shared.Rent(edits.Length * 2)).AsSpan(0, edits.Length * 2);
		try
		{
			// Pass 1: check, snap each range outward to cluster boundaries, and size the result, so the
			// text is written once at its final length instead of through a growing builder.
			var position = 0;
			var previousEnd = 0;
			var length = 0;
			var runCount = Runs.Length + edits.Length;
			for (var i = 0; i < edits.Length; i++)
			{
				var edit = edits[i];
				if (edit.Start < 0 || edit.Start > Length)
					throw new ArgumentOutOfRangeException(nameof(edits), edit.Start, "Edit starts outside the text.");
				if (edit.Length < 0 || edit.Length > Length - edit.Start)
					throw new ArgumentOutOfRangeException(nameof(edits), edit.Length, "Edit ends outside the text.");
				if (edit.Start < previousEnd)
					throw new ArgumentException("Edits must be sorted by start and must not overlap.", nameof(edits));
				previousEnd = edit.Start + edit.Length;

				var start = Math.Max(position, Graphemes.SnapStart(source, edit.Start));
				var end = edit.Length == 0 ? start : Math.Max(start, Graphemes.SnapEnd(source, edit.Start + edit.Length));
				ranges[i * 2] = start;
				ranges[i * 2 + 1] = end;

				var replacement = edit.Replacement ?? Empty;
				length = checked(length + (start - position) + replacement.Length);
				runCount += replacement.Runs.Length;
				position = end;
			}
			length = checked(length + (Length - position));

			// Pass 2: the text.
			var text = string.Create(length, new SpliceState(source, edits, ranges), static (span, state) =>
			{
				var from = 0;
				var written = 0;
				for (var i = 0; i < state.Edits.Length; i++)
				{
					var start = state.Ranges[i * 2];
					state.Source.AsSpan(from, start - from).CopyTo(span[written..]);
					written += start - from;
					var replacement = (state.Edits[i].Replacement ?? Empty).Text;
					replacement.AsSpan().CopyTo(span[written..]);
					written += replacement.Length;
					from = state.Ranges[i * 2 + 1];
				}
				state.Source.AsSpan(from).CopyTo(span[written..]);
			});

			// Pass 3: the runs, unless neither side has any.
			if (Runs.IsDefaultOrEmpty && runCount == edits.Length) return new MarkupText(text, ImmutableArray<Run>.Empty);

			var runs = ImmutableArray.CreateBuilder<Run>(runCount);
			position = 0;
			var output = 0;
			for (var i = 0; i < edits.Length; i++)
			{
				var start = ranges[i * 2];
				ClipInto(position, start, output - position, runs);
				output += start - position;
				var replacement = edits[i].Replacement ?? Empty;
				foreach (var run in replacement.Runs) runs.Add(run with { Start = run.Start + output });
				output += replacement.Length;
				position = ranges[i * 2 + 1];
			}
			ClipInto(position, Length, output - position, runs);
			return new MarkupText(text, runs.Count == runs.Capacity ? runs.MoveToImmutable() : runs.ToImmutable());
		}
		finally
		{
			if (rented is not null) ArrayPool<int>.Shared.Return(rented);
		}
	}

	private readonly ref struct SpliceState(string source, ReadOnlySpan<Edit> edits, ReadOnlySpan<int> ranges)
	{
		public string Source { get; } = source;
		public ReadOnlySpan<Edit> Edits { get; } = edits;
		public ReadOnlySpan<int> Ranges { get; } = ranges;
	}

	/// <summary>
	/// Replaces every non-overlapping ordinal occurrence of <paramref name="search"/> in one pass,
	/// leaving the markup on the text between matches untouched.
	/// </summary>
	public MarkupText ReplaceAll(string search, MarkupText replacement)
	{
		ArgumentNullException.ThrowIfNull(search);
		ArgumentNullException.ThrowIfNull(replacement);
		if (search.Length == 0 || Length == 0) return this;

		List<Edit>? edits = null;
		var position = 0;
		while (position <= Length - search.Length)
		{
			var found = Text.IndexOf(search, position, StringComparison.Ordinal);
			if (found < 0) break;
			(edits ??= []).Add(new Edit(found, search.Length, replacement));
			position = found + search.Length;
		}
		return edits is null ? this : Splice(CollectionsMarshal.AsSpan(edits));
	}

	/// <summary>Ordinal index of the first occurrence of <paramref name="search"/>, or -1.</summary>
	public int IndexOf(string search) => Text.IndexOf(search, StringComparison.Ordinal);

	/// <summary>Ordinal index of the last occurrence of <paramref name="search"/>, or -1.</summary>
	public int LastIndexOf(string search) => Text.LastIndexOf(search, StringComparison.Ordinal);

	/// <summary>Ordinal indexes of every non-overlapping occurrence of <paramref name="search"/>.</summary>
	public IEnumerable<int> IndexesOf(string search)
	{
		if (search.Length == 0) yield break;
		var position = 0;
		while (position <= Length - search.Length)
		{
			var found = Text.IndexOf(search, position, StringComparison.Ordinal);
			if (found < 0) yield break;
			yield return found;
			position = found + search.Length;
		}
	}

	/// <summary>
	/// Transforms the whole plain text. A transform that keeps the length keeps the runs; any
	/// other result is plain, because the run positions no longer mean anything.
	/// </summary>
	public MarkupText Apply(Func<string, string> transform)
	{
		ArgumentNullException.ThrowIfNull(transform);
		var text = transform(Text);
		return text.Length == Length ? new MarkupText(text, Runs) : Plain(text);
	}

	/// <summary>
	/// Transforms each styled run and each plain gap separately, then concatenates the results.
	/// </summary>
	public MarkupText Map(Func<MarkupText, MarkupText> transform)
	{
		ArgumentNullException.ThrowIfNull(transform);
		if (Length == 0) return this;

		var segments = new List<MarkupText>(Runs.Length * 2 + 1);
		var position = 0;
		foreach (var run in Runs)
		{
			if (run.Start > position) segments.Add(transform(Substring(position, run.Start - position)));
			segments.Add(transform(Substring(run.Start, run.Length)));
			position = run.End;
		}
		if (position < Length) segments.Add(transform(Substring(position)));
		return Concat(CollectionsMarshal.AsSpan(segments));
	}

	/// <summary>
	/// Appends <paramref name="tail"/>, extending the outermost markup of a run that reaches the
	/// end of this text over it. Text that ends plain gets a plain concatenation.
	/// </summary>
	public MarkupText AttachTail(MarkupText tail)
	{
		ArgumentNullException.ThrowIfNull(tail);
		if (tail.Length == 0) return this;
		if (Runs.Length == 0 || Runs[^1].End != Length) return Concat(this, tail);
		return Concat(this, Wrap(Runs[^1].Markups.Outermost, tail));
	}

	/// <summary>The markup of the run that strictly contains <paramref name="index"/>, if any.</summary>
	private MarkupSet? EnclosingMarkups(int index)
	{
		var i = FirstRunIndexAt(index);
		if (i >= Runs.Length) return null;
		var run = Runs[i];
		return run.Start < index && run.End > index ? run.Markups : null;
	}

	/// <summary>
	/// Copies the runs overlapping <c>[from, to)</c> into <paramref name="builder"/>, clipped to
	/// that range and shifted by <paramref name="offset"/>.
	/// </summary>
	private void ClipInto(int from, int to, int offset, ImmutableArray<Run>.Builder builder)
	{
		if (to <= from) return;
		for (var i = FirstRunIndexAt(from); i < Runs.Length; i++)
		{
			var run = Runs[i];
			if (run.Start >= to) break;
			var start = Math.Max(run.Start, from);
			var end = Math.Min(run.End, to);
			if (end > start) builder.Add(new Run(start + offset, end - start, run.Markups));
		}
	}

	/// <summary>
	/// Index of the last run starting at or before <paramref name="position"/>, or 0 — the first
	/// index a scan for runs overlapping <paramref name="position"/> has to start from.
	/// </summary>
	private int FirstRunIndexAt(int position)
	{
		var low = 0;
		var high = Runs.Length - 1;
		var result = 0;
		while (low <= high)
		{
			var mid = (low + high) >> 1;
			if (Runs[mid].Start <= position)
			{
				result = mid;
				low = mid + 1;
			}
			else
			{
				high = mid - 1;
			}
		}
		return result;
	}

	private static bool HasPoint(MarkupSet markups)
	{
		foreach (var markup in markups.Items)
			if (markup is IPointMarkup) return true;
		return false;
	}

	/// <summary>Layers <paramref name="outer"/> over every run and gap of <paramref name="inner"/>.</summary>
	private static MarkupText WrapWith(MarkupText inner, MarkupSet outer)
	{
		var builder = ImmutableArray.CreateBuilder<Run>(inner.Runs.Length * 2 + 1);
		var position = 0;
		foreach (var run in inner.Runs)
		{
			if (run.Start > position) builder.Add(new Run(position, run.Start - position, outer));
			builder.Add(new Run(run.Start, run.Length, Layer(run.Markups, outer)));
			position = run.End;
		}
		if (position < inner.Length) builder.Add(new Run(position, inner.Length - position, outer));
		return new MarkupText(inner.Text, builder.ToImmutable());
	}

	private static MarkupSet Layer(MarkupSet inner, MarkupSet outer)
	{
		var result = inner;
		foreach (var markup in outer.Items) result = result.Append(markup);
		return result;
	}
}
