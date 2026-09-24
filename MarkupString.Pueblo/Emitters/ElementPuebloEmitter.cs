using System.Buffers;
using System.Globalization;
using MarkupString.Html;
namespace MarkupString.Pueblo;

/// <summary>
/// Writes the shared vocabulary in the Pueblo client's own HTML extensions.
/// </summary>
/// <remarks>
/// Names and attributes are the client's own (<c>api/ChHtmSym.cpp</c>, <c>api/ChPaneTag.cpp</c> and the
/// sound module in <see href="https://github.com/uecasm/pueblo">uecasm/pueblo</see>). Pueblo has two
/// players, one for wave files and one for MIDI, and chooses between them by the file; it has no channel
/// for music as such, and it plays a sound once or loops it, without a count.
/// </remarks>
internal sealed class ElementPuebloEmitter(Type markupType) : IMarkupEmitter
{
	/// <summary>The markup types this emitter writes.</summary>
	internal static readonly Type[] Types =
	[
		typeof(SoundMarkup), typeof(SoundStopMarkup), typeof(ClearScreenMarkup), typeof(PrefetchMarkup),
		typeof(ImageMarkup), typeof(PaneMarkup), typeof(PreformattedMarkup),
	];

	/// <summary>The pane name Pueblo reads as "back to wherever text was going before".</summary>
	internal const string PreviousPane = "_previous";

	public Type MarkupType { get; } = markupType;

	public MarkupFormat Format => MarkupFormat.Pueblo;

	public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(markup);
		ArgumentNullException.ThrowIfNull(output);

		switch (markup)
		{
			case SoundMarkup sound:
				Tag(output, "img",
					("xch_sound", sound.Loops ? "loop" : "play"),
					("href", sound.Source),
					("xch_volume", sound.Volume?.ToString(CultureInfo.InvariantCulture)));
				return;

			case SoundStopMarkup stop:
				Tag(output, "img",
					("xch_sound", "stop"),
					("xch_device", stop.Channel switch
					{
						SoundChannel.Effects => "wave",
						SoundChannel.Music => "midi",
						_ => null,
					}));
				return;

			case ClearScreenMarkup:
				Tag(output, "xch_page", ("clear", "text"));
				return;

			case PrefetchMarkup prefetch:
				// The client ignores a prefetch with no likelihood of being used.
				Tag(output, "xch_prefetch", ("href", prefetch.Source), ("xch_prob", "100"));
				return;

			case ImageMarkup image:
				Tag(output, "img",
					("src", image.Source),
					("alt", image.Description ?? string.Empty),
					("width", image.Width?.ToString(CultureInfo.InvariantCulture)),
					("height", image.Height?.ToString(CultureInfo.InvariantCulture)),
					("align", image.Align?.ToString().ToLowerInvariant()));
				return;

			case PreformattedMarkup:
				// The client is back on MUD-text conventions inside this: it breaks the lines itself, and
				// the font is fixed-width. PreformattedMarkup suspends the <BR> substitution to match.
				output.Write("<xch_mudtext>");
				output.Write(body);
				output.Write("</xch_mudtext>");
				return;

			case PaneMarkup pane:
				Tag(output, "xch_pane", ("action", "redirect"), ("name", pane.Name), ("panetitle", pane.Title));
				output.Write(body);
				Tag(output, "xch_pane", ("action", "redirect"), ("name", PreviousPane));
				return;

			default:
				output.Write(body);
				return;
		}
	}

	private static void Tag(IBufferWriter<char> output, string name, params ReadOnlySpan<(string Name, string? Value)> attributes)
	{
		output.Write("<");
		output.Write(name);
		foreach (var (attribute, value) in attributes)
		{
			if (value is null) continue;
			output.Write(" ");
			output.Write(new HtmlAttribute(attribute, value).ToString());
		}
		output.Write(">");
	}
}
