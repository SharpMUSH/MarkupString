using System.Buffers;
using System.Net;

namespace MarkupString.Mxp;

/// <summary>
/// Writes an <see cref="MxpElement"/> as the nearest thing a browser has, for the handful that have one:
/// an image, a sound, a gauge, status text and a frame. Everything else writes nothing, as it does for a
/// terminal.
/// </summary>
/// <remarks>
/// <para>Each carries its MXP name in a <c>data-mxp</c> attribute and an <c>ms-mxp-*</c> class, so a page
/// can find them, style them, or take over an element this emitter renders plainly. A page that wants
/// none of it registers <see cref="MxpSilentEmitter"/> for <see cref="MarkupFormat.Html"/> instead.</para>
/// <para>A source address is written only when it is an absolute <c>http</c> or <c>https</c> URL. MXP's <c>FName</c> is a file on the game's own sound or
/// image directory, which a browser has no way to resolve, so an element with no <c>URL</c> of its own
/// renders as its caption rather than a broken fetch.</para>
/// </remarks>
public sealed class MxpHtmlEmitter : IMarkupEmitter
{
	/// <summary>The one instance; the emitter carries no state.</summary>
	public static readonly MxpHtmlEmitter Instance = new();

	private MxpHtmlEmitter()
	{
	}

	/// <inheritdoc/>
	public Type MarkupType => typeof(MxpElement);

	/// <inheritdoc/>
	public MarkupFormat Format => MarkupFormat.Html;

	/// <inheritdoc/>
	public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(markup);
		ArgumentNullException.ThrowIfNull(output);

		var element = (MxpElement)markup;
		switch (element.Name.ToUpperInvariant())
		{
			case "IMAGE":
				WriteImage(element, output);
				return;
			case "SOUND" or "MUSIC":
				WriteAudio(element, output);
				return;
			case "GAUGE":
				WriteGauge(element, output);
				return;
			case "STAT":
				WriteStat(element, output);
				return;
			case "FRAME" or "VAR":
				WriteWrapper(element, body, output);
				return;
			default:
				if (element.WrapsContent) output.Write(body);
				return;
		}
	}

	private static void WriteImage(MxpElement element, IBufferWriter<char> output)
	{
		var source = Address(element, "URL");
		if (source is null) return;

		output.Write("<img class=\"ms-mxp-image\" data-mxp=\"IMAGE\" src=\"");
		output.Write(WebUtility.HtmlEncode(source));
		output.Write("\" alt=\"");
		output.Write(WebUtility.HtmlEncode(Positional(element) ?? string.Empty));
		output.Write("\">");
	}

	private static void WriteAudio(MxpElement element, IBufferWriter<char> output)
	{
		var source = Address(element, "U");
		if (source is null) return;

		output.Write("<audio class=\"ms-mxp-");
		output.Write(element.Name.ToLowerInvariant());
		output.Write("\" data-mxp=\"");
		output.Write(element.Name.ToUpperInvariant());
		output.Write("\" src=\"");
		output.Write(WebUtility.HtmlEncode(source));
		output.Write("\" autoplay></audio>");
	}

	private static void WriteGauge(MxpElement element, IBufferWriter<char> output)
	{
		// The entity names are the client's to resolve, and a browser holds none of them, so the page is
		// given the names and draws the bar itself.
		output.Write("<span class=\"ms-mxp-gauge\" data-mxp=\"GAUGE\" data-entity=\"");
		output.Write(WebUtility.HtmlEncode(Positional(element) ?? string.Empty));
		output.Write("\" data-max=\"");
		output.Write(WebUtility.HtmlEncode(Named(element, "MAX") ?? string.Empty));
		output.Write("\">");
		output.Write(WebUtility.HtmlEncode(Named(element, "CAPTION") ?? Positional(element) ?? string.Empty));
		output.Write("</span>");
	}

	private static void WriteStat(MxpElement element, IBufferWriter<char> output)
	{
		output.Write("<span class=\"ms-mxp-stat\" data-mxp=\"STAT\" data-entity=\"");
		output.Write(WebUtility.HtmlEncode(Positional(element) ?? string.Empty));
		output.Write("\">");
		output.Write(WebUtility.HtmlEncode(Named(element, "CAPTION") ?? Positional(element) ?? string.Empty));
		output.Write("</span>");
	}

	private static void WriteWrapper(MxpElement element, ReadOnlySpan<char> body, IBufferWriter<char> output)
	{
		output.Write("<span class=\"ms-mxp-");
		output.Write(element.Name.ToLowerInvariant());
		output.Write("\" data-mxp=\"");
		output.Write(element.Name.ToUpperInvariant());
		output.Write("\" data-name=\"");
		output.Write(WebUtility.HtmlEncode(Positional(element) ?? string.Empty));
		output.Write("\">");
		output.Write(body);
		output.Write("</span>");
	}

	private static string? Positional(MxpElement element)
	{
		foreach (var argument in element.Arguments)
		{
			if (argument.Name is null) return argument.Value;
		}

		return null;
	}

	private static string? Named(MxpElement element, string name)
	{
		foreach (var argument in element.Arguments)
		{
			if (string.Equals(argument.Name, name, StringComparison.OrdinalIgnoreCase)) return argument.Value;
		}

		return null;
	}

	/// <summary>The element's own address, when it gave one a browser can fetch.</summary>
	private static string? Address(MxpElement element, string name)
	{
		var value = Named(element, name);
		if (value is null) return null;

		return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
			? value
			: null;
	}
}
