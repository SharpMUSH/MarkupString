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
/// <para>A tag over several runs is one element: text split by an inner layer (a link inside <c>&lt;pre&gt;</c>)
/// is written <c>&lt;pre&gt;&lt;a&gt;a&lt;/a&gt; &lt;a&gt;b&lt;/a&gt;&lt;/pre&gt;</c>, not one <c>&lt;pre&gt;</c> per run. The open
/// tag is left out where the run before carries the same layer with the same layers around it, and the close
/// tag where the run after does, so the elements still nest.</para>
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

		if (!Continues(markup, context.Markups, context.Previous))
		{
			output.Write("<");
			output.Write(html.TagName);
			if (html.Attributes is { Length: > 0 } attributes)
			{
				output.Write(" ");
				output.Write(attributes);
			}
			output.Write(">");
		}
		output.Write(body);
		if (!Continues(markup, context.Markups, context.Next))
		{
			output.Write("</");
			output.Write(html.TagName);
			output.Write(">");
		}
	}

	/// <summary>
	/// Whether <paramref name="markup"/>'s element goes on through <paramref name="neighbour"/>: the neighbour
	/// carries it, and the layers outside it in <paramref name="own"/> are the ones outside it there.
	/// </summary>
	/// <remarks>
	/// Only tags are known to continue: another layer outside, such as a colour span, is closed and opened again
	/// on every run, so an element inside it cannot go on past the run. A neighbour holding a point may be left out
	/// by a format that does not draw it, so the element is closed before it and opened again after it, and a point run is its own element.
	/// </remarks>
	private static bool Continues(IMarkup markup, MarkupSet own, MarkupSet? neighbour)
	{
		if (neighbour is null || HoldsPoint(own) || HoldsPoint(neighbour) || Outside(own, markup) is not { } mine || Outside(neighbour, markup) is not { } theirs) return false;
		return mine.SequenceEqual(theirs);
	}

	private static bool HoldsPoint(MarkupSet markups)
	{
		foreach (var layer in markups)
		{
			if (layer is IPointMarkup) return true;
		}

		return false;
	}

	/// <summary>
	/// <paramref name="markup"/> and the drawn layers outside it, outermost last; null when the set lacks it, or a
	/// drawn layer outside it is not a tag.
	/// </summary>
	private static List<IMarkup>? Outside(MarkupSet markups, IMarkup markup)
	{
		List<IMarkup>? outside = null;
		foreach (var layer in markups)
		{
			if (outside is null && layer.Equals(markup)) outside = [];
			if (outside is null || layer is IDataMarkup) continue;
			if (layer is not HtmlMarkup) return null;
			outside.Add(layer);
		}

		return outside;
	}
}
