namespace MarkupString.Pueblo;

/// <summary>Installs this package's emitters into a <see cref="MarkupRegistry"/>.</summary>
public static class PuebloRegistration
{
	/// <summary>
	/// Returns a registry that writes the shared vocabulary in <see cref="MarkupFormat.Pueblo"/> as the
	/// Pueblo client reads it: a sound as <c>&lt;img xch_sound&gt;</c>, a pane as
	/// <c>&lt;xch_pane action="redirect"&gt;</c> and back, clearing the screen as
	/// <c>&lt;xch_page&gt;</c>, a prefetch as <c>&lt;xch_prefetch&gt;</c>, and a picture as
	/// <c>&lt;img&gt;</c>.
	/// </summary>
	/// <remarks>
	/// Pueblo has no gauges, status bar or variables; those write their text. Plain HTML tags stay
	/// <c>WithHtml()</c>'s, and styling and command links <c>WithAnsi()</c>'s.
	/// </remarks>
	public static MarkupRegistry WithPueblo(this MarkupRegistry registry)
	{
		ArgumentNullException.ThrowIfNull(registry);

		foreach (var type in ElementPuebloEmitter.Types) registry = registry.With(new ElementPuebloEmitter(type));
		return registry;
	}
}
