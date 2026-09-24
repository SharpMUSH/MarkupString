using System.Buffers;
namespace MarkupString;

/// <summary>
/// Turns a <see cref="MarkupText"/> into an output format: literal text encoded per
/// <see cref="MarkupFormat.Encoding"/>, each styled run handed to the registry's set emitter or,
/// failing that, wrapped by its layers' emitters innermost first.
/// </summary>
public static class MarkupTextRenderer
{
	/// <summary>C0 controls other than tab, newline and carriage return, plus DEL.</summary>
	private const string ControlCharacters = "\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u000b\u000c\u000e\u000f\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001a\u001b\u001c\u001d\u001e\u001f\u007f";

	/// <summary>
	/// The three characters that are markup in HTML text, and so cannot survive being written
	/// literally into it: <c>&lt;</c> opens a tag and <c>&amp;</c> opens an entity, and <c>&gt;</c>
	/// goes with them because a parser recovering from a malformed tag looks for it.
	/// </summary>
	/// <remarks>
	/// <c>"</c> and <c>'</c> are deliberately absent. They are markup only inside an attribute
	/// value, and no attribute is written from here -- <c>HtmlTagEmitter</c> writes the body
	/// between <c>&gt;</c> and <c>&lt;/</c>, and the layers that do emit attributes encode their own.
	/// Encoding them here bought nothing and cost something: over a socket it inflates every
	/// apostrophe in every line of dialogue fivefold, and it leaves Pueblo and MXP output leaning on
	/// the two entities a client is least likely to implement.
	/// </remarks>
	private const string HtmlCharacters = "<>&";

	private static readonly SearchValues<char> Controls = SearchValues.Create(ControlCharacters);

	/// <summary><see cref="Controls"/> plus the characters HTML encodes.</summary>
	private static readonly SearchValues<char> ControlsAndHtml = SearchValues.Create(ControlCharacters + HtmlCharacters);

	/// <summary><see cref="ControlsAndHtml"/> plus the line endings <see cref="TextEncoding.HtmlLineBreaks"/> rewrites.</summary>
	private static readonly SearchValues<char> ControlsHtmlAndLines = SearchValues.Create(ControlCharacters + HtmlCharacters + "\r\n");

	/// <summary>Writes <paramref name="text"/> to <paramref name="output"/> under <paramref name="encoding"/>.</summary>
	public static void EncodeText(ReadOnlySpan<char> text, TextEncoding encoding, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(output);
		switch (encoding)
		{
			case TextEncoding.StripControls:
				Strip(text, output);
				break;
			case TextEncoding.Html:
				HtmlEncode(text, output, lineBreaks: false);
				break;
			case TextEncoding.HtmlLineBreaks:
				HtmlEncode(text, output, lineBreaks: true);
				break;
			default:
				output.Write(text);
				break;
		}
	}

	private static void Strip(ReadOnlySpan<char> text, IBufferWriter<char> output)
	{
		while (!text.IsEmpty)
		{
			var index = text.IndexOfAny(Controls);
			if (index < 0)
			{
				output.Write(text);
				return;
			}
			output.Write(text[..index]);
			text = text[(index + 1)..];
		}
	}

	/// <summary>The line ending a client renders the stream as HTML reads as one.</summary>
	private const string HtmlLineBreak = "<BR>\n";

	private static void HtmlEncode(ReadOnlySpan<char> text, IBufferWriter<char> output, bool lineBreaks)
	{
		var searchValues = lineBreaks ? ControlsHtmlAndLines : ControlsAndHtml;
		while (!text.IsEmpty)
		{
			var index = text.IndexOfAny(searchValues);
			if (index < 0)
			{
				output.Write(text);
				return;
			}
			output.Write(text[..index]);
			switch (text[index])
			{
				case '<': output.Write("&lt;"); break;
				case '>': output.Write("&gt;"); break;
				case '&': output.Write("&amp;"); break;
				case '\n': output.Write(HtmlLineBreak); break;

				// A CRLF is one line ending, so the newline is consumed with the return; a lone CR is
				// still an ending on the clients old enough to write one.
				case '\r':
					output.Write(HtmlLineBreak);
					if (index + 1 < text.Length && text[index + 1] == '\n') index++;
					break;
				default: break;   // a control character: dropped
			}
			text = text[(index + 1)..];
		}
	}

