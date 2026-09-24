using System.Buffers;
using System.Globalization;
namespace MarkupString.Mxp;

/// <summary>
/// Writes the shared vocabulary as MXP tags, holding each to what the client said it supports.
/// </summary>
/// <remarks>
/// An element the client refused is written as a format without MXP writes it: nothing for a point,
/// and the wrapped text for the rest — so a picture the client will not show still leaves its
/// description, and a pane it cannot open still leaves its text in the main window.
/// </remarks>
internal sealed class ElementMxpEmitter(Type markupType, Func<string, bool>? supports) : IMarkupEmitter
{
	/// <summary>The markup types this emitter writes.</summary>
	internal static readonly Type[] Types =
	[
		typeof(SoundMarkup), typeof(SoundStopMarkup), typeof(ImageMarkup), typeof(PaneMarkup),
		typeof(ExpireLinksMarkup), typeof(RelocateMarkup), typeof(LoginPromptMarkup),
		typeof(VariableMarkup), typeof(GaugeMarkup), typeof(StatusMarkup),
	];

	public Type MarkupType { get; } = markupType;

	public MarkupFormat Format => MarkupFormat.Mxp;

	public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(markup);
		ArgumentNullException.ThrowIfNull(output);

		var tag = new MxpTag(output);
		switch (markup)
		{
			case SoundMarkup sound:
				{
					var name = sound.Channel == SoundChannel.Music ? "MUSIC" : "SOUND";
					if (!Supports(name)) return;

					var (file, directory) = Split(sound.Source);
					tag.Open(name).Positional(file)
						.Named("V", sound.Volume)
						.Named("L", sound.Repeats)
						.Named("C", sound.Channel == SoundChannel.Music && sound.Continues ? "1" : null)
						.Named("U", directory)
						.Close();
					return;
				}

			case SoundStopMarkup stop:
				if (stop.Channel is not SoundChannel.Music && Supports("SOUND")) tag.Open("SOUND").Positional("Off").Close();
				if (stop.Channel is not SoundChannel.Effects && Supports("MUSIC")) tag.Open("MUSIC").Positional("Off").Close();
				return;

			case ImageMarkup image:
				{
					if (!Supports("IMAGE"))
					{
						output.Write(body);
						return;
					}

					var (file, directory) = Split(image.Source);
					tag.Open("IMAGE").Positional(file)
						.Named("URL", directory)
						.Named("W", image.Width)
						.Named("H", image.Height)
						.Named("ALIGN", image.Align?.ToString().ToUpperInvariant())
						.Close();
					return;
				}

			case PaneMarkup pane:
				if (!Supports("FRAME") || !Supports("DEST"))
				{
					output.Write(body);
					return;
				}

				// One frame and one destination around the whole pane, however many runs its content is
				// in: a DEST per run would open and close the redirect around every word.
				if (context.StartsRegion(markup))
				{
					tag.Open("FRAME").Positional(pane.Name).Named("TITLE", pane.Title).Close();
					tag.Open("DEST").Positional(pane.Name).Close();
				}

				output.Write(body);
				if (context.EndsRegion(markup)) output.Write("</DEST>");
				return;

			case ExpireLinksMarkup expire:
				if (Supports("EXPIRE")) tag.Open("EXPIRE").Positional(expire.Group).Close();
				return;

			case RelocateMarkup relocate:
				if (!Supports("RELOCATE")) return;
				tag.Open("RELOCATE").Positional(relocate.Host)
					.Positional(relocate.Port.ToString(CultureInfo.InvariantCulture))
					.Flag("QUIET", relocate.Quiet)
					.Close();
				return;

			case LoginPromptMarkup login:
				{
					var name = login.Field == LoginField.Password ? "PASSWORD" : "USER";
					if (Supports(name)) tag.Open(name).Close();
					return;
				}

			case VariableMarkup variable:
				if (!Supports("VAR"))
				{
					output.Write(body);
					return;
				}

				if (context.StartsRegion(markup)) tag.Open("VAR").Positional(variable.Name).Close();
				output.Write(body);
				if (context.EndsRegion(markup)) output.Write("</VAR>");
				return;

			case GaugeMarkup gauge:
				if (!Supports("GAUGE"))
				{
					output.Write(body);
					return;
				}

				tag.Open("GAUGE").Positional(gauge.Variable)
					.Named("MAX", gauge.Maximum)
					.Named("CAPTION", gauge.Caption)
					.Named("COLOR", gauge.Color)
					.Close();
				return;

			case StatusMarkup status:
				if (!Supports("STAT"))
				{
					output.Write(body);
					return;
				}

				tag.Open("STAT").Positional(status.Variable)
					.Named("MAX", status.Maximum)
					.Named("CAPTION", status.Caption)
					.Close();
				return;

			default:
				output.Write(body);
				return;
		}
	}

	private bool Supports(string element) => supports?.Invoke(element) ?? true;

	/// <summary>
	/// MXP names a file and, separately, the address of the directory to fetch it from when the client
	/// does not have it. An absolute address is split at its last slash; anything else is a file name.
	/// </summary>
	internal static (string File, string? Directory) Split(string source)
	{
		if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
			return (source, null);

		var slash = source.LastIndexOf('/');
		return slash < 0 || slash == source.Length - 1 || slash < uri.Scheme.Length + 3
			? (source, null)
			: (source[(slash + 1)..], source[..(slash + 1)]);
	}
}

/// <summary>Writes one MXP tag: its name, then its arguments, each quoted when it has to be.</summary>
internal readonly ref struct MxpTag(IBufferWriter<char> output)
{
	private readonly IBufferWriter<char> _output = output;

	public MxpTag Open(string name)
	{
		_output.Write("<");
		_output.Write(name);
		return this;
	}

	public MxpTag Positional(string? value)
	{
		if (value is null) return this;
		_output.Write(" ");
		WriteValue(value);
		return this;
	}

	public MxpTag Named(string name, int? value) =>
		value is { } number ? Named(name, number.ToString(CultureInfo.InvariantCulture)) : this;

	public MxpTag Named(string name, string? value)
	{
		if (value is null) return this;
		_output.Write(" ");
		_output.Write(name);
		_output.Write("=");
		WriteValue(value);
		return this;
	}

	public MxpTag Flag(string name, bool set)
	{
		if (!set) return this;
		_output.Write(" ");
		_output.Write(name);
		return this;
	}

	public void Close() => _output.Write(">");

	/// <summary>
	/// MXP separates arguments with whitespace and reads a quoted value as one, so a value carrying
	/// whitespace, a quote or a bracket is quoted, and the characters that are markup inside it written as
	/// entities.
	/// </summary>
	private void WriteValue(string value)
	{
		if (value.Length > 0 && value.AsSpan().IndexOfAny(" \t\r\n\"'<>&") < 0)
		{
			_output.Write(value);
			return;
		}

		_output.Write("\"");
		_output.Write(value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;"));
		_output.Write("\"");
	}
}
