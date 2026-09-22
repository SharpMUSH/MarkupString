using System.Buffers;
using System.Globalization;
namespace MarkupString.Html;

/// <summary>
/// Writes the shared vocabulary for a browser: the elements HTML has — <c>&lt;audio&gt;</c>,
/// <c>&lt;img&gt;</c>, <c>&lt;link rel="prefetch"&gt;</c> — and, for what it has no element for, an
/// empty or wrapping element whose class and <c>data-</c> attributes the page acts on: <c>ms-clear</c>,
/// <c>ms-pane</c>, <c>ms-gauge</c>.
/// </summary>
/// <remarks>
/// Addresses are written as given. Whether a page fetches, plays or shows them — and whether a
/// relative file name means anything to it — is the page's decision, made with a
/// <c>Content-Security-Policy</c>, with an <see cref="HtmlTagPolicy"/>, or by handling the element
/// itself. With a policy, every element here is held to it as it is written, and one it refuses is
/// written as a format that cannot express it writes it: nothing for a point, the text for the rest.
/// </remarks>
internal sealed class ElementHtmlEmitter(Type markupType, HtmlTagPolicy? policy = null) : IMarkupEmitter
{
	/// <summary>The markup types this emitter writes.</summary>
	internal static readonly Type[] Types =
	[
		typeof(SoundMarkup), typeof(SoundStopMarkup), typeof(ClearScreenMarkup), typeof(ExpireLinksMarkup),
		typeof(PrefetchMarkup), typeof(ImageMarkup), typeof(PaneMarkup), typeof(VariableMarkup),
		typeof(GaugeMarkup), typeof(StatusMarkup),
	];

	/// <summary>How an element is closed.</summary>
	private enum Shape
	{
		/// <summary>A void element: <c>&lt;img&gt;</c>, <c>&lt;link&gt;</c>.</summary>
		Void,

		/// <summary>An element with no content of its own: <c>&lt;audio&gt;&lt;/audio&gt;</c>, an empty span.</summary>
		Empty,

		/// <summary>An element around the text it marks.</summary>
		Wrapping,
	}

	public Type MarkupType { get; } = markupType;

	public MarkupFormat Format => MarkupFormat.Html;

	public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(markup);
		ArgumentNullException.ThrowIfNull(output);

		switch (markup)
		{
			case SoundMarkup sound:
				Write(markup, output, "audio", Shape.Empty, body,
					("class", "ms-sound"),
					("data-channel", sound.Channel == SoundChannel.Music ? "music" : "effects"),
					("src", sound.Source),
					("preload", "none"),
					("loop", sound.Loops ? string.Empty : null),
					("data-volume", Number(sound.Volume)),
					("data-repeats", sound.Loops ? null : Number(sound.Repeats)),
					("data-continues", sound.Continues ? "true" : null));
				return;

			case SoundStopMarkup stop:
				Write(markup, output, "span", Shape.Empty, body,
					("class", "ms-sound-stop"),
					("data-channel", stop.Channel switch
					{
						SoundChannel.Effects => "effects",
						SoundChannel.Music => "music",
						_ => null,
					}));
				return;

			case ClearScreenMarkup:
				Write(markup, output, "span", Shape.Empty, body, ("class", "ms-clear"));
				return;

			case ExpireLinksMarkup expire:
				Write(markup, output, "span", Shape.Empty, body, ("class", "ms-expire"), ("data-group", expire.Group));
				return;

			case PrefetchMarkup prefetch:
				Write(markup, output, "link", Shape.Void, body, ("rel", "prefetch"), ("href", prefetch.Source));
				return;

			case ImageMarkup image:
				Write(markup, output, "img", Shape.Void, body,
					("class", "ms-image"),
					("src", image.Source),
					("alt", image.Description ?? string.Empty),
					("width", Number(image.Width)),
					("height", Number(image.Height)),
					("data-align", image.Align?.ToString().ToLowerInvariant()));
				return;

			case PaneMarkup pane:
				Write(markup, output, "span", Shape.Wrapping, body,
					("class", "ms-pane"), ("data-pane", pane.Name), ("data-title", pane.Title));
				return;

			case VariableMarkup variable:
				Write(markup, output, "span", Shape.Wrapping, body, ("class", "ms-variable"), ("data-name", variable.Name));
				return;

			case GaugeMarkup gauge:
				Write(markup, output, "span", Shape.Wrapping, body,
					("class", "ms-gauge"), ("data-variable", gauge.Variable), ("data-maximum", gauge.Maximum),
					("data-caption", gauge.Caption), ("data-color", gauge.Color));
				return;

			case StatusMarkup status:
				Write(markup, output, "span", Shape.Wrapping, body,
					("class", "ms-status"), ("data-variable", status.Variable),
					("data-maximum", status.Maximum), ("data-caption", status.Caption));
				return;

			default:
				output.Write(body);
				return;
		}
	}

	/// <summary>
	/// Writes one element, held to the policy when there is one. The tag is built as an
	/// <see cref="HtmlMarkup"/> so that it is the same thing a policy sees anywhere else. A tag the
	/// policy refuses leaves what a format that cannot express the element leaves: nothing for a point,
	/// and the text for everything else — a refused picture still leaves its description.
	/// </summary>
	private void Write(
		IMarkup markup,
		IBufferWriter<char> output,
		string name,
		Shape shape,
		ReadOnlySpan<char> body,
		params ReadOnlySpan<(string Name, string? Value)> attributes)
	{
		var written = new HtmlAttribute[Count(attributes)];
		var next = 0;
		foreach (var (attribute, value) in attributes)
		{
			if (value is not null) written[next++] = new HtmlAttribute(attribute, value);
		}

		var tag = HtmlMarkup.Tag(name, written);
		if (policy is not null)
		{
			if (policy.Apply(tag) is not { } held)
			{
				if (markup is not IPointMarkup) output.Write(body);
				return;
			}

			tag = held;
		}

		output.Write("<");
		output.Write(tag.TagName);
		if (tag.Attributes is { Length: > 0 } rendered)
		{
			output.Write(" ");
			output.Write(rendered);
		}
		output.Write(">");

		if (shape == Shape.Void) return;

		if (shape == Shape.Wrapping) output.Write(body);
		output.Write("</");
		output.Write(tag.TagName);
		output.Write(">");
	}

	private static int Count(ReadOnlySpan<(string Name, string? Value)> attributes)
	{
		var count = 0;
		foreach (var (_, value) in attributes)
		{
			if (value is not null) count++;
		}

		return count;
	}

	private static string? Number(int? value) => value?.ToString(CultureInfo.InvariantCulture);
}
