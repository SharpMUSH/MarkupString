namespace MarkupString.Ansi;

/// <summary>Installs this package's emitters and codec into a <see cref="MarkupRegistry"/>.</summary>
public static class AnsiRegistration
{
	/// <summary>
	/// Returns a registry that renders <see cref="AnsiMarkup"/> in all six formats and serialises
	/// it under kind <c>"ansi"</c>. Plain needs nothing: the body passes through.
	/// </summary>
	/// <remarks>
	/// The five emitters are <see cref="IMarkupSetEmitter"/>s — one per format, claiming the whole
	/// run — because the layers of a run have to fold into a single sequence or element. Layers
	/// this package does not own are delegated to their own emitters, so composing this with
	/// another package's registration works in either order.
	/// </remarks>
	public static MarkupRegistry WithAnsi(this MarkupRegistry registry)
	{
		ArgumentNullException.ThrowIfNull(registry);

		return registry
			.With(new AnsiSetEmitter())
			.With(new AnsiHtmlEmitter())
			.With(new AnsiPuebloEmitter())
			.With(new AnsiMxpEmitter())
			.With(new AnsiBBCodeEmitter())
			.With(new AnsiMarkupCodec())
			// A bell is not ANSI styling, but it is the same audience: a client that reads a control
			// character, or an HTML page that reads an element. BBCode and Plain have neither, and drop
			// the character with every other control.
			.With(new BellEmitter(MarkupFormat.Ansi))
			.With(new BellEmitter(MarkupFormat.Pueblo))
			.With(new BellEmitter(MarkupFormat.Mxp))
			.With(new BellEmitter(MarkupFormat.Html));
	}
}
