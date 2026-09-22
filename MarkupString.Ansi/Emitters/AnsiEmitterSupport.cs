using System.Buffers;
using System.Net;
namespace MarkupString.Ansi;

/// <summary>Which tag vocabulary <see cref="AnsiEmitterSupport.EmitTagged"/> writes links in.</summary>
internal enum TagFlavour
{
	/// <summary>Pueblo: <c>&lt;A XCH_CMD&gt;</c> for commands, <c>&lt;A HREF&gt;</c> for URLs.</summary>
	Pueblo,

	/// <summary>MXP: <c>&lt;SEND&gt;</c> for commands, <c>&lt;A HREF&gt;</c> for URLs.</summary>
	Mxp
}

/// <summary>
/// The parts every set emitter in this package shares: folding a run's layers into one
/// <see cref="AnsiStyle"/>, delegating the layers it does not own, and the two link forms that are
/// spelled the same way in more than one format.
/// </summary>
internal static class AnsiEmitterSupport
{
	private const string Osc8 = "\e]8;;";
	private const string Bel = "\u0007";

	/// <summary>
	/// Whether this package folds <paramref name="layer"/> into the run's style for
	/// <paramref name="format"/>, rather than delegating it to its own emitter. The one place that
	/// question is answered: <see cref="Fold"/> takes the layers it says yes to and
	/// <see cref="WriteWrapped"/> takes exactly the rest, so no layer is rendered twice or dropped.
	/// </summary>
	internal static bool ClaimsStyle(IMarkup layer, MarkupFormat format, out AnsiStyle style)
	{
		if (layer is IAnsiStyleSource source) return source.TryGetAnsiStyle(format, out style);
		style = AnsiStyle.None;
		return false;
	}

	/// <summary>
	/// Folds every layer that offers a style in <paramref name="format"/> into one, outermost
	/// first, so an inner layer's settings win. Layers that offer none are left for
	/// <see cref="WriteWrapped"/>.
	/// </summary>
	internal static AnsiStyle Fold(MarkupSet? set, MarkupFormat format)
	{
		if (set is null) return AnsiStyle.None;

		var effective = AnsiStyle.None;
		for (var i = set.Count - 1; i >= 0; i--)
			if (ClaimsStyle(set[i], format, out var style))
				effective = effective.Combine(style);

		return effective;
	}

	/// <summary>
	/// Writes one contiguous stretch of layers this package folds, around a body the layers inside it
	/// have already been written into.
	/// </summary>
	internal delegate void SegmentWriter(in AnsiStyle style, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output);

	/// <summary>
	/// Writes a run in the nesting it was built with, for a format that expresses nesting: the layers
	/// this package folds are written by <paramref name="writeSegment"/> in stretches, and a layer it
	/// does not own is written by its own emitter between them. <c>[bold, tag, red]</c> is a bold inside
	/// a tag inside a red, and comes out that way, rather than as one bold red inside a tag.
	/// </summary>
	/// <remarks>
	/// The common shapes cost nothing extra: with no foreign layer carrying an emitter, this is one
	/// <paramref name="writeSegment"/> over the whole fold, the same call the emitters made before there
	/// was anything to interleave.
	/// </remarks>
	internal static void EmitSegmented(
		MarkupSet set,
		ReadOnlySpan<char> body,
		in EmitContext context,
		IBufferWriter<char> output,
		SegmentWriter writeSegment)
	{
		if (!HasDelegatedLayer(set, context))
		{
			writeSegment(Fold(set, context.Format), body, context, output);
			return;
		}

		PooledCharWriter? front = null;
		PooledCharWriter? back = null;
		try
		{
			front = new PooledCharWriter(body.Length + 32);
			front.Write(body);
			back = new PooledCharWriter(front.WrittenCount + 32);

			var pending = AnsiStyle.None;
			var hasPending = false;

			// A style that clears discards everything around it (AnsiStyle.Combine), and a delegated layer
			// in between does not change that: once a stretch has cleared, the stretches outside it write
			// no styling at all.
			var cleared = false;
			for (var i = 0; i < set.Count; i++)
			{
				if (ClaimsStyle(set[i], context.Format, out var style))
				{
					// Innermost first, and an inner layer's settings win, which is how Fold combines them.
					pending = hasPending ? style.Combine(pending) : style;
					hasPending = true;
					continue;
				}

				var emitter = context.Registry.FindEmitter(set[i].GetType(), context.Format);
				if (emitter is null) continue;

				if (hasPending)
				{
					cleared |= pending.Clear;
					back.Clear();
					writeSegment(cleared ? AnsiStyle.None : pending, front.WrittenSpan, context, back);
					(front, back) = (back, front);
					pending = AnsiStyle.None;
					hasPending = false;
				}

				back.Clear();
				emitter.Emit(set[i], front.WrittenSpan, context, back);
				(front, back) = (back, front);
			}

			writeSegment(cleared ? AnsiStyle.None : pending, front.WrittenSpan, context, output);
		}
		finally
		{
			front?.Dispose();
			back?.Dispose();
		}
	}

	/// <summary>Whether any layer is one this package does not fold and something else can write.</summary>
	private static bool HasDelegatedLayer(MarkupSet set, in EmitContext context)
	{
		for (var i = 0; i < set.Count; i++)
			if (!ClaimsStyle(set[i], context.Format, out _)
				&& context.Registry.FindEmitter(set[i].GetType(), context.Format) is not null)
				return true;

		return false;
	}

