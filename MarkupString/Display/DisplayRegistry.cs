namespace MarkupString.Display;

/// <summary>
/// The style contributors a structured renderer folds markup with. Immutable; build one once with
/// <c>With(...)</c> and share it.
/// </summary>
public sealed class DisplayRegistry
{
	private readonly IStyleContributor[] _contributors;

	private DisplayRegistry(IStyleContributor[] contributors) => _contributors = contributors;

	/// <summary>A registry that knows no markup: everything is plain.</summary>
	public static DisplayRegistry Empty { get; } = new([]);

	/// <summary>A registry that knows the shared vocabulary: pictures, panes, menus and highlights.</summary>
	public static DisplayRegistry Core { get; } = Empty.With(VocabularyStyles.Instance);

	/// <summary>The contributors, in the order they run.</summary>
	public IReadOnlyList<IStyleContributor> Contributors => _contributors;

	/// <summary>A registry with <paramref name="contributor"/> added after the others.</summary>
	public DisplayRegistry With(IStyleContributor contributor)
	{
		ArgumentNullException.ThrowIfNull(contributor);
		return new DisplayRegistry([.. _contributors, contributor]);
	}

	/// <summary>Folds <paramref name="markups"/> into one style, degraded to what <paramref name="options"/> can show.</summary>
	public DisplayStyle Resolve(MarkupSet? markups, DisplayOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		if (markups is null || markups.Count == 0) return DisplayStyle.Plain;

		var builder = new StyleBuilder();
		foreach (var contributor in _contributors) contributor.Contribute(markups, builder, options);
		return Degrade(builder, options);
	}

	private static DisplayStyle Degrade(StyleBuilder style, DisplayOptions options)
	{
		var capabilities = options.Capabilities;

		if (capabilities.Colors <= ColorLevel.Attributes)
		{
			style.Foreground = null;
			style.Background = null;
		}

		style.Attributes &= capabilities.Colors == ColorLevel.None ? TextAttributes.None : capabilities.Attributes | TextAttributes.Inverse;

		if (style.Has(TextAttributes.Inverse))
		{
			if (capabilities.Colors <= ColorLevel.Attributes && (capabilities.Attributes & TextAttributes.Inverse) == 0)
			{
				style.Attributes &= ~TextAttributes.Inverse;
			}
			else if (capabilities.Colors > ColorLevel.Attributes)
			{
				(style.Foreground, style.Background) = (style.Background ?? options.DefaultBackground, style.Foreground ?? options.DefaultForeground);
			}
		}

		if (style.Foreground is { } foreground && options.Colors is { } resolver)
		{
			style.Foreground = resolver.Legible(foreground, style.Background ?? options.DefaultBackground);
		}

		if (style.Interaction is { } interaction) style.Interaction = Degrade(interaction, capabilities.Interactions);
		if (capabilities.Pictures == PictureMethods.None) style.Picture = null;
		if (!capabilities.Panes) style.Pane = null;

		return style.Build();
	}

	private static Interaction? Degrade(Interaction interaction, InteractionKinds allowed)
	{
		if (interaction.Choices is { Count: > 0 } && (allowed & InteractionKinds.Menu) == 0)
		{
			interaction = interaction with { Choices = null };
		}

		var kind = interaction.Kind switch
		{
			InteractionKind.Url => InteractionKinds.Url,
			InteractionKind.Command => InteractionKinds.Command,
			_ => InteractionKinds.Prompt,
		};

		if ((allowed & kind) != 0) return interaction;

		// A prompt link on an output that only sends commands is still useful as a command.
		if (interaction.Kind == InteractionKind.Prompt && (allowed & InteractionKinds.Command) != 0)
		{
			return interaction with { Kind = InteractionKind.Command };
		}

		return null;
	}
}

file static class StyleBuilderExtensions
{
	public static bool Has(this StyleBuilder style, TextAttributes attribute) => (style.Attributes & attribute) == attribute;
}

/// <summary>Styles for the shared vocabulary: pictures, panes, command menus and highlights.</summary>
public sealed class VocabularyStyles : IStyleContributor
{
	/// <summary>The one instance.</summary>
	public static VocabularyStyles Instance { get; } = new();

	private VocabularyStyles()
	{
	}

	/// <inheritdoc/>
	public void Contribute(MarkupSet markups, StyleBuilder style, DisplayOptions options)
	{
		ArgumentNullException.ThrowIfNull(markups);
		ArgumentNullException.ThrowIfNull(style);

		// Outermost first, so the innermost layer has the last word.
		for (var i = markups.Count - 1; i >= 0; i--)
		{
			switch (markups[i])
			{
				case ImageMarkup image:
					style.Picture = image;
					break;
				case PaneMarkup pane:
					style.Pane = pane.Name;
					break;
				case CommandMenuMarkup menu when menu.Choices.Count > 0:
					style.Interaction = new Interaction(menu.Prompt ? InteractionKind.Prompt : InteractionKind.Command, menu.Choices[0].Command)
					{
						Hint = menu.Hint,
						Choices = menu.Choices,
					};
					break;
				case HighlightMarkup highlight:
					style.Background = highlight.Color;
					break;
			}
		}
	}
}
