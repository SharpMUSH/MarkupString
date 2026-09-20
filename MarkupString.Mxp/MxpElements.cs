using System.Collections.Immutable;
using System.Globalization;

namespace MarkupString.Mxp;

/// <summary>Where an <see cref="MxpElements.Image"/> sits against the text around it.</summary>
public enum MxpAlign
{
	/// <summary>Unset; the client decides.</summary>
	Default,
	Left,
	Right,
	Top,
	Middle,
	Bottom,
}

/// <summary>What a <see cref="MxpElements.Frame"/> call does to the frame it names.</summary>
public enum MxpFrameAction
{
	/// <summary>Create the frame, or show it again.</summary>
	Open,
	/// <summary>Close it.</summary>
	Close,
	/// <summary>Send everything that follows to it.</summary>
	Redirect,
}

/// <summary>
/// MXP's own elements, as the specification defines them: the ones that play a sound, show an image,
/// draw a gauge, open a frame, and the rest.
/// </summary>
/// <remarks>
/// Each returns the text the element stands in, so it composes with everything else:
/// <c>MarkupText.Concat(MxpElements.Sound("door.wav"), MarkupText.Plain("The door creaks."))</c>. An
/// argument left unset is not written, so a client sees the specification's own defaults.
/// </remarks>
public static class MxpElements
{
	/// <summary>
	/// <c>&lt;SOUND&gt;</c> — plays a sound once. <paramref name="volume"/>, <paramref name="loops"/> and
	/// <paramref name="priority"/> are MXP's <c>V</c>, <c>L</c> and <c>P</c>; <paramref name="type"/> and
	/// <paramref name="url"/> its <c>T</c> and <c>U</c>. Pass <paramref name="file"/> as <c>Off</c> to stop
	/// whatever is playing.
	/// </summary>
	public static MarkupText Sound(
		string file, int? volume = null, int? loops = null, int? priority = null, string? type = null, string? url = null) =>
		MxpElement.Standalone("SOUND", [.. Media(file, volume, loops, priority, type, url, null)]);

	/// <summary>
	/// <c>&lt;MUSIC&gt;</c> — plays music, which differs from a sound in that a client keeps it going.
	/// <paramref name="continues"/> is MXP's <c>C</c>: whether a repeat of the same file plays on rather
	/// than restarting.
	/// </summary>
	public static MarkupText Music(
		string file, int? volume = null, int? loops = null, bool? continues = null, string? type = null, string? url = null) =>
		MxpElement.Standalone("MUSIC", [.. Media(file, volume, loops, null, type, url, continues)]);

	/// <summary>
	/// <c>&lt;IMAGE&gt;</c> — shows an image. <paramref name="height"/>, <paramref name="width"/>,
	/// <paramref name="horizontalSpace"/> and <paramref name="verticalSpace"/> are MXP's <c>H</c>,
	/// <c>W</c>, <c>HSPACE</c> and <c>VSPACE</c>; <paramref name="isMap"/> its <c>ISMAP</c>, which makes
	/// the image clickable.
	/// </summary>
	public static MarkupText Image(
		string file,
		string? url = null,
		string? type = null,
		string? height = null,
		string? width = null,
		string? horizontalSpace = null,
		string? verticalSpace = null,
		MxpAlign align = MxpAlign.Default,
		bool isMap = false)
	{
		var arguments = ImmutableArray.CreateBuilder<MxpArgument>();
		arguments.Add(new MxpArgument(null, file));
		Add(arguments, "URL", url);
		Add(arguments, "T", type);
		Add(arguments, "H", height);
		Add(arguments, "W", width);
		Add(arguments, "HSPACE", horizontalSpace);
		Add(arguments, "VSPACE", verticalSpace);
		if (align != MxpAlign.Default) Add(arguments, "ALIGN", align.ToString().ToUpperInvariant());
		if (isMap) arguments.Add(new MxpArgument("ISMAP", string.Empty));
		return MxpElement.Standalone("IMAGE", [.. arguments]);
	}

	/// <summary>
	/// <c>&lt;GAUGE&gt;</c> — draws a bar from an entity the client already holds, so it redraws itself as
	/// that entity changes. <paramref name="entity"/> and <paramref name="max"/> name entities, not values.
	/// </summary>
	public static MarkupText Gauge(string entity, string? max = null, string? caption = null, string? colour = null)
	{
		var arguments = ImmutableArray.CreateBuilder<MxpArgument>();
		arguments.Add(new MxpArgument(null, entity));
		Add(arguments, "MAX", max);
		Add(arguments, "CAPTION", caption);
		Add(arguments, "COLOR", colour);
		return MxpElement.Standalone("GAUGE", [.. arguments]);
	}

	/// <summary><c>&lt;STAT&gt;</c> — the same, as status-bar text rather than a bar.</summary>
	public static MarkupText Stat(string entity, string? max = null, string? caption = null)
	{
		var arguments = ImmutableArray.CreateBuilder<MxpArgument>();
		arguments.Add(new MxpArgument(null, entity));
		Add(arguments, "MAX", max);
		Add(arguments, "CAPTION", caption);
		return MxpElement.Standalone("STAT", [.. arguments]);
	}

