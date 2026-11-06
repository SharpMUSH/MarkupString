using MarkupString.Display;
namespace MarkupString.Ansi.Decoding;

/// <summary>Registers terminal styles on a <see cref="DisplayRegistry"/>, and reads a terminal's capabilities.</summary>
public static class AnsiDisplay
{
	/// <summary>The registry with <see cref="AnsiStyles"/> added.</summary>
	/// <param name="registry">The registry.</param>
	/// <param name="boldIsBright">Draw bold text in a standard colour in its bright version, as many MUD clients do.</param>
	public static DisplayRegistry WithAnsi(this DisplayRegistry registry, bool boldIsBright = false)
	{
		ArgumentNullException.ThrowIfNull(registry);
		return registry.With(boldIsBright ? AnsiStyles.BoldIsBright : AnsiStyles.Instance);
	}

	/// <summary>The shared vocabulary and terminal styles.</summary>
	public static DisplayRegistry Default { get; } = DisplayRegistry.Core.WithAnsi();

	/// <summary>What a terminal shows, from its profile and the colour depth the host settled on.</summary>
	public static OutputCapabilities ToOutputCapabilities(this TerminalProfile profile, AnsiColorDepth depth)
	{
		ArgumentNullException.ThrowIfNull(profile);
		var features = profile.Features;
		var pictures = PictureMethods.None;
		if ((features & TerminalFeatures.KittyGraphics) != 0) pictures |= PictureMethods.KittyPlaceholders;
		if ((features & TerminalFeatures.InlineImages) != 0) pictures |= PictureMethods.InlineImages;
		if ((features & TerminalFeatures.Sixel) != 0) pictures |= PictureMethods.Sixel;
		if ((features & TerminalFeatures.BlockArt) != 0) pictures |= PictureMethods.HalfBlocks;

		var interactions = InteractionKinds.None;
		if ((features & TerminalFeatures.Hyperlinks) != 0) interactions |= InteractionKinds.Url;
		if ((features & TerminalFeatures.CommandLinks) != 0) interactions |= InteractionKinds.Command;

		return new OutputCapabilities
		{
			Colors = depth switch
			{
				AnsiColorDepth.TrueColor => ColorLevel.TrueColor,
				AnsiColorDepth.Xterm256 => ColorLevel.Xterm256,
				AnsiColorDepth.Standard => ColorLevel.Standard,
				AnsiColorDepth.Attributes => ColorLevel.Attributes,
				_ => ColorLevel.None,
			},
			Attributes = TextAttributes.All,
			Interactions = interactions,
			Pictures = pictures,
			MovingPictures = (features & TerminalFeatures.MovingPictures) != 0,
		};
	}

	/// <summary>The colour depth an output shows, for an ANSI writer.</summary>
	public static AnsiColorDepth ToColorDepth(this OutputCapabilities capabilities)
	{
		ArgumentNullException.ThrowIfNull(capabilities);
		return capabilities.Colors switch
		{
			ColorLevel.TrueColor => AnsiColorDepth.TrueColor,
			ColorLevel.Xterm256 => AnsiColorDepth.Xterm256,
			ColorLevel.Standard => AnsiColorDepth.Standard,
			ColorLevel.Attributes => AnsiColorDepth.Attributes,
			_ => AnsiColorDepth.None,
		};
	}

	/// <summary>The terminal features an output has, for an ANSI writer.</summary>
	public static TerminalFeatures ToTerminalFeatures(this OutputCapabilities capabilities)
	{
		ArgumentNullException.ThrowIfNull(capabilities);
		var features = TerminalFeatures.None;
		if ((capabilities.Interactions & InteractionKinds.Url) != 0) features |= TerminalFeatures.Hyperlinks;
		if ((capabilities.Interactions & InteractionKinds.Command) != 0) features |= TerminalFeatures.CommandLinks;
		if ((capabilities.Pictures & PictureMethods.KittyPlaceholders) != 0) features |= TerminalFeatures.KittyGraphics;
		if ((capabilities.Pictures & PictureMethods.InlineImages) != 0) features |= TerminalFeatures.InlineImages;
		if ((capabilities.Pictures & PictureMethods.Sixel) != 0) features |= TerminalFeatures.Sixel;
		if ((capabilities.Pictures & PictureMethods.HalfBlocks) != 0) features |= TerminalFeatures.BlockArt;
		if (capabilities.MovingPictures) features |= TerminalFeatures.MovingPictures;
		return features;
	}
}

