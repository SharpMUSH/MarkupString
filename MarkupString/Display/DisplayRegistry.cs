namespace MarkupString.Display;

/// <summary>
/// The style contributors a structured renderer folds markup with. Immutable; build one once with
/// <c>With(...)</c> and share it.
/// </summary>
public sealed class DisplayRegistry : IStyleResolver
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
		if (builder.Interaction is { Group: null } interaction)
		{
			// The innermost group names the link, whichever contributor made it.
			for (var i = 0; i < markups.Count; i++)
			{
				if (markups[i] is not LinkGroupMarkup group) continue;
				builder.Interaction = interaction with { Group = group.Group };
				break;
			}
		}

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

		var level = capabilities.Colors;
		style.Background = Reduce(style.Background, level, options.Colors);
		style.Foreground = Reduce(style.Foreground, level, options.Colors);
		if (style.Foreground is { } foreground && options.Colors is { } resolver)
		{
			// The client's change for legibility is reduced too, as the output can only show its palette.
			style.Foreground = Reduce(resolver.Legible(foreground, style.Background ?? options.DefaultBackground), level, resolver);
		}

		if (style.Interaction is { } interaction) style.Interaction = Degrade(interaction, capabilities.Interactions);
		if (capabilities.Pictures == PictureMethods.None) style.Picture = null;
		if (!capabilities.Panes) style.Pane = null;

		return style.Build();
	}

	/// <summary>
	/// <paramref name="color"/> as an output of <paramref name="level"/> shows it: the nearest of its sixteen or 256
	/// colours, by redmean distance, as <paramref name="resolver"/> draws them (or the xterm palette).
	/// </summary>
	private static RgbColor? Reduce(RgbColor? color, ColorLevel level, IColorResolver? resolver)
	{
		if (color is not { } rgb || level is not (ColorLevel.Standard or ColorLevel.Xterm256)) return color;
		var count = level == ColorLevel.Standard ? 16 : 256;
		var best = rgb;
		var nearest = double.MaxValue;
		for (var i = 0; i < count; i++)
		{
			var entry = Palette(i, resolver);
			var distance = Redmean(rgb, entry);
			if (distance >= nearest) continue;
			(nearest, best) = (distance, entry);
			if (distance == 0) break;
		}

		return best;
	}

	private static RgbColor Palette(int index, IColorResolver? resolver)
	{
		if (index < 16) return resolver?.Standard(index & 7, index >= 8) ?? ColorMath.StandardColor(index);
		return resolver?.Xterm(index) ?? XtermColor(index);
	}

	private static ReadOnlySpan<byte> CubeLevels => [0, 95, 135, 175, 215, 255];

	private static RgbColor XtermColor(int index)
	{
		if (index >= 232)
		{
			var grey = (byte)(8 + 10 * (index - 232));
			return new RgbColor(grey, grey, grey);
		}

		var offset = index - 16;
		return new RgbColor(CubeLevels[offset / 36], CubeLevels[offset / 6 % 6], CubeLevels[offset % 6]);
	}

	/// <summary>The "redmean" distance, squared: a cheap measure of how different two colours look.</summary>
	private static double Redmean(RgbColor a, RgbColor b)
	{
		var mean = (a.R + b.R) / 2.0;
		double r = a.R - b.R, g = a.G - b.G, bl = a.B - b.B;
		return (2 + mean / 256) * r * r + 4 * g * g + (2 + (255 - mean) / 256) * bl * bl;
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
