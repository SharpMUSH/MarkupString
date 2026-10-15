using System.Buffers;
namespace MarkupString.Ansi;

/// <summary>
/// Renders a run as GitHub-flavoured Markdown: bold as <c>**</c>, italic as <c>*</c>, strikethrough as
/// <c>~~</c>, and a URL link as <c>[text](address)</c>. Markdown has no colour, underline, faint, blink,
/// overline or reverse video, so those are dropped and the text kept; so is a command link, which a page
/// has nowhere to send.
/// </summary>
/// <remarks>
/// <para>Markdown spells nesting with delimiters that have to pair up, so emphasis that carries on from one
/// run into the next is written once round both — <c>**a *b***</c>, not <c>**a*****b***</c> — the way the
/// ANSI emitter writes only what changed. A run decides that from its neighbours alone
/// (<see cref="EmitContext.Previous"/> and <see cref="EmitContext.Next"/>), the two sides agree on what stays
/// open, and a run next to a point, a block or a layer drawn by another package's emitter closes everything
/// it opened.</para>
/// <para>A delimiter is kept off the whitespace at either end of what it covers, where Markdown would not
/// read it as one, and is closed before a line ending and opened again after it, since emphasis cannot cross
/// a blank line. A run of nothing but whitespace is not wrapped at all.</para>
/// </remarks>
public sealed class AnsiMarkdownEmitter : IMarkupSetEmitter
{
	/// <inheritdoc/>
	public MarkupFormat Format => MarkupFormat.Markdown;

	/// <inheritdoc/>
	public bool TryEmit(MarkupSet set, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(set);
		ArgumentNullException.ThrowIfNull(output);

		if (StandsAlone(context.Markups, context) || AnsiEmitterSupport.HasDelegatedLayer(set, context))
		{
			AnsiEmitterSupport.EmitSegmented(set, body, context, output, WriteAlone);
			return true;
		}

		var own = Delimiters(AnsiEmitterSupport.Fold(set, context.Format));
		WriteDelimited(own, Shared(own, context.Previous, context), Shared(own, context.Next, context), body, output);
		return true;
	}

	/// <summary>One delimiter pair: what opens a span and what closes it.</summary>
	private readonly record struct Delimiter(string Open, string Close);

	private static readonly Delimiter Strike = new("~~", "~~");
	private static readonly Delimiter Strong = new("**", "**");
	private static readonly Delimiter Emphasis = new("*", "*");

	/// <summary>
	/// The delimiters <paramref name="style"/> is written with, outermost first, always in the same order —
	/// a link, then strikethrough, bold and italic — so two runs that share some of them share the outermost.
	/// </summary>
	private static List<Delimiter> Delimiters(in AnsiStyle style)
	{
		var delimiters = new List<Delimiter>(4);
		if (style.LinkKind == LinkKind.Url
			&& style.LinkUrl is { Length: > 0 } url
			&& UrlSafety.IsSafeNavigableUrl(url))
			delimiters.Add(new Delimiter("[", "](" + MarkdownSyntax.Destination(url) + ")"));
		if (style.StrikeThrough) delimiters.Add(Strike);
		if (style.Bold) delimiters.Add(Strong);
		if (style.Italic) delimiters.Add(Emphasis);
		return delimiters;
	}

	/// <summary>
	/// Whether a run is written with nothing left open on either side: one that carries a point (written by
	/// its own emitter, or not at all), a block (drawn whole, or not as runs of this emitter's), or a layer
	/// another package writes between the stretches of styling.
	/// </summary>
	private static bool StandsAlone(MarkupSet markups, in EmitContext context)
	{
		foreach (var markup in markups)
			if (markup is IPointMarkup or IBlockMarkup) return true;
		return AnsiEmitterSupport.HasDelegatedLayer(markups, context);
	}

	/// <summary>How many of <paramref name="own"/>'s outermost delimiters stay open into, or from, <paramref name="neighbour"/>.</summary>
	private static int Shared(List<Delimiter> own, MarkupSet? neighbour, in EmitContext context)
	{
		if (own.Count == 0 || neighbour is null || StandsAlone(neighbour, context)) return 0;
		var theirs = Delimiters(AnsiEmitterSupport.Fold(neighbour, context.Format));
		var shared = 0;
		while (shared < own.Count && shared < theirs.Count && own[shared] == theirs[shared]) shared++;
		return shared;
	}

	private static void WriteAlone(in AnsiStyle style, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output) =>
		WriteDelimited(Delimiters(style), 0, 0, body, output);

	/// <summary>
	/// Writes <paramref name="body"/> inside <paramref name="delimiters"/>, the first <paramref name="opened"/> of
	/// them already open before it and the first <paramref name="kept"/> left open after it.
	/// </summary>
	private static void WriteDelimited(List<Delimiter> delimiters, int opened, int kept, ReadOnlySpan<char> body, IBufferWriter<char> output)
	{
		if (delimiters.Count == 0)
		{
			output.Write(body);
			return;
		}

		while (true)
		{
			var newline = body.IndexOf('\n');
			if (newline < 0)
			{
				WriteLine(delimiters, opened, kept, body, output);
				return;
			}

			// Everything is closed before the line ending and opened again after it.
			WriteLine(delimiters, opened, 0, body[..newline], output);
			output.Write("\n");
			body = body[(newline + 1)..];
			opened = 0;
		}
	}

	/// <summary>One line of a run: the delimiters it opens after its leading whitespace, and those it closes before its trailing whitespace.</summary>
	private static void WriteLine(List<Delimiter> delimiters, int opened, int kept, ReadOnlySpan<char> line, IBufferWriter<char> output)
	{
		var start = line.IndexOfAnyExcept(' ', '\t');
		if (start < 0)
		{
			// Nothing to cover. What was open before closes here and what stays open after opens here, against
			// the text either side of the whitespace; a pair that would open and close round nothing is left out.
			for (var i = opened - 1; i >= kept; i--) output.Write(delimiters[i].Close);
			output.Write(line);
			for (var i = opened; i < kept; i++) output.Write(delimiters[i].Open);
			return;
		}

		var end = line.LastIndexOfAnyExcept(' ', '\t') + 1;
		output.Write(line[..start]);
		for (var i = opened; i < delimiters.Count; i++) output.Write(delimiters[i].Open);
		output.Write(line[start..end]);
		for (var i = delimiters.Count - 1; i >= kept; i--) output.Write(delimiters[i].Close);
		output.Write(line[end..]);
	}
}
