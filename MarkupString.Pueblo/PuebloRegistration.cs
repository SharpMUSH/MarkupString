namespace MarkupString.Pueblo;

/// <summary>Installs this package's emitters and codec into a <see cref="MarkupRegistry"/>.</summary>
public static class PuebloRegistration
{
	/// <summary>
	/// Returns a registry that writes an <see cref="PuebloElement"/> as an Pueblo tag in
	/// <see cref="MarkupFormat.Pueblo"/>, as the nearest HTML in <see cref="MarkupFormat.Html"/>, and as
	/// nothing in every other format — carrier included, so a terminal is sent neither a tag it would show
	/// as text nor the zero-width space it rode on.
	/// </summary>
	/// <remarks>
	/// A registry is cheap to build, so one per connection — carrying that client's answers — costs a
	/// dictionary copy, and one built without <paramref name="supports"/> serves every client that was
	/// never asked.
	/// To keep Pueblo's elements out of the browser entirely, add
	/// <c>new PuebloSilentEmitter(MarkupFormat.Html)</c> after this; the last emitter registered for a format
	/// wins.
	/// </remarks>
	/// <param name="registry">The registry to add to.</param>
	/// <param name="supports">
	/// Which elements to write, for a consumer that knows a client renders only some of them. Null writes
	/// every element, which is the ordinary case: Pueblo has no exchange that asks.
	/// An element it refuses is written as a format without Pueblo writes it: nothing for one that stands
	/// alone, and the content alone for one that wraps — so a <c>FRAME</c> a client cannot open does not
	/// take the text that followed it.
	/// </param>
	public static MarkupRegistry WithPueblo(this MarkupRegistry registry, Func<PuebloElement, bool>? supports = null)
	{
		ArgumentNullException.ThrowIfNull(registry);

		return registry
			.With(supports is null ? PuebloElementEmitter.Instance : new PuebloElementEmitter(supports))
			.With(PuebloHtmlEmitter.Instance)
			.With(new PuebloSilentEmitter(MarkupFormat.Ansi))
			.With(new PuebloSilentEmitter(MarkupFormat.Mxp))
			.With(new PuebloSilentEmitter(MarkupFormat.Plain))
			.With(new PuebloSilentEmitter(MarkupFormat.BBCode))
			.With(new PuebloElementCodec());
	}
}
