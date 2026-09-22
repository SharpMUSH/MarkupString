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
/// <c>Content-Security-Policy</c> or by handling the element itself.
/// </remarks>
internal sealed class ElementHtmlEmitter(Type markupType) : IMarkupEmitter
{
	/// <summary>The markup types this emitter writes.</summary>
	internal static readonly Type[] Types =
	[
		typeof(SoundMarkup), typeof(SoundStopMarkup), typeof(ClearScreenMarkup), typeof(ExpireLinksMarkup),
		typeof(PrefetchMarkup), typeof(ImageMarkup), typeof(PaneMarkup), typeof(VariableMarkup),
		typeof(GaugeMarkup), typeof(StatusMarkup),
	];

	public Type MarkupType { get; } = markupType;

	public MarkupFormat Format => MarkupFormat.Html;

	public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(markup);
		ArgumentNullException.ThrowIfNull(output);

		switch (markup)
		{
			case SoundMarkup sound:
				output.Write("<audio class=\"ms-sound\"");
				Attribute(output, "data-channel", sound.Channel == SoundChannel.Music ? "music" : "effects");
				Attribute(output, "src", sound.Source);
				Attribute(output, "preload", "none");
				if (sound.Loops) output.Write(" loop");
				Attribute(output, "data-volume", sound.Volume);
				if (!sound.Loops) Attribute(output, "data-repeats", sound.Repeats);
				if (sound.Continues) Attribute(output, "data-continues", "true");
				output.Write("></audio>");
				break;

			case SoundStopMarkup stop:
				output.Write("<span class=\"ms-sound-stop\"");
				if (stop.Channel is { } channel) Attribute(output, "data-channel", channel == SoundChannel.Music ? "music" : "effects");
				output.Write("></span>");
				break;

			case ClearScreenMarkup:
				output.Write("<span class=\"ms-clear\"></span>");
				break;

			case ExpireLinksMarkup expire:
				output.Write("<span class=\"ms-expire\"");
				Attribute(output, "data-group", expire.Group);
				output.Write("></span>");
				break;

			case PrefetchMarkup prefetch:
				output.Write("<link rel=\"prefetch\"");
				Attribute(output, "href", prefetch.Source);
				output.Write(">");
				break;

			case ImageMarkup image:
				output.Write("<img class=\"ms-image\"");
				Attribute(output, "src", image.Source);
				Attribute(output, "alt", image.Description ?? string.Empty);
				Attribute(output, "width", image.Width);
				Attribute(output, "height", image.Height);
				if (image.Align is { } align) Attribute(output, "data-align", align.ToString().ToLowerInvariant());
				output.Write(">");
				break;

			case PaneMarkup pane:
				Wrapping(output, "ms-pane", body, ("data-pane", pane.Name), ("data-title", pane.Title));
				break;

			case VariableMarkup variable:
				Wrapping(output, "ms-variable", body, ("data-name", variable.Name));
				break;

			case GaugeMarkup gauge:
				Wrapping(output, "ms-gauge", body,
					("data-variable", gauge.Variable), ("data-maximum", gauge.Maximum),
					("data-caption", gauge.Caption), ("data-color", gauge.Color));
				break;

			case StatusMarkup status:
				Wrapping(output, "ms-status", body,
					("data-variable", status.Variable), ("data-maximum", status.Maximum), ("data-caption", status.Caption));
				break;

			default:
				output.Write(body);
				break;
		}
	}

	private static void Wrapping(IBufferWriter<char> output, string className, ReadOnlySpan<char> body, params ReadOnlySpan<(string Name, string? Value)> attributes)
	{
		output.Write("<span class=\"");
		output.Write(className);
		output.Write("\"");
		foreach (var (name, value) in attributes) Attribute(output, name, value);
		output.Write(">");
		output.Write(body);
		output.Write("</span>");
	}

	private static void Attribute(IBufferWriter<char> output, string name, int? value)
	{
		if (value is { } number) Attribute(output, name, number.ToString(CultureInfo.InvariantCulture));
	}

	private static void Attribute(IBufferWriter<char> output, string name, string? value)
	{
		if (value is null) return;
		output.Write(" ");
		output.Write(new HtmlAttribute(name, value).ToString());
	}
}
