namespace MarkupString.Display;

/// <summary>What clicking a stretch of text does.</summary>
public enum InteractionKind
{
	/// <summary>Opens <see cref="Interaction.Target"/>, an address.</summary>
	Url,

	/// <summary>Sends <see cref="Interaction.Target"/> to the game as a command.</summary>
	Command,

	/// <summary>Puts <see cref="Interaction.Target"/> in the input line, for the player to finish and send.</summary>
	Prompt,
}

/// <summary>A clickable stretch of text, resolved from whichever markup made it clickable.</summary>
/// <param name="Kind">What a click does.</param>
/// <param name="Target">The address or command.</param>
public sealed record Interaction(InteractionKind Kind, string Target)
{
	/// <summary>A hint to show on hover, or null.</summary>
	public string? Hint { get; init; }

	/// <summary>A menu of further choices (MXP <c>&lt;SEND href="a|b"&gt;</c>), or null. <see cref="Target"/> is the first.</summary>
	public IReadOnlyList<CommandChoice>? Choices { get; init; }

	/// <summary>
	/// Text with the same group is one link for hover and focus, even across lines (as an OSC 8 <c>id</c> groups them), as a contributor sets it. Null groups
	/// by <see cref="Target"/> alone.
	/// </summary>
	public string? Group { get; init; }

	/// <summary>Value equality, the choices compared item by item.</summary>
	public bool Equals(Interaction? other) =>
		other is not null
		&& Kind == other.Kind
		&& string.Equals(Target, other.Target, StringComparison.Ordinal)
		&& string.Equals(Hint, other.Hint, StringComparison.Ordinal)
		&& string.Equals(Group, other.Group, StringComparison.Ordinal)
		&& (ReferenceEquals(Choices, other.Choices) || (Choices is not null && other.Choices is not null && Choices.SequenceEqual(other.Choices)));

	/// <inheritdoc/>
	public override int GetHashCode() => HashCode.Combine(Kind, Target, Hint, Group, Choices?.Count ?? -1);
}

/// <summary>
/// How a stretch of text looks once every layer of its markup is folded together and resolved to colours: what a
/// cell or a span is drawn with. Colours are RGB, already swapped when the text is inverse.
/// </summary>
public readonly record struct DisplayStyle
{
	/// <summary>The text colour, or null for the output's default.</summary>
	public RgbColor? Foreground { get; init; }

	/// <summary>The background colour, or null for the output's default.</summary>
	public RgbColor? Background { get; init; }

	/// <summary>The attributes. <see cref="TextAttributes.Inverse"/> stays set for information; the colours are already swapped.</summary>
	public TextAttributes Attributes { get; init; }

	/// <summary>What clicking it does, or null.</summary>
	public Interaction? Interaction { get; init; }

	/// <summary>The picture these cells are part of, or null.</summary>
	public ImageMarkup? Picture { get; init; }

	/// <summary>The pane it belongs in, or null for the main output.</summary>
	public string? Pane { get; init; }

	/// <summary>No styling at all.</summary>
	public static DisplayStyle Plain => default;

	/// <summary>Whether <paramref name="attribute"/> is set.</summary>
	public bool Has(TextAttributes attribute) => (Attributes & attribute) == attribute;
}

/// <summary>
/// Collects a style as contributors fold a run's markup into it. Contributors run in the order they were added to
/// the <see cref="DisplayRegistry"/>; each reads the layers it knows and leaves the rest.
/// </summary>
public sealed class StyleBuilder
{
	/// <summary>The text colour so far, or null.</summary>
	public RgbColor? Foreground { get; set; }

	/// <summary>The background colour so far, or null.</summary>
	public RgbColor? Background { get; set; }

	/// <summary>The attributes so far.</summary>
	public TextAttributes Attributes { get; set; }

	/// <summary>The interaction so far, or null.</summary>
	public Interaction? Interaction { get; set; }

	/// <summary>The picture so far, or null.</summary>
	public ImageMarkup? Picture { get; set; }

	/// <summary>The pane so far, or null.</summary>
	public string? Pane { get; set; }

	internal void Clear()
	{
		Foreground = null;
		Background = null;
		Attributes = TextAttributes.None;
		Interaction = null;
		Picture = null;
		Pane = null;
	}

	internal DisplayStyle Build() => new()
	{
		Foreground = Foreground,
		Background = Background,
		Attributes = Attributes,
		Interaction = Interaction,
		Picture = Picture,
		Pane = Pane,
	};
}

/// <summary>Folds the layers it knows of a run's markup into a <see cref="StyleBuilder"/>.</summary>
public interface IStyleContributor
{
	/// <summary>Reads <paramref name="markups"/> (innermost first) and adds what it knows to <paramref name="style"/>.</summary>
	void Contribute(MarkupSet markups, StyleBuilder style, DisplayOptions options);
}

/// <summary>Turns a run's markup into the style an output draws it with: a <see cref="DisplayRegistry"/>, or a host's own.</summary>
public interface IStyleResolver
{
	/// <summary>Folds <paramref name="markups"/> into one style, degraded to what <paramref name="options"/> can show.</summary>
	DisplayStyle Resolve(MarkupSet? markups, DisplayOptions options);
}

/// <summary>Turns a palette colour into RGB, so a client's theme decides what "red" is.</summary>
public interface IColorResolver
{
	/// <summary>One of the sixteen standard colours: <paramref name="index"/> 0 to 7, bright or not.</summary>
	RgbColor Standard(int index, bool bright);

	/// <summary>An entry of the 256-colour palette.</summary>
	RgbColor Xterm(int index);

	/// <summary>
	/// The colour text is finally drawn in, given its background: where a client keeps text legible against its
	/// background, it changes the colour here. Return <paramref name="foreground"/> to leave it alone.
	/// </summary>
	RgbColor Legible(RgbColor foreground, RgbColor background);
}

/// <summary>What a structured renderer is drawing for.</summary>
public sealed record DisplayOptions
{
	/// <summary>What the output can show; whatever it cannot is dropped from each style.</summary>
	public OutputCapabilities Capabilities { get; init; } = OutputCapabilities.Browser;

	/// <summary>How palette colours become RGB, or null for the xterm palette.</summary>
	public IColorResolver? Colors { get; init; }

	/// <summary>Which link addresses become clickable; one that may not stays text. <see cref="UrlPolicy.Safe"/> unless set.</summary>
	public IUrlPolicy Links { get; init; } = UrlPolicy.Safe;

	/// <summary>The default text colour, used when inverse text has no colour of its own.</summary>
	public RgbColor DefaultForeground { get; init; } = new(229, 229, 229);

	/// <summary>The default background, used when inverse text has no background of its own.</summary>
	public RgbColor DefaultBackground { get; init; } = new(0, 0, 0);

	/// <summary>How many cells a tab advances to, in the cell renderer.</summary>
	public int TabWidth { get; init; } = 8;
}
