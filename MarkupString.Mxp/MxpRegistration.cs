namespace MarkupString.Mxp;

/// <summary>Installs this package's emitters and codec into a <see cref="MarkupRegistry"/>.</summary>
public static class MxpRegistration
{
	/// <summary>
	/// Returns a registry that writes an <see cref="MxpElement"/> as an MXP tag in
	/// <see cref="MarkupFormat.Mxp"/>, as the nearest HTML in <see cref="MarkupFormat.Html"/>, and as
	/// nothing in every other format — carrier included, so a terminal is sent neither a tag it would show
	/// as text nor the zero-width space it rode on.
	/// </summary>
	/// <remarks>
	/// A registry is cheap to build, so one per connection — carrying that client's answers — costs a
	/// dictionary copy, and one built without <paramref name="supports"/> serves every client that was
	/// never asked.
	/// To keep MXP's elements out of the browser entirely, add
	/// <c>new MxpSilentEmitter(MarkupFormat.Html)</c> after this; the last emitter registered for a format
	/// wins.
	/// </remarks>
	/// <param name="registry">The registry to add to.</param>
	/// <param name="supports">
	/// Which elements this connection's client said it can render, from MXP's <c>&lt;SUPPORT&gt;</c>
	/// exchange. Null writes every element, which is the right answer for a client that was never asked.
	/// An element it refuses is written as a format without MXP writes it: nothing for one that stands
	/// alone, and the content alone for one that wraps — so a <c>FRAME</c> a client cannot open does not
	/// take the text that followed it.
	/// </param>
	public static MarkupRegistry WithMxp(this MarkupRegistry registry, Func<MxpElement, bool>? supports = null)
	{
		ArgumentNullException.ThrowIfNull(registry);

		return registry
			.With(supports is null ? MxpElementEmitter.Instance : new MxpElementEmitter(supports))
			.With(MxpHtmlEmitter.Instance)
			.With(new MxpSilentEmitter(MarkupFormat.Ansi))
			.With(new MxpSilentEmitter(MarkupFormat.Pueblo))
			.With(new MxpSilentEmitter(MarkupFormat.Plain))
			.With(new MxpSilentEmitter(MarkupFormat.BBCode))
			.With(new MxpElementCodec());
	}
}