	/// <summary>Renders <paramref name="text"/> in <paramref name="format"/> to <paramref name="output"/>.</summary>
	public static void Render(MarkupText text, MarkupFormat format, MarkupRegistry registry, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(format);
		ArgumentNullException.ThrowIfNull(registry);
		ArgumentNullException.ThrowIfNull(output);

		// The document framer wraps the whole text, line framing included: its preamble comes before
		// the first line's prefix, and its epilogue after the last line.
		var framer = registry.FindFramer(format);
		framer?.WritePreamble(output);

		bool anyRunEmitted;
		if (registry.FindLineFramer(format) is { } lineFramer)
		{
			// A line is only known once it has been written — an emitter can put a newline anywhere —
			// so the body is rendered first and the prefixes go in on the way out.
			using var rendered = new PooledCharWriter(text.Text.Length * 2);
			anyRunEmitted = RenderBody(text, format, registry, rendered);
			WriteFramedLines(rendered.WrittenSpan, lineFramer, output);
		}
		else
		{
			anyRunEmitted = RenderBody(text, format, registry, output);
		}

		framer?.WriteEpilogue(anyRunEmitted, output);
	}

	private static void WriteFramedLines(ReadOnlySpan<char> rendered, ILineFramer framer, IBufferWriter<char> output)
	{
		while (true)
		{
			var newline = rendered.IndexOf('\n');
			var line = newline < 0 ? rendered : rendered[..newline];
			if (line.Length > 0 && line is not "\r") framer.WriteLineStart(output);
			output.Write(line);
			if (newline < 0) return;
			output.Write("\n");
			rendered = rendered[(newline + 1)..];
		}
	}

	/// <summary>Writes the text and its runs, and returns whether any emitter wrote for a run.</summary>
	private static bool RenderBody(MarkupText text, MarkupFormat format, MarkupRegistry registry, IBufferWriter<char> output)
	{
		var content = text.Text.AsSpan();
		var runs = text.Runs;
		var position = 0;
		var anyRunEmitted = false;
		for (var i = 0; i < runs.Length; i++)
		{
			var run = runs[i];
			if (run.Start > position) EncodeText(content[position..run.Start], format.Encoding, output);
			var context = new EmitContext
			{
				Format = format,
				Registry = registry,
				Markups = run.Markups,
				Previous = i > 0 && runs[i - 1].End == run.Start ? runs[i - 1].Markups : null,
				Next = i + 1 < runs.Length && runs[i + 1].Start == run.End ? runs[i + 1].Markups : null,
				IsFirstRun = i == 0,
				IsLastRun = i == runs.Length - 1,
			};
			anyRunEmitted |= RenderRun(run, content.Slice(run.Start, run.Length), format, registry, context, output);
			position = run.End;
		}
		if (position < content.Length) EncodeText(content[position..], format.Encoding, output);

		return anyRunEmitted;
	}

