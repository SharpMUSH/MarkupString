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
	/// To keep MXP's elements out of the browser entirely, add
	/// <c>new MxpSilentEmitter(MarkupFormat.Html)</c> after this; the last emitter registered for a format
	/// wins.
	/// </remarks>
	public static MarkupRegistry WithMxp(this MarkupRegistry registry)
	{
		ArgumentNullException.ThrowIfNull(registry);

		return registry
			.With(MxpElementEmitter.Instance)
			.With(MxpHtmlEmitter.Instance)
			.With(new MxpSilentEmitter(MarkupFormat.Ansi))
			.With(new MxpSilentEmitter(MarkupFormat.Pueblo))
			.With(new MxpSilentEmitter(MarkupFormat.Plain))
			.With(new MxpSilentEmitter(MarkupFormat.BBCode))
			.With(new MxpElementCodec());
	}
}
