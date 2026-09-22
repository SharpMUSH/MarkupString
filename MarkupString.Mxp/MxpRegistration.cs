namespace MarkupString.Mxp;

/// <summary>Installs this package's emitters into a <see cref="MarkupRegistry"/>.</summary>
public static class MxpRegistration
{
	/// <summary>
	/// The MXP elements this package writes, as MXP names them — the list to ask a client about with
	/// <c>&lt;SUPPORT&gt;</c>.
	/// </summary>
	public static IReadOnlyList<string> Elements { get; } =
	[
		"SOUND", "MUSIC", "IMAGE", "FRAME", "DEST", "EXPIRE", "RELOCATE", "USER", "PASSWORD", "VAR", "GAUGE", "STAT",
	];

	/// <summary>
	/// Returns a registry that writes the shared vocabulary as MXP tags in <see cref="MarkupFormat.Mxp"/>:
	/// a sound as <c>&lt;SOUND&gt;</c>, a picture as <c>&lt;IMAGE&gt;</c>, a pane as a <c>&lt;FRAME&gt;</c>
	/// and a <c>&lt;DEST&gt;</c>, and so on through <see cref="Elements"/>.
	/// </summary>
	/// <remarks>
	/// <para>These are MXP's secure elements, which a client acts on only on a line in secure mode; pair
	/// this with <c>WithMxpSecureLines()</c>, or frame the lines yourself.</para>
	/// <para>MXP lets a client say what it can render, and <paramref name="supports"/> is where that
	/// answer comes in: it is asked with an element's name from <see cref="Elements"/>, upper case, and an
	/// element it refuses is written as a format without MXP writes it — nothing for a sound, the
	/// description for a picture, the text in the main window for a pane. The answer belongs to one
	/// connection, so a server builds a registry per answer. With no predicate, every element is written,
	/// which is right for a client that was never asked.</para>
	/// </remarks>
	/// <param name="registry">The registry to add to.</param>
	/// <param name="supports">Whether the client supports an element, by its MXP name; every element when null.</param>
	public static MarkupRegistry WithMxp(this MarkupRegistry registry, Func<string, bool>? supports = null)
	{
		ArgumentNullException.ThrowIfNull(registry);

		foreach (var type in ElementMxpEmitter.Types) registry = registry.With(new ElementMxpEmitter(type, supports));
		return registry;
	}
}
