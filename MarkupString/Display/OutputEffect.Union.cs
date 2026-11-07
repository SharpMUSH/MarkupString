using MarkupString.Decoding;

namespace MarkupString.Display;

/// <summary>
/// Something output asks of the client besides text to show: a sound, a bell, a title, links to expire. It is
/// one of the types that already say it:
/// <list type="bullet">
/// <item>a point from the text: <see cref="BellMarkup"/>, <see cref="ClearScreenMarkup"/>, <see cref="SoundMarkup"/>,
/// <see cref="SoundStopMarkup"/>, <see cref="ExpireLinksMarkup"/>, <see cref="PrefetchMarkup"/>,
/// <see cref="LoginPromptMarkup"/>, <see cref="RelocateMarkup"/>, or any other <see cref="IPointMarkup"/>, such as a plugin's;</item>
/// <item>what a decoder raised: <see cref="TitleEvent"/>, <see cref="PaletteEvent"/>, <see cref="ClipboardEvent"/>,
/// <see cref="DownloadEvent"/>, <see cref="ReplyEvent"/>, <see cref="ProtocolEvent"/>, or any other <see cref="DecodeEvent"/>;</item>
/// <item>a <see cref="VariableEvent"/>, from a decoder or from the text a <see cref="VariableMarkup"/> covers.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>A host handles markup frames, decoded ANSI, MXP and Pueblo with one switch:
/// <c>effect switch { SoundMarkup sound =&gt; …, TitleEvent title =&gt; …, IPointMarkup other =&gt; …, … }</c>.
/// <see cref="IPointMarkup"/> and <see cref="DecodeEvent"/> are the last cases, so a plugin's own point or
/// event still arrives.</para>
/// <para>A value of a case type converts to one implicitly. On .NET 10, where the runtime has no unions, the
/// same type is built with copies of the compiler's union attribute and interface, and <c>Value</c> holds the
/// case.</para>
/// </remarks>
public union OutputEffect(
	BellMarkup, ClearScreenMarkup, SoundMarkup, SoundStopMarkup, ExpireLinksMarkup, PrefetchMarkup, LoginPromptMarkup,
	RelocateMarkup, VariableEvent, TitleEvent, PaletteEvent, ClipboardEvent, DownloadEvent, ReplyEvent, ProtocolEvent,
	IPointMarkup, DecodeEvent)
{
	/// <summary>An effect of a point: a bell, a sound, a plugin's own.</summary>
	public static OutputEffect Of(IPointMarkup point) => new(point ?? throw new ArgumentNullException(nameof(point)));

	/// <summary>An effect of something a decoder raised.</summary>
	public static OutputEffect Of(DecodeEvent decoded) => new(decoded ?? throw new ArgumentNullException(nameof(decoded)));
}