	/// <summary>Renders one run to <paramref name="output"/>. Returns whether an emitter actually wrote — a set
	/// emitter that claimed the run, or at least one per-markup emitter along the layer stack — as against every
	/// layer being unregistered and the body passing through unchanged.</summary>
	private static bool RenderRun(
		Run run,
		ReadOnlySpan<char> body,
		MarkupFormat format,
		MarkupRegistry registry,
		in EmitContext context,
		IBufferWriter<char> output)
	{
		var markups = run.Markups;
		var points = CountPoints(markups);

		var front = new PooledCharWriter(body.Length + 16);
		PooledCharWriter? back = null;
		try
		{
			if (points > 0)
			{
				// A point's carrier is not text. With no emitter for this format the point writes nothing
				// — not the carrier, and not the layers around it, which would wrap an empty body.
				if (!WritePoints(markups, body, format, registry, context, front)) return false;
				markups = Without(markups, points);
				if (markups is null)
				{
					output.Write(front.WrittenSpan);
					return true;
				}
			}
			else
			{
				EncodeText(body, EncodingFor(markups, format, registry), front);
			}

			var setEmitter = registry.FindSetEmitter(format);
			if (setEmitter is not null)
			{
				back = new PooledCharWriter(front.WrittenCount + 16);
				if (setEmitter.TryEmit(markups!, front.WrittenSpan, context, back))
				{
					output.Write(back.WrittenSpan);
					return true;
				}
				back.Clear();
			}

			var emitted = points > 0;
			for (var i = 0; i < markups!.Count; i++)
			{
				var markup = markups[i];
				var emitter = registry.FindEmitter(markup.GetType(), format);
				if (emitter is null) continue;
				emitted = true;
				back ??= new PooledCharWriter(front.WrittenCount + 16);
				back.Clear();
				emitter.Emit(markup, front.WrittenSpan, context, back);
				(front, back) = (back, front);
			}

			output.Write(front.WrittenSpan);
			return emitted;
		}
		finally
		{
			front.Dispose();
			back?.Dispose();
		}
	}

	/// <summary>
	/// The encoding for a run's text: the innermost layer that decides one
	/// (<see cref="ITextEncodingSource"/>) and is written in this format, or the format's own when none
	/// is.
	/// </summary>
	/// <remarks>
	/// A layer only governs the text it covers if it is actually written. Preformatting that reaches a
	/// registry without the package that writes <c>&lt;xch_mudtext&gt;</c> marks nothing, so the client is
	/// still reading HTML and still needs the line endings the format would have written — suspending
	/// them there would lose every break in the region.
	/// </remarks>
	private static TextEncoding EncodingFor(MarkupSet markups, MarkupFormat format, MarkupRegistry registry)
	{
		for (var i = 0; i < markups.Count; i++)
			if (markups[i] is ITextEncodingSource source
				&& registry.FindEmitter(markups[i].GetType(), format) is not null
				&& source.TryGetEncoding(format, out var encoding))
				return encoding;

		return format.Encoding;
	}

	/// <summary>How many points the run carries. A well-formed run carries at most one.</summary>
	private static int CountPoints(MarkupSet markups)
	{
		var points = 0;
		foreach (var markup in markups)
			if (markup is IPointMarkup) points++;
		return points;
	}

	/// <summary>
	/// Writes every point the run carries, innermost first, once per carrier it covers — equal points
	/// side by side coalesce into one run, and each carrier is still one point. Returns whether anything
	/// was written: a point this format has no emitter for writes nothing at all, and a run of nothing
	/// but such points is dropped whole, carrier included.
	/// </summary>
	private static bool WritePoints(
		MarkupSet markups,
		ReadOnlySpan<char> body,
		MarkupFormat format,
		MarkupRegistry registry,
		in EmitContext context,
		IBufferWriter<char> output)
	{
		var written = false;
		foreach (var markup in markups)
		{
			if (markup is not IPointMarkup point) continue;

			var emitter = registry.FindEmitter(point.GetType(), format);
			if (emitter is null) continue;

			var carrier = point.Carrier.Length;
			for (var i = 0; i + carrier <= body.Length; i += carrier) emitter.Emit(point, body.Slice(i, carrier), context, output);
			written = true;
		}

		return written;
	}

	/// <summary>The run's layers with the points taken out, or null when it carried nothing else.</summary>
	private static MarkupSet? Without(MarkupSet markups, int points)
	{
		if (markups.Count == points) return null;

		var rest = new List<IMarkup>(markups.Count - points);
		foreach (var markup in markups)
			if (markup is not IPointMarkup) rest.Add(markup);
		return MarkupSet.Of(rest);
	}
}
