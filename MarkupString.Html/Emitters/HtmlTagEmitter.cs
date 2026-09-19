using System.Buffers;
namespace MarkupString.Html;

/// <summary>
/// Renders an <see cref="HtmlMarkup"/> layer as its own tag: <c>&lt;{TagName} {Attributes}&gt;
/// body&lt;/{TagName}&gt;</c>, or <c>&lt;{TagName}&gt;body&lt;/{TagName}&gt;</c> when there are no
/// attributes. One instance is registered per format — <see cref="MarkupFormat.Html"/>,
/// <see cref="MarkupFormat.Pueblo"/> and <see cref="MarkupFormat.Mxp"/> — because these formats all
/// want the tag written verbatim, unlike Ansi/BBCode, where <see cref="HtmlMarkup"/> folds a
/// handful of tags into terminal styling instead (see <see cref="HtmlMarkup.TryGetAnsiStyle"/>).
/// </summary>
/// <remarks>
/// Without a policy the tag name and attributes are written exactly as the markup carries them. With
/// one, every tag is held to it as it is written — see <see cref="HtmlTagPolicy.Apply"/> — and a tag it
/// refuses leaves its body in place, unwrapped. The body is encoded by the renderer either way.
/// </remarks>
public sealed class HtmlTagEmitter : IMarkupEmitter
{
	/// <summary>An emitter for <paramref name="format"/> that writes every tag as the markup carries it.</summary>
	public HtmlTagEmitter(MarkupFormat format) : this(format, null)
	{
	}

	/// <summary>An emitter for <paramref name="format"/> that holds every tag to <paramref name="policy"/>, when one is given.</summary>
	public HtmlTagEmitter(MarkupFormat format, HtmlTagPolicy? policy)
	{
		ArgumentNullException.ThrowIfNull(format);
		Format = format;
		Policy = policy;
	}

	/// <inheritdoc/>
	public Type MarkupType => typeof(HtmlMarkup);

	/// <inheritdoc/>
	public MarkupFormat Format { get; }

	/// <summary>The policy every tag is held to, or <see langword="null"/> to write tags as given.</summary>
	public HtmlTagPolicy? Policy { get; }

	/// <inheritdoc/>
	public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(markup);
		ArgumentNullException.ThrowIfNull(output);

		var html = (HtmlMarkup)markup;
		if (Policy is not null)
		{
			if (Policy.Apply(html) is not { } held)
			{
				output.Write(body);
				return;
			}

			html = held;
		}

		output.Write("<");
		output.Write(html.TagName);
		if (html.Attributes is { Length: > 0 } attributes)
		{
			output.Write(" ");
			output.Write(attributes);
		}
		output.Write(">");
		output.Write(body);
		output.Write("</");
		output.Write(html.TagName);
		output.Write(">");
	}
}