	/// <summary>
	/// <c>&lt;EXPIRE&gt;</c> — takes back the links written under <paramref name="name"/>, or every link so
	/// far when no name is given, so an exit that has closed stops being clickable.
	/// </summary>
	public static MarkupText Expire(string? name = null) =>
		name is null ? MxpElement.Standalone("EXPIRE") : MxpElement.Standalone("EXPIRE", new MxpArgument(null, name));

	/// <summary><c>&lt;USER&gt;</c> — asks the client to send the username it stored for this world.</summary>
	public static MarkupText User() => MxpElement.Standalone("USER");

	/// <summary><c>&lt;PASSWORD&gt;</c> — asks the client to send the password it stored for this world.</summary>
	public static MarkupText Password() => MxpElement.Standalone("PASSWORD");

	/// <summary><c>&lt;NOBR&gt;</c> — the newline after this does not break the line.</summary>
	public static MarkupText NoBreak() => MxpElement.Standalone("NOBR");

	/// <summary><c>&lt;SBR&gt;</c> — a soft break: a space the client may wrap at.</summary>
	public static MarkupText SoftBreak() => MxpElement.Standalone("SBR");

	/// <summary>
	/// <c>&lt;RELOCATE&gt;</c> — tells the client to connect somewhere else. <paramref name="quiet"/> is
	/// MXP's <c>QUIET</c>, which asks it not to say so.
	/// </summary>
	public static MarkupText Relocate(string host, int port, bool quiet = false)
	{
		var arguments = ImmutableArray.CreateBuilder<MxpArgument>();
		arguments.Add(new MxpArgument(null, host));
		arguments.Add(new MxpArgument(null, port.ToString(CultureInfo.InvariantCulture)));
		if (quiet) arguments.Add(new MxpArgument("QUIET", string.Empty));
		return MxpElement.Standalone("RELOCATE", [.. arguments]);
	}

	/// <summary>
	/// <c>&lt;FRAME&gt;</c> — opens, closes or redirects a window of its own, wrapping the text that goes
	/// to it. Everything but <paramref name="name"/> and <paramref name="action"/> is layout the client
	/// applies when it creates the frame.
	/// </summary>
	public static MarkupText Frame(
		MarkupText content,
		string name,
		MxpFrameAction action = MxpFrameAction.Open,
		string? title = null,
		bool isInternal = false,
		MxpAlign align = MxpAlign.Default,
		string? left = null,
		string? top = null,
		string? width = null,
		string? height = null,
		bool? scrolling = null,
		bool floating = false)
	{
		var arguments = ImmutableArray.CreateBuilder<MxpArgument>();
		arguments.Add(new MxpArgument(null, name));
		Add(arguments, "ACTION", action.ToString().ToUpperInvariant());
		Add(arguments, "TITLE", title);
		if (isInternal) arguments.Add(new MxpArgument("INTERNAL", string.Empty));
		if (align != MxpAlign.Default) Add(arguments, "ALIGN", align.ToString().ToUpperInvariant());
		Add(arguments, "LEFT", left);
		Add(arguments, "TOP", top);
		Add(arguments, "WIDTH", width);
		Add(arguments, "HEIGHT", height);
		if (scrolling is { } scrolls) Add(arguments, "SCROLLING", scrolls ? "yes" : "no");
		if (floating) arguments.Add(new MxpArgument("FLOATING", string.Empty));
		return MxpElement.Wrapping("FRAME", content, [.. arguments]);
	}

	/// <summary>
	/// <c>&lt;VAR&gt;</c> — shows <paramref name="value"/> and keeps it in the client under
	/// <paramref name="name"/>, where a <see cref="Gauge"/> or a <see cref="Stat"/> can read it.
	/// </summary>
	public static MarkupText Var(
		MarkupText value,
		string name,
		string? description = null,
		bool isPrivate = false,
		bool publish = false,
		bool delete = false)
	{
		var arguments = ImmutableArray.CreateBuilder<MxpArgument>();
		arguments.Add(new MxpArgument(null, name));
		Add(arguments, "DESC", description);
		if (isPrivate) arguments.Add(new MxpArgument("PRIVATE", string.Empty));
		if (publish) arguments.Add(new MxpArgument("PUBLISH", string.Empty));
		if (delete) arguments.Add(new MxpArgument("DELETE", string.Empty));
		return MxpElement.Wrapping("VAR", value, [.. arguments]);
	}

	private static ImmutableArray<MxpArgument> Media(
		string file, int? volume, int? loops, int? priority, string? type, string? url, bool? continues)
	{
		var arguments = ImmutableArray.CreateBuilder<MxpArgument>();
		arguments.Add(new MxpArgument(null, file));
		if (volume is { } v) Add(arguments, "V", v.ToString(CultureInfo.InvariantCulture));
		if (loops is { } l) Add(arguments, "L", l.ToString(CultureInfo.InvariantCulture));
		if (priority is { } p) Add(arguments, "P", p.ToString(CultureInfo.InvariantCulture));
		if (continues is { } c) Add(arguments, "C", c ? "1" : "0");
		Add(arguments, "T", type);
		Add(arguments, "U", url);
		return [.. arguments];
	}

	private static void Add(ImmutableArray<MxpArgument>.Builder arguments, string name, string? value)
	{
		if (value is not null) arguments.Add(new MxpArgument(name, value));
	}
}