	/// <summary>
	/// Writes <paramref name="core"/> — the run as this package rendered it — wrapped by the layers
	/// this package does not own in <see cref="EmitContext.Format"/>, innermost first, each through its
	/// own emitter for that format. A layer with no emitter registered for the format wraps in nothing:
	/// its body passes through. This is the terminal's shape, where a style is state rather than
	/// nesting: the sequence is written once around the run, and where a delegated layer's own output
	/// sits relative to it changes nothing on screen. Formats that express nesting use
	/// <see cref="EmitSegmented"/>.
	/// </summary>
	internal static void WriteWrapped(
		MarkupSet set,
		ReadOnlySpan<char> core,
		in EmitContext context,
		IBufferWriter<char> output)
	{
		PooledCharWriter? front = null;
		PooledCharWriter? back = null;
		try
		{
			for (var i = 0; i < set.Count; i++)
			{
				var layer = set[i];
				if (ClaimsStyle(layer, context.Format, out _)) continue;

				var emitter = context.Registry.FindEmitter(layer.GetType(), context.Format);
				if (emitter is null) continue;

				if (front is null)
				{
					front = new PooledCharWriter(core.Length + 16);
					front.Write(core);
				}

				back ??= new PooledCharWriter(front.WrittenCount + 16);
				back.Clear();
				emitter.Emit(layer, front.WrittenSpan, context, back);
				(front, back) = (back, front);
			}

			output.Write(front is null ? core : front.WrittenSpan);
		}
		finally
		{
			front?.Dispose();
			back?.Dispose();
		}
	}

	/// <summary>
	/// Writes the body inside an OSC 8 hyperlink when the style carries a navigable URL. OSC 8 can
	/// only navigate, so a command link — and any URL with a scheme
	/// <see cref="UrlSafety.IsSafeNavigableUrl"/> rejects — is written as plain text.
	/// </summary>
	internal static void WriteHyperlinked(in AnsiStyle style, ReadOnlySpan<char> body, IBufferWriter<char> output)
	{
		if (style.LinkKind != LinkKind.Url
			|| style.LinkUrl is not { Length: > 0 } url
			|| !UrlSafety.IsSafeNavigableUrl(url))
		{
			output.Write(body);
			return;
		}

		output.Write(Osc8);
		output.Write(url);
		output.Write(Bel);
		output.Write(body);
		output.Write(Osc8);
		output.Write(Bel);
	}

	/// <summary>
	/// The shared Pueblo/MXP body: colours and attributes as SGR (these formats are not diffed
	/// across runs, so each styled run opens and closes its own state), links as the format's own
	/// tag, and everything else delegated.
	/// </summary>
	internal static void EmitTagged(
		MarkupSet set,
		ReadOnlySpan<char> body,
		in EmitContext context,
		IBufferWriter<char> output,
		TagFlavour flavour)
	{
		// The flavour cannot ride on the delegate's signature, so it is closed over here; a run with
		// nothing delegated never allocates the closure's work beyond this one call.
		EmitSegmented(set, body, context, output,
			(in AnsiStyle style, ReadOnlySpan<char> segment, in EmitContext segmentContext, IBufferWriter<char> segmentOutput) =>
			{
				SgrWriter.Transition(AnsiStyle.None, style, segmentOutput);
				WriteTaggedLink(style, segment, segmentOutput, flavour);
				if (LeavesState(style)) SgrWriter.Reset(segmentOutput);
			});
	}

	/// <summary>
	/// Whether the style leaves the terminal in a state that has to be closed. A link alone does
	/// not — OSC 8 closes itself — and neither does <see cref="AnsiStyle.Clear"/>, which is the
	/// reset <see cref="SgrWriter.Transition"/> already wrote.
	/// </summary>
	internal static bool LeavesState(in AnsiStyle style) =>
		style.Foreground is not null || style.Background is not null
		|| style.Bold || style.Faint || style.Italic || style.Underlined
		|| style.Overlined || style.Blink || style.Inverted || style.StrikeThrough;

	private static void WriteTaggedLink(
		in AnsiStyle style,
		ReadOnlySpan<char> body,
		IBufferWriter<char> output,
		TagFlavour flavour)
	{
		if (style.LinkUrl is not { Length: > 0 } url)
		{
			output.Write(body);
			return;
		}

		if (style.LinkKind == LinkKind.Command)
		{
			var (open, hint, close) = flavour == TagFlavour.Mxp
				? ("<SEND HREF=\"", " HINT=\"", "</SEND>")
				: ("<A XCH_CMD=\"", " XCH_HINT=\"", "</A>");

			output.Write(open);
			output.Write(WebUtility.HtmlEncode(url));
			output.Write("\"");
			if (style.LinkText is { Length: > 0 } text)
			{
				output.Write(hint);
				output.Write(WebUtility.HtmlEncode(text));
				output.Write("\"");
			}
			output.Write(">");
			output.Write(body);
			output.Write(close);
			return;
		}

		if (!UrlSafety.IsSafeNavigableUrl(url))
		{
			output.Write(body);
			return;
		}

		output.Write("<A HREF=\"");
		output.Write(WebUtility.HtmlEncode(url));
		output.Write("\">");
		output.Write(body);
		output.Write("</A>");
	}
}
