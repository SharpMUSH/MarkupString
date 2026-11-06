namespace MarkupString;

/// <summary>One choice of a command menu.</summary>
/// <param name="Label">What the menu shows.</param>
/// <param name="Command">What choosing it sends.</param>
public sealed record CommandChoice(string Label, string Command);

/// <summary>
/// Makes the text it wraps a menu of commands: a click sends the first, and a client with menus offers the rest.
/// MXP writes it as <c>&lt;SEND href="a|b" hint="…|A|B"&gt;</c>.
/// </summary>
/// <param name="Choices">The commands, the default first.</param>
/// <param name="Hint">A hint to show on hover, or null.</param>
/// <param name="Prompt">Whether a click puts the command in the input line instead of sending it.</param>
public sealed record CommandMenuMarkup(IReadOnlyList<CommandChoice> Choices, string? Hint = null, bool Prompt = false) : IMarkup
{
	/// <summary>Value equality, the choices compared item by item.</summary>
	public bool Equals(CommandMenuMarkup? other) =>
		other is not null
		&& Prompt == other.Prompt
		&& string.Equals(Hint, other.Hint, StringComparison.Ordinal)
		&& Choices.SequenceEqual(other.Choices);

	/// <inheritdoc/>
	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(Hint, StringComparer.Ordinal);
		hash.Add(Prompt);
		foreach (var choice in Choices) hash.Add(choice);
		return hash.ToHashCode();
	}
}

/// <summary>
/// Paints the background behind the text it wraps, as a search match or a selection is shown. Unlike a colour
/// layer it says nothing about the text's own colour.
/// </summary>
/// <param name="Color">The background.</param>
public sealed record HighlightMarkup(RgbColor Color) : IMarkup;
