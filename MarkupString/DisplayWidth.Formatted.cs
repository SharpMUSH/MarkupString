using System.Buffers;
using MarkupString.Decoding;

namespace MarkupString;

public static partial class DisplayWidth
{
	// Width needs the sequence boundaries, never a picture, title or other payload. Keep the
	// shared tokenizer's consumption rules without retaining arbitrarily large strings.
	private static readonly VtLimits WidthLimits = new() { MaxStringLength = 0, MaxApcLength = 0 };

	/// <summary>
	/// Cells occupied by the text in an ANSI fragment, excluding complete or incomplete terminal
	/// escape sequences. Unlike <see cref="Of(ReadOnlySpan{char})"/>, this reads formatting syntax.
	/// </summary>
	/// <remarks>
	/// Uses <see cref="VtTokenizer"/>'s UTF-8 rules: C1 characters are not eight-bit sequence introducers.
	/// Counts text, not cursor movement, tab stops, screen erasure or image dimensions. Line breaks
	/// occupy zero cells; widths across lines are added, not measured as a bounding rectangle.
	/// </remarks>
	public static int OfAnsi(ReadOnlySpan<char> text) =>
		text.Contains('\e') ? OfFormatted(text, tagged: false) : Of(text);

	/// <summary>
	/// Cells occupied by the text in an HTML, MXP or Pueblo fragment: tags, comments and ANSI
	/// sequences occupy no cells, and entities are decoded once before measuring their text.
	/// </summary>
	/// <remarks>
	/// Uses <see cref="TagScanner"/> and <see cref="TagSyntax.Entity"/>: unknown entities and incomplete
	/// tags remain literal text, as do tags or entities longer than the scanner limits. This is a
	/// text measure, not browser layout or protocol execution:
	/// it does not evaluate CSS, scripts, custom MXP entities, image dimensions or cursor movement.
	/// Line breaks occupy zero cells and widths across lines are added. Use <see cref="Of(ReadOnlySpan{char})"/>
	/// for literal source text; this method does not read Markdown or BBCode.
	/// </remarks>
	public static int OfTagged(ReadOnlySpan<char> text) =>
		text.IndexOfAny('<', '&', '\e') < 0 ? Of(text) : OfFormatted(text, tagged: true);

	/// <summary>
	/// Cells reserved by the run's original source when <see cref="EmitContext.SourceDisplayWidth"/>
	/// is supplied; otherwise measures <paramref name="body"/> using the context's format capabilities.
	/// </summary>
	/// <remarks>
	/// Source geometry wins even when an inner emitter replaced the text, and zero is a supplied width.
	/// Without it, ANSI uses <see cref="OfAnsi"/> and formats declaring <see cref="MarkupFormat.IsTagged"/>
	/// use <see cref="OfTagged"/>. Other formats retain literal <see cref="Of(ReadOnlySpan{char})"/>
	/// measurement; there is no Markdown or BBCode parser here. A manually constructed context should
	/// supply source geometry when its body cannot be measured by those fallbacks.
	/// </remarks>
	public static int Of(ReadOnlySpan<char> body, in EmitContext context)
	{
		if (context.SourceDisplayWidth is { } width) return width;
		if (context.Format == MarkupFormat.Ansi) return OfAnsi(body);
		if (context.Format.IsTagged) return OfTagged(body);
		return Of(body);
	}

	/// <summary>Collects visible text through the shared scanners, then measures it as one Unicode sequence.</summary>
	private static int OfFormatted(ReadOnlySpan<char> text, bool tagged)
	{
		using var plain = new PooledCharWriter(Math.Min(text.Length, 256));
		var sink = new WidthSink(plain, tagged);
		new VtTokenizer(WidthLimits).Feed(text, sink);
		sink.Finish();
		// Measure the resulting text together: formatting may divide a surrogate pair or a
		// joined emoji into separate callbacks, without adding a visible boundary between them.
		return Of(plain.WrittenSpan);
	}

	private sealed class WidthSink(PooledCharWriter plain, bool tagged) : IVtTokenSink, ITagSink
	{
		private readonly TagScanner? _tags = tagged ? new TagScanner() : null;
		public bool ReadsMarkup => true;

		/// <summary>Collects literal text directly, or passes it through the tag scanner when tagged syntax is enabled.</summary>
		public void Print(ReadOnlySpan<char> text)
		{
			if (_tags is null) plain.Write(text);
			else _tags.Feed(text, this);
		}

		public void Execute(char control)
		{
			Span<char> text = stackalloc char[1];
			text[0] = control;
			Print(text);
		}

		public void Text(ReadOnlySpan<char> text) => plain.Write(text);
		public void Tag(ReadOnlySpan<char> body) { }
		/// <summary>Decodes a known entity once and preserves an unknown entity as literal text.</summary>
		public void Entity(ReadOnlySpan<char> name)
		{
			if (TagSyntax.Entity(name) is { } decoded) plain.Write(decoded);
			else
			{
				plain.Write("&");
				plain.Write(name);
				plain.Write(";");
			}
		}

		public void Csi(in CsiSequence sequence) { }
		public void Esc(in EscSequence sequence) { }
		public void Osc(ReadOnlySpan<char> payload) { }
		public void Dcs(in CsiSequence header, ReadOnlySpan<char> data) { }
		public void Apc(ReadOnlySpan<char> payload) { }
		public void Abandoned(VtSequenceKind kind) { }
		/// <summary>Flushes an incomplete tag or entity as literal text before the combined text is measured.</summary>
		public void Finish() => _tags?.Flush(this);
	}
}
