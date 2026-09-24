namespace MarkupString;

/// <summary>
/// What an emitter is told about the run it is emitting. <see cref="Previous"/> and
/// <see cref="Next"/> are the markup sets of the immediately adjacent runs, so a stateful emitter
/// (SGR, say) can diff against them; either is <see langword="null"/> when a plain-text gap or the
/// end of the text sits there instead.
/// </summary>
public readonly ref struct EmitContext
{
	/// <summary>The format being rendered.</summary>
	public required MarkupFormat Format { get; init; }

	/// <summary>The registry the render is running against — how a set emitter delegates layers it does not own.</summary>
	public required MarkupRegistry Registry { get; init; }

	/// <summary>This run's own markups, innermost first.</summary>
	public required MarkupSet Markups { get; init; }

	/// <summary>The previous run's markups, or <see langword="null"/> when a gap or the start of the text precedes this run.</summary>
	public MarkupSet? Previous { get; init; }

	/// <summary>The next run's markups, or <see langword="null"/> when a gap or the end of the text follows this run.</summary>
	public MarkupSet? Next { get; init; }

	/// <summary>Whether this is the first run of the text.</summary>
	public bool IsFirstRun { get; init; }

	/// <summary>Whether this is the last run of the text.</summary>
	public bool IsLastRun { get; init; }

	/// <summary>
	/// Whether this run begins the stretch <paramref name="markup"/> covers — false when the run before
	/// it carries the same layer under the same enclosing ones, and so has already opened whatever wraps
	/// it.
	/// </summary>
	/// <remarks>
	/// A markup wrapping text that carries markup of its own covers several runs: styling inside a
	/// preformatted region, a name inside a pane. An emitter that wrote its tag for each of them would
	/// produce a string of <c>&lt;pre&gt;</c>s or open and close a pane around every word. Asking this and
	/// <see cref="EndsRegion"/> writes one, around the lot.
	/// </remarks>
	public bool StartsRegion(IMarkup markup) => !Continues(Previous, markup);

	/// <summary>Whether this run ends the stretch <paramref name="markup"/> covers. See <see cref="StartsRegion"/>.</summary>
	public bool EndsRegion(IMarkup markup) => !Continues(Next, markup);

	/// <summary>
	/// Whether an adjacent run carries this layer under the same enclosing ones, and so is the same
	/// stretch of text.
	/// </summary>
	/// <remarks>
	/// <para>Equality, not identity: two adjacent regions that are the same thing — the same pane, the
	/// same preformatting — are one region, and a region whose neighbour differs in any way opens its
	/// own.</para>
	/// <para>Everything from this layer outward has to match, or a region would outlive what encloses
	/// it: a variable carried across a pane boundary would be closed after the pane was, which is
	/// elements that cross rather than nest.</para>
	/// </remarks>
	private bool Continues(MarkupSet? adjacent, IMarkup markup)
	{
		if (adjacent is null) return false;

		var here = IndexOf(Markups, markup);
		var there = IndexOf(adjacent, markup);
		if (here < 0 || there < 0 || Markups.Count - here != adjacent.Count - there) return false;

		for (var i = 0; here + i < Markups.Count; i++)
			if (!Equals(Markups[here + i], adjacent[there + i]))
				return false;

		return true;
	}

	private static int IndexOf(MarkupSet markups, IMarkup markup)
	{
		for (var i = 0; i < markups.Count; i++)
			if (Equals(markups[i], markup))
				return i;

		return -1;
	}
}