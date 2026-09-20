using System.Collections.Immutable;
using System.Globalization;

namespace MarkupString.Pueblo;

/// <summary>How a Pueblo client reads what follows a <see cref="PuebloElements.Mode"/> switch.</summary>
public enum PuebloMode
{
	/// <summary>Plain MUD text: line ends break lines, and the font is fixed-width.</summary>
	Text,
	/// <summary>HTML, with MUD-text conventions still applied.</summary>
	Html,
	/// <summary>HTML alone, which is what a world switches to once it is sending markup.</summary>
	PureHtml,
}

/// <summary>What a <see cref="PuebloElements.Page"/> instruction clears.</summary>
public enum PuebloClear
{
	/// <summary>The text the player has already been shown.</summary>
	Text,
	/// <summary>Everything, the images included.</summary>
	All,
}

/// <summary>
/// Pueblo's own extensions, as its client reads them: the pane, the page instruction, the sound and
/// alert attributes, prefetching, and the mode switches a world uses to enter HTML.
/// </summary>
/// <remarks>
/// Names are the client's own (<c>api/ChHtmSym.cpp</c> and the sound module in
/// <see href="https://github.com/uecasm/pueblo">uecasm/pueblo</see>). Plain HTML is not here — Pueblo
/// reads an HTML subset, and <c>HtmlMarkup</c> writes that — and neither are styling and links, which
/// travel as <c>AnsiMarkup</c> and are already written per dialect.
/// </remarks>
public static class PuebloElements
{
	/// <summary>
	/// <c>&lt;xch_page&gt;</c> — clears what the player has been shown. The handshake sends this as part
	/// of moving a client into HTML.
	/// </summary>
	public static MarkupText Page(PuebloClear clear = PuebloClear.Text) =>
		PuebloElement.Standalone("xch_page", new PuebloAttribute("clear", clear.ToString().ToLowerInvariant()));

	/// <summary>
	/// <c>&lt;img xch_mode=…&gt;</c> — how the client reads what follows. A world announces
	/// <see cref="PuebloMode.PureHtml"/> once it is sending markup rather than MUD text.
	/// </summary>
	public static MarkupText Mode(PuebloMode mode) =>
		PuebloElement.Standalone("img", new PuebloAttribute("xch_mode", mode switch
		{
			PuebloMode.PureHtml => "purehtml",
			PuebloMode.Html => "html",
			_ => "text",
		}));

	/// <summary>
	/// <c>&lt;xch_mudtext&gt;</c> — marks text the client should treat as it treats a non-HTML world's:
	/// line ends break lines, and the font is fixed-width.
	/// </summary>
	/// <remarks>
	/// The <c>&lt;/xch_mudtext&gt;</c> that takes a whole connection out of MUD-text handling is part of
	/// the Pueblo handshake, which belongs to the telnet layer rather than here.
	/// </remarks>
	public static MarkupText MudText(MarkupText content) => PuebloElement.Wrapping("xch_mudtext", content);

	/// <summary>
	/// <c>&lt;xch_pane&gt;</c> — a pane of its own, wrapping what goes to it. MXP's <c>FRAME</c> was
	/// modelled on this.
	/// </summary>
	public static MarkupText Pane(
		MarkupText content,
		string name,
		string? title = null,
		string? minimumWidth = null,
		string? minimumHeight = null,
		string? alignTo = null,
		bool? scrolling = null,
		string? options = null)
	{
		var attributes = ImmutableArray.CreateBuilder<PuebloAttribute>();
		attributes.Add(new PuebloAttribute("name", name));
		Add(attributes, "panetitle", title);
		Add(attributes, "minwidth", minimumWidth);
		Add(attributes, "minheight", minimumHeight);
		Add(attributes, "alignto", alignTo);
		if (scrolling is { } scrolls) Add(attributes, "scrolling", scrolls ? "yes" : "no");
		Add(attributes, "options", options);
		return PuebloElement.Wrapping("xch_pane", content, [.. attributes]);
	}

	/// <summary>
	/// <c>&lt;img xch_sound=…&gt;</c> — plays a sound. <paramref name="volume"/> and
	/// <paramref name="device"/> are the client's <c>xch_volume</c> and <c>xch_device</c>.
	/// </summary>
	public static MarkupText Sound(string file, int? volume = null, string? device = null) =>
		SoundLike("xch_sound", file, volume, device);

	/// <summary><c>&lt;img xch_alert=…&gt;</c> — the same, for a sound that is asking for attention.</summary>
	public static MarkupText Alert(string file, int? volume = null, string? device = null) =>
		SoundLike("xch_alert", file, volume, device);

	/// <summary><c>&lt;img xch_speech=…&gt;</c> — text for the client to speak.</summary>
	public static MarkupText Speech(string text, int? volume = null, string? device = null) =>
		SoundLike("xch_speech", text, volume, device);

	/// <summary>
	/// <c>&lt;xch_prefetch&gt;</c> — asks the client to fetch something now that it will want soon.
	/// </summary>
	public static MarkupText Prefetch(string source) =>
		PuebloElement.Standalone("xch_prefetch", new PuebloAttribute("src", source));

	/// <summary>
	/// <c>&lt;img&gt;</c> with Pueblo's own attributes: a picture that can carry a command, a tooltip, and
	/// the graph hook its VRML worlds use.
	/// </summary>
	public static MarkupText Image(
		string source,
		string? command = null,
		string? hint = null,
		string? graph = null,
		string? width = null,
		string? height = null)
	{
		var attributes = ImmutableArray.CreateBuilder<PuebloAttribute>();
		attributes.Add(new PuebloAttribute("src", source));
		Add(attributes, "xch_cmd", command);
		Add(attributes, "xch_hint", hint);
		Add(attributes, "xch_graph", graph);
		Add(attributes, "width", width);
		Add(attributes, "height", height);
		return PuebloElement.Standalone("img", [.. attributes]);
	}

	private static MarkupText SoundLike(string attribute, string value, int? volume, string? device)
	{
		var attributes = ImmutableArray.CreateBuilder<PuebloAttribute>();
		attributes.Add(new PuebloAttribute(attribute, value));
		if (volume is { } level) Add(attributes, "xch_volume", level.ToString(CultureInfo.InvariantCulture));
		Add(attributes, "xch_device", device);
		return PuebloElement.Standalone("img", [.. attributes]);
	}

	private static void Add(ImmutableArray<PuebloAttribute>.Builder attributes, string name, string? value)
	{
		if (value is not null) attributes.Add(new PuebloAttribute(name, value));
	}
}