/// <summary>
/// Folds every layer with a terminal style (<see cref="IAnsiStyleSource"/>: SGR, an MXP colour, an HTML
/// <c>&lt;b&gt;</c>) into colours, attributes and a link, outermost first as <see cref="AnsiStyle.Combine"/> nests them.
/// </summary>
public sealed class AnsiStyles : IStyleContributor
{
	private readonly bool _boldIsBright;

	private AnsiStyles(bool boldIsBright) => _boldIsBright = boldIsBright;

	/// <summary>Colours as they are written.</summary>
	public static AnsiStyles Instance { get; } = new(false);

	/// <summary>Bold text in a standard colour drawn in its bright version.</summary>
	public static AnsiStyles BoldIsBright { get; } = new(true);

	/// <inheritdoc/>
	public void Contribute(MarkupSet markups, StyleBuilder style, DisplayOptions options)
	{
		ArgumentNullException.ThrowIfNull(markups);
		ArgumentNullException.ThrowIfNull(style);
		ArgumentNullException.ThrowIfNull(options);

		var folded = AnsiStyle.None;
		var any = false;
		for (var i = markups.Count - 1; i >= 0; i--)
		{
			if (markups[i] is IAnsiStyleSource source && source.TryGetAnsiStyle(MarkupFormat.Ansi, out var layer))
			{
				folded = folded.Combine(layer);
				any = true;
			}
		}

		if (!any) return;

		var colors = options.Colors ?? AnsiColors.Default;
		var foreground = folded.Foreground;
		if (_boldIsBright && folded.Bold && foreground is AnsiColor.Standard { Bright: false } standard)
		{
			foreground = AnsiColor.StandardOf(standard.Index, true);
		}

		if (Resolve(foreground, colors) is { } fg) style.Foreground = fg;
		if (Resolve(folded.Background, colors) is { } bg) style.Background ??= bg;

		var attributes = TextAttributes.None;
		if (folded.Bold) attributes |= TextAttributes.Bold;
		if (folded.Faint) attributes |= TextAttributes.Faint;
		if (folded.Italic) attributes |= TextAttributes.Italic;
		if (folded.Underlined) attributes |= TextAttributes.Underline;
		if (folded.Blink) attributes |= TextAttributes.Blink;
		if (folded.Inverted) attributes |= TextAttributes.Inverse;
		if (folded.Concealed) attributes |= TextAttributes.Conceal;
		if (folded.StrikeThrough) attributes |= TextAttributes.Strikethrough;
		if (folded.Overlined) attributes |= TextAttributes.Overline;
		style.Attributes |= attributes;

		if (!string.IsNullOrEmpty(folded.LinkUrl) && style.Interaction is null)
		{
			if (folded.LinkKind == LinkKind.Command)
			{
				style.Interaction = new Interaction(InteractionKind.Command, folded.LinkUrl) { Hint = folded.LinkText };
			}
			else if (UrlSafety.IsSafeNavigableUrl(folded.LinkUrl))
			{
				style.Interaction = new Interaction(InteractionKind.Url, folded.LinkUrl) { Hint = folded.LinkText };
			}
		}
	}

	private static RgbColor? Resolve(AnsiColor? color, IColorResolver colors) => color switch
	{
		AnsiColor.Standard standard => colors.Standard(standard.Index, standard.Bright),
		AnsiColor.Xterm { Index: < 16 } xterm => colors.Standard(xterm.Index & 7, xterm.Index >= 8),
		AnsiColor.Xterm xterm => colors.Xterm(xterm.Index),
		AnsiColor.Rgb rgb => new RgbColor(rgb.R, rgb.G, rgb.B),
		_ => null,
	};
}

/// <summary>The xterm palette, as <see cref="AnsiPalette"/> has it, as an <see cref="IColorResolver"/>.</summary>
public class AnsiColors : IColorResolver
{
	/// <summary>The xterm palette, text left as it is.</summary>
	public static AnsiColors Default { get; } = new();

	/// <inheritdoc/>
	public virtual RgbColor Standard(int index, bool bright)
	{
		var rgb = AnsiPalette.Standard((byte)(index & 7), bright);
		return new RgbColor(rgb.R, rgb.G, rgb.B);
	}

	/// <inheritdoc/>
	public virtual RgbColor Xterm(int index)
	{
		var rgb = AnsiPalette.Xterm((byte)index);
		return new RgbColor(rgb.R, rgb.G, rgb.B);
	}

	/// <inheritdoc/>
	public virtual RgbColor Legible(RgbColor foreground, RgbColor background) => foreground;
}
