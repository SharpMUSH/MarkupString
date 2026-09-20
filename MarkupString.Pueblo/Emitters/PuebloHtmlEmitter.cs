using System.Buffers;
using System.Net;

namespace MarkupString.Pueblo;

/// <summary>
/// Writes a <see cref="PuebloElement"/> as the nearest thing a browser has, for the two that have one:
/// a picture and a pane. Everything else — the page instruction, the mode switches, prefetching, the
/// sound attributes — writes nothing, because none of it means anything outside a Pueblo client.
/// </summary>
/// <remarks>
/// <para>Each carries its Pueblo name in a <c>data-pueblo</c> attribute and an <c>ms-pueblo-*</c> class,
/// so a page can find them, style them, or take one over.</para>
/// <para>A source is written only when it is an absolute <c>http</c> or <c>https</c> URL: Pueblo names
/// a file against the world's own directory, which a browser cannot resolve.</para>
/// </remarks>
public sealed class PuebloHtmlEmitter : IMarkupEmitter
{
	/// <summary>The one instance; the emitter carries no state.</summary>
	public static readonly PuebloHtmlEmitter Instance = new();

	private PuebloHtmlEmitter()
	{
	}

	/// <inheritdoc/>
	public Type MarkupType => typeof(PuebloElement);

	/// <inheritdoc/>
	public MarkupFormat Format => MarkupFormat.Html;

	/// <inheritdoc/>
	public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(markup);
		ArgumentNullException.ThrowIfNull(output);

		var element = (PuebloElement)markup;

		if (element.Name.Equals("xch_pane", StringComparison.OrdinalIgnoreCase))
		{
			output.Write("<span class=\"ms-pueblo-pane\" data-pueblo=\"xch_pane\" data-name=\"");
			output.Write(WebUtility.HtmlEncode(Attribute(element, "name") ?? string.Empty));
			output.Write("\">");
			output.Write(body);
			output.Write("</span>");
			return;
		}

		// An <img> carrying one of the sound attributes is a sound rather than a picture, and the file it
		// names is the world's, not an address.
		if (element.Name.Equals("img", StringComparison.OrdinalIgnoreCase)
			&& Attribute(element, "src") is { } source
			&& Address(source) is { } address)
		{
			output.Write("<img class=\"ms-pueblo-image\" data-pueblo=\"img\" src=\"");
			output.Write(WebUtility.HtmlEncode(address));
			output.Write("\" alt=\"");
			output.Write(WebUtility.HtmlEncode(Attribute(element, "xch_hint") ?? string.Empty));
			output.Write("\">");
			return;
		}

		if (element.WrapsContent) output.Write(body);
	}

	private static string? Attribute(PuebloElement element, string name)
	{
		foreach (var attribute in element.Attributes)
		{
			if (string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase)) return attribute.Value;
		}

		return null;
	}

	private static string? Address(string value) =>
		Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? value : null;
}
