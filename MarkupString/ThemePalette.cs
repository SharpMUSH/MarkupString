using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using MarkupString.Layout;

namespace MarkupString;

/// <summary>
/// One colour of a <see cref="ThemePalette"/>: an exact colour, the standard terminal colour a
/// sixteen-colour client is sent in its place, or both.
/// </summary>
/// <param name="Rgb">The colour, or <see langword="null"/> for a standard colour alone, which the reader's own client decides the look of.</param>
/// <param name="Slot">The standard colour, 0-7 normal and 8-15 bright, or <see langword="null"/> to have the client pick the nearest.</param>
public readonly record struct ThemeColor(RgbColor? Rgb, int? Slot)
{
	/// <summary><paramref name="rgb"/>, with the standard colour of the same kind (<see cref="ColorMath.StandardSlot"/>).</summary>
	public static ThemeColor Of(RgbColor rgb) => new(rgb, ColorMath.StandardSlot(rgb));

	/// <summary>Standard colour <paramref name="slot"/> alone: whatever the reader's client has made it.</summary>
	public static ThemeColor Standard(int slot)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(slot);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 15);
		return new(null, slot);
	}

	/// <summary>The colour to measure with: <see cref="Rgb"/>, or the slot's usual VGA value.</summary>
	public RgbColor Resolved => Rgb ?? ColorMath.StandardColor(Slot ?? 7);
}

/// <summary>Whether a palette is made for a dark background or a light one.</summary>
public enum ThemeMode
{
	/// <summary>Light colours on a dark background, as most MU* clients are.</summary>
	Dark,

	/// <summary>Dark colours on a light background.</summary>
	Light,
}

/// <summary>What a colour in a <see cref="ThemePalette"/> is for.</summary>
public enum ThemeRole
{
	/// <summary>What the others are measured against. Never painted in a terminal.</summary>
	Background,

	/// <summary>Body text, measured against the background.</summary>
	Foreground,

	/// <summary>The main accent: borders, headings, gauge bars.</summary>
	Primary,

	/// <summary>Titles and labels.</summary>
	Secondary,

	/// <summary>Bullets and highlights.</summary>
	Tertiary,

	/// <summary>Quiet lines: tree guides, separators, the rule under headings, a gauge's empty part.</summary>
	Muted,

	/// <summary>Things going well.</summary>
	Success,

	/// <summary>Things to watch.</summary>
	Warning,

	/// <summary>Things gone wrong.</summary>
	Error,

	/// <summary>Things to know.</summary>
	Info,
}

/// <summary>How <see cref="ThemePalette.Generate"/> picks the accents' hues from the seed's.</summary>
public enum ThemeHarmony
{
	/// <summary>One hue throughout, the accents told apart by lightness and chroma.</summary>
	Monochrome,

	/// <summary>The seed's neighbours, 30 degrees either side.</summary>
	Analogous,

	/// <summary>The seed and the hue opposite it.</summary>
	Complementary,

	/// <summary>The seed and the two hues either side of its opposite, 150 and 210 degrees round.</summary>
	Split,

	/// <summary>Three hues evenly round the wheel.</summary>
	Triadic,

	/// <summary>Four hues evenly round the wheel; the fourth is the info colour.</summary>
	Tetradic,
}

/// <summary>A role of a <see cref="ThemePalette"/> whose contrast with the background falls short (<see cref="ThemePalette.Check"/>).</summary>
/// <param name="Role">The role.</param>
/// <param name="Ratio">Its contrast with the background.</param>
/// <param name="Required">What it should be: 4.5 for text, 3 for lines.</param>
public readonly record struct ContrastShortfall(ThemeRole Role, double Ratio, double Required);

/// <summary>
/// The colours a theme is made of, by what they are for (<see cref="ThemeRole"/>), each with the
/// standard colour a sixteen-colour client gets instead. <see cref="ToTheme"/> turns it into the
/// colours of a <see cref="LayoutTheme"/>'s parts.
/// </summary>
/// <remarks>
/// Make one from a preset (<see cref="Preset"/>), from a base16 scheme (<see cref="FromBase16"/>), from
/// one colour (<see cref="Generate"/>), or from JSON (<see cref="TryParse(string, out ThemePalette?, out string?)"/>).
/// A role left unset is not painted.
/// </remarks>
public sealed record ThemePalette
{
	/// <summary>A name to show for it.</summary>
	public string Name { get; init; } = "custom";

	/// <summary>Whether it is made for a dark background or a light one.</summary>
	public ThemeMode Mode { get; init; } = ThemeMode.Dark;

	/// <summary>The colours set, by role.</summary>
	public ImmutableDictionary<ThemeRole, ThemeColor> Colors { get; init; } = ImmutableDictionary<ThemeRole, ThemeColor>.Empty;

	/// <summary>The colour for <paramref name="role"/>, or <see langword="null"/> when it is unset.</summary>
	public ThemeColor? this[ThemeRole role] => Colors.TryGetValue(role, out var color) ? color : null;

	/// <summary>This palette with <paramref name="role"/> set to <paramref name="color"/>, or unset when it is null.</summary>
	public ThemePalette With(ThemeRole role, ThemeColor? color) =>
		this with { Colors = color is { } set ? Colors.SetItem(role, set) : Colors.Remove(role) };

	/// <summary>The background contrast is measured against: the one set, or black or white by <see cref="Mode"/>.</summary>
	public RgbColor BackgroundColor =>
		this[ThemeRole.Background]?.Resolved ?? (Mode == ThemeMode.Dark ? new RgbColor(0, 0, 0) : new RgbColor(255, 255, 255));

	/// <summary>The contrast a role should have with the background: 3 for lines, 4.5 for text.</summary>
	public static double Required(ThemeRole role) => role switch
	{
		ThemeRole.Background => 1,
		ThemeRole.Primary or ThemeRole.Muted => 3,
		_ => 4.5,
	};

	/// <summary>
	/// The roles set whose contrast with the background is under <see cref="Required"/>. A standard colour
	/// alone, or a standard background, looks however the reader's client makes it, so it is not measured.
	/// </summary>
	public IReadOnlyList<ContrastShortfall> Check()
	{
		var background = BackgroundColor;
		var shortfalls = new List<ContrastShortfall>();
		if (this[ThemeRole.Background] is { Rgb: null }) return shortfalls;
		foreach (var role in Enum.GetValues<ThemeRole>())
		{
			if (role == ThemeRole.Background || this[role] is not { Rgb: not null } color) continue;
			var ratio = ColorMath.Contrast(color.Resolved, background);
			if (ratio < Required(role)) shortfalls.Add(new ContrastShortfall(role, ratio, Required(role)));
		}
		return shortfalls;
	}

	/// <summary>
	/// The palette as the colours of a layout's parts: borders in the primary colour, titles in the
	/// secondary one and bold, labels secondary, bullets tertiary, headings primary and bold, gauge bars
	/// primary, and guides, separators, the rule under headings and a gauge's empty part muted.
	/// </summary>
	/// <param name="paint">Makes the markup a format draws a colour with, bold or not: an ANSI colour, say.</param>
	public LayoutTheme ToTheme(Func<ThemeColor, bool, IMarkup> paint)
	{
		ArgumentNullException.ThrowIfNull(paint);
		IMarkup? Part(ThemeRole role, bool bold = false) => this[role] is { } color ? paint(color, bold) : null;
		return new LayoutTheme
		{
			BorderColor = Part(ThemeRole.Primary),
			TitleColor = Part(ThemeRole.Secondary, bold: true),
			HeadingColor = Part(ThemeRole.Primary, bold: true),
			LabelColor = Part(ThemeRole.Secondary),
			BulletColor = Part(ThemeRole.Tertiary),
			GaugeFilledColor = Part(ThemeRole.Primary),
			GuideColor = Part(ThemeRole.Muted),
			SeparatorColor = Part(ThemeRole.Muted),
			HeaderRuleColor = Part(ThemeRole.Muted),
			GaugeEmptyColor = Part(ThemeRole.Muted),
		};
	}

	/// <summary>
	/// Standard colours alone, so each reader sees the game in the colours their own client is set to:
	/// cyan borders, bright white titles, yellow bullets, dark grey guides.
	/// </summary>
	public static ThemePalette Terminal { get; } = new()
	{
		Name = "terminal",
		Colors = ImmutableDictionary.CreateRange(new Dictionary<ThemeRole, ThemeColor>
		{
			[ThemeRole.Background] = ThemeColor.Standard(0),
			[ThemeRole.Foreground] = ThemeColor.Standard(7),
			[ThemeRole.Primary] = ThemeColor.Standard(6),
			[ThemeRole.Secondary] = ThemeColor.Standard(15),
			[ThemeRole.Tertiary] = ThemeColor.Standard(3),
			[ThemeRole.Muted] = ThemeColor.Standard(8),
			[ThemeRole.Success] = ThemeColor.Standard(2),
			[ThemeRole.Warning] = ThemeColor.Standard(3),
			[ThemeRole.Error] = ThemeColor.Standard(1),
			[ThemeRole.Info] = ThemeColor.Standard(12),
		}),
	};

	/// <summary>Every preset: <see cref="Terminal"/> and well-known schemes.</summary>
	public static IReadOnlyList<ThemePalette> Presets { get; } =
	[
		Terminal,
		Base16("catppuccin-mocha", "1e1e2e 181825 313244 45475a 585b70 cdd6f4 f5e0dc b4befe f38ba8 fab387 f9e2af a6e3a1 94e2d5 89b4fa cba6f7 f2cdcd"),
		Base16("catppuccin-latte", "eff1f5 e6e9ef ccd0da bcc0cc acb0be 4c4f69 dc8a78 7287fd d20f39 fe640b df8e1d 40a02b 179299 1e66f5 8839ef dd7878"),
		Base16("dracula", "282a36 21222c 44475a 6272a4 bfbfbf f8f8f2 f8f8f2 ffffff ff5555 ffb86c f1fa8c 50fa7b 8be9fd bd93f9 ff79c6 ff79c6"),
		Base16("gruvbox-dark", "282828 3c3836 504945 665c54 bdae93 d5c4a1 ebdbb2 fbf1c7 fb4934 fe8019 fabd2f b8bb26 8ec07c 83a598 d3869b d65d0e"),
		Base16("nord", "2e3440 3b4252 434c5e 4c566a d8dee9 e5e9f0 eceff4 8fbcbb bf616a d08770 ebcb8b a3be8c 88c0d0 81a1c1 b48ead 5e81ac"),
		Base16("solarized-dark", "002b36 073642 586e75 657b83 839496 93a1a1 eee8d5 fdf6e3 dc322f cb4b16 b58900 859900 2aa198 268bd2 6c71c4 d33682"),
		Base16("solarized-light", "fdf6e3 eee8d5 93a1a1 839496 657b83 586e75 073642 002b36 dc322f cb4b16 b58900 859900 2aa198 268bd2 6c71c4 d33682"),
		Base16("tokyo-night", "1a1b26 16161e 2f3549 565f89 a9b1d6 c0caf5 cbccd1 d5d6db f7768e ff9e64 e0af68 9ece6a 7dcfff 7aa2f7 bb9af7 db4b4b"),
	];

	/// <summary>The preset named <paramref name="name"/>, ignoring case, or null.</summary>
	public static ThemePalette? Preset(string name)
	{
		foreach (var preset in Presets)
			if (string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase)) return preset;
		return null;
	}

	private static ThemePalette Base16(string name, string colors) =>
		FromBase16(name, [.. colors.Split(' ').Select(hex => ColorMath.TryParseHex(hex, out var rgb) ? rgb : default)]);

	/// <summary>
	/// A base16 scheme (<c>base00</c> to <c>base0F</c>) as a palette, by base16's own guide: background
	/// <c>base00</c>, foreground <c>base05</c>, muted <c>base03</c>, primary <c>base0D</c> (blue),
	/// secondary <c>base0E</c> (magenta), tertiary <c>base0C</c> (cyan), success <c>base0B</c>, warning
	/// <c>base0A</c>, error <c>base08</c>, info <c>base0C</c>. Each keeps the standard colour base16
	/// gives its place in a terminal; dark or light by the background.
	/// </summary>
	/// <exception cref="ArgumentException">There are not sixteen colours.</exception>
	public static ThemePalette FromBase16(string name, IReadOnlyList<RgbColor> colors)
	{
		ArgumentNullException.ThrowIfNull(colors);
		if (colors.Count != 16) throw new ArgumentException("A base16 scheme has sixteen colours.", nameof(colors));
		ThemeColor At(int index, int slot) => new(colors[index], slot);
		return new ThemePalette
		{
			Name = name,
			Mode = ColorMath.Luminance(colors[0]) < 0.18 ? ThemeMode.Dark : ThemeMode.Light,
			Colors = ImmutableDictionary.CreateRange(new Dictionary<ThemeRole, ThemeColor>
			{
				[ThemeRole.Background] = At(0x0, 0),
				[ThemeRole.Foreground] = At(0x5, 7),
				[ThemeRole.Muted] = At(0x3, 8),
				[ThemeRole.Primary] = At(0xD, 4),
				[ThemeRole.Secondary] = At(0xE, 5),
				[ThemeRole.Tertiary] = At(0xC, 6),
				[ThemeRole.Success] = At(0xB, 2),
				[ThemeRole.Warning] = At(0xA, 3),
				[ThemeRole.Error] = At(0x8, 1),
				[ThemeRole.Info] = At(0xC, 6),
			}),
		};
	}

	/// <summary>
	/// A palette made from one colour. The accents take their hues from <paramref name="seed"/>'s as
	/// <paramref name="harmony"/> says and its chroma, and each is made lighter or darker until it stands
	/// out from the background enough: 3:1 for lines, 4.5:1 for text, both raised toward 7:1 by
	/// <paramref name="contrast"/>. Success, warning, error and info keep green, amber, red and blue,
	/// turned a little toward the seed. The background and foreground are near black and near white
	/// tinted with the seed.
	/// </summary>
	/// <param name="seed">The colour it is made from.</param>
	/// <param name="harmony">How the accents' hues are picked.</param>
	/// <param name="mode">Whether it is for a dark background or a light one.</param>
	/// <param name="contrast">From 0 to 1: how much more than the minimum the colours stand out.</param>
	public static ThemePalette Generate(RgbColor seed, ThemeHarmony harmony = ThemeHarmony.Analogous, ThemeMode mode = ThemeMode.Dark, double contrast = 0)
	{
		var source = ColorMath.ToOklch(seed);
		var hue = double.IsNaN(source.H) ? 250 : source.H;
		var chroma = source.C < 0.02 ? 0 : Math.Clamp(source.C, 0.06, 0.17);
		var dark = mode == ThemeMode.Dark;
		var level = double.IsFinite(contrast) ? Math.Clamp(contrast, 0, 1) : 0;
		var lines = 3 + 4 * level;
		var text = 4.5 + 2.5 * level;

		var background = ColorMath.FromOklch(new OklchColor(dark ? 0.18 : 0.98, Math.Min(chroma, 0.02), hue));
		var foreground = ColorMath.WithContrast(ColorMath.FromOklch(new OklchColor(dark ? 0.9 : 0.25, Math.Min(chroma, 0.015), hue)), background, Math.Max(text, 7));

		ThemeColor Accent(double turn, double lightness, double share, double ratio) =>
			ThemeColor.Of(ColorMath.WithContrast(
				ColorMath.FromOklch(new OklchColor(dark ? lightness : 1 - lightness * 0.65, chroma * share, ColorMath.Wrap(hue + turn))),
				background, ratio));

		// The hues of the secondary, tertiary and info colours, and how much of the seed's chroma each keeps.
		var (second, third, info) = harmony switch
		{
			ThemeHarmony.Monochrome => (0.0, 0.0, (double?)null),
			ThemeHarmony.Complementary => (180.0, 30.0, (double?)null),
			ThemeHarmony.Split => (150.0, 210.0, (double?)null),
			ThemeHarmony.Triadic => (120.0, 240.0, (double?)null),
			ThemeHarmony.Tetradic => (90.0, 180.0, (double?)270),
			_ => (30.0, -30.0, (double?)null),
		};
		var mono = harmony == ThemeHarmony.Monochrome;

		ThemeColor Status(double conventional)
		{
			// Turned toward the seed, but not so far that red stops being red.
			var turned = chroma == 0 ? conventional : conventional + Math.Clamp(ColorMath.Turn(conventional, hue) * 0.8, -20, 20);
			return ThemeColor.Of(ColorMath.WithContrast(
				ColorMath.FromOklch(new OklchColor(dark ? 0.75 : 0.5, 0.14, ColorMath.Wrap(turned))), background, text));
		}

		return new ThemePalette
		{
			Name = "generated",
			Mode = mode,
			Colors = ImmutableDictionary.CreateRange(new Dictionary<ThemeRole, ThemeColor>
			{
				[ThemeRole.Background] = new(background, dark ? 0 : 15),
				[ThemeRole.Foreground] = new(foreground, dark ? 7 : 0),
				[ThemeRole.Primary] = Accent(0, 0.72, 1, lines),
				[ThemeRole.Secondary] = Accent(second, mono ? 0.86 : 0.8, mono ? 0.5 : 1, text),
				[ThemeRole.Tertiary] = Accent(third, mono ? 0.64 : 0.76, mono ? 0.8 : 1, text),
				[ThemeRole.Muted] = Accent(0, 0.5, chroma == 0 ? 0 : 0.15, lines),
				[ThemeRole.Success] = Status(145),
				[ThemeRole.Warning] = Status(80),
				[ThemeRole.Error] = Status(27),
				[ThemeRole.Info] = info is { } turn ? Accent(turn, 0.76, 1, text) : Status(245),
			}),
		};
	}

	/// <summary>The role named <paramref name="name"/>, ignoring case.</summary>
	public static bool TryParseRole(string name, out ThemeRole role) =>
		Enum.TryParse(name, ignoreCase: true, out role) && Enum.IsDefined(role) && !int.TryParse(name, out _);

	/// <summary>
	/// Reads a palette from JSON: a preset's name (<c>"nord"</c>), or an object with any of
	/// <c>preset</c> (start from that one), <c>base16</c> (sixteen colours), <c>seed</c> with
	/// <c>harmony</c> and <c>contrast</c> (generate one), <c>mode</c>, <c>name</c>, and <c>colors</c>
	/// setting roles: <c>"#88c0d0"</c>, a standard colour <c>6</c>, <c>{"rgb":"#88c0d0","slot":6}</c>, or
	/// <c>null</c> to unset one.
	/// </summary>
	public static bool TryParse(string json, out ThemePalette? palette, out string? error)
	{
		palette = null;
		try
		{
			using var document = JsonDocument.Parse(json);
			return TryRead(document.RootElement, out palette, out error);
		}
		catch (JsonException)
		{
			// A bare word is a preset's name, as it would be in quotes.
			if (Preset(json.Trim()) is { } preset)
			{
				palette = preset;
				error = null;
				return true;
			}
			error = "not JSON or a theme name";
			return false;
		}
	}

	/// <summary>Reads a palette from <paramref name="element"/>, as <see cref="TryParse(string, out ThemePalette?, out string?)"/> describes.</summary>
	public static bool TryRead(JsonElement element, out ThemePalette? palette, out string? error)
	{
		palette = null;
		error = null;
		if (element.ValueKind == JsonValueKind.String)
		{
			palette = Preset(element.GetString() ?? string.Empty);
			error = palette is null ? $"no theme named '{element.GetString()}'" : null;
			return palette is not null;
		}
		if (element.ValueKind != JsonValueKind.Object)
		{
			error = "a theme is a name or an object";
			return false;
		}

		var result = new ThemePalette();
		var mode = (ThemeMode?)null;
		if (element.TryGetProperty("mode", out var modeValue))
		{
			if (modeValue.ValueKind != JsonValueKind.String || !Enum.TryParse<ThemeMode>(modeValue.GetString(), true, out var parsed) || !Enum.IsDefined(parsed))
			{
				error = "mode is dark or light";
				return false;
			}
			mode = parsed;
		}

		var sources = new[] { "preset", "base16", "seed" }.Count(name => element.TryGetProperty(name, out _));
		if (sources > 1)
		{
			error = "use one of preset, base16 and seed";
			return false;
		}
		if (element.TryGetProperty("preset", out var presetValue))
		{
			if (!TryRead(presetValue, out var preset, out error)) return false;
			result = preset!;
		}
		else if (element.TryGetProperty("base16", out var base16))
		{
			if (base16.ValueKind != JsonValueKind.Array || base16.GetArrayLength() != 16)
			{
				error = "base16 is an array of sixteen colours";
				return false;
			}
			var colors = new List<RgbColor>(16);
			foreach (var item in base16.EnumerateArray())
			{
				if (item.ValueKind != JsonValueKind.String || !ColorMath.TryParseHex(item.GetString(), out var rgb))
				{
					error = $"'{item}' is not a colour like #88c0d0";
					return false;
				}
				colors.Add(rgb);
			}
			result = FromBase16("custom", colors);
		}
		else if (element.TryGetProperty("seed", out var seedValue))
		{
			if (seedValue.ValueKind != JsonValueKind.String || !ColorMath.TryParseHex(seedValue.GetString(), out var seed))
			{
				error = "seed is a colour like #7aa2f7";
				return false;
			}
			var harmony = ThemeHarmony.Analogous;
			if (element.TryGetProperty("harmony", out var harmonyValue)
				&& (harmonyValue.ValueKind != JsonValueKind.String || !Enum.TryParse(harmonyValue.GetString(), true, out harmony) || !Enum.IsDefined(harmony)))
			{
				error = "harmony is one of " + string.Join(", ", Enum.GetNames<ThemeHarmony>().Select(n => n.ToLowerInvariant()));
				return false;
			}
			var level = 0.0;
			if (element.TryGetProperty("contrast", out var contrastValue)
				&& (contrastValue.ValueKind != JsonValueKind.Number || !contrastValue.TryGetDouble(out level) || level is < 0 or > 1))
			{
				error = "contrast is a number from 0 to 1";
				return false;
			}
			result = Generate(seed, harmony, mode ?? ThemeMode.Dark, level);
		}

		if (mode is { } chosen) result = result with { Mode = chosen };
		if (element.TryGetProperty("name", out var nameValue))
		{
			if (nameValue.ValueKind != JsonValueKind.String)
			{
				error = "name is text";
				return false;
			}
			result = result with { Name = nameValue.GetString()! };
		}

		if (element.TryGetProperty("colors", out var colorsValue))
		{
			if (colorsValue.ValueKind != JsonValueKind.Object)
			{
				error = "colors is an object of role: colour";
				return false;
			}
			foreach (var property in colorsValue.EnumerateObject())
			{
				if (!TryParseRole(property.Name, out var role))
				{
					error = $"'{property.Name}' is not a role; the roles are {string.Join(", ", Enum.GetNames<ThemeRole>().Select(n => n.ToLowerInvariant()))}";
					return false;
				}
				if (property.Value.ValueKind == JsonValueKind.Null)
				{
					result = result.With(role, null);
					continue;
				}
				if (!TryReadColor(property.Value, out var color, out error)) return false;
				result = result.With(role, color);
			}
		}

		foreach (var property in element.EnumerateObject())
		{
			if (property.Name is "preset" or "base16" or "seed" or "harmony" or "contrast" or "mode" or "name" or "colors") continue;
			error = $"a theme has no '{property.Name}'";
			return false;
		}

		palette = result;
		return true;
	}

	private static bool TryReadColor(JsonElement value, out ThemeColor color, out string? error)
	{
		color = default;
		error = null;
		switch (value.ValueKind)
		{
			case JsonValueKind.String when ColorMath.TryParseHex(value.GetString(), out var rgb):
				color = ThemeColor.Of(rgb);
				return true;
			case JsonValueKind.Number when value.TryGetInt32(out var slot) && slot is >= 0 and <= 15:
				color = ThemeColor.Standard(slot);
				return true;
			case JsonValueKind.Object:
				RgbColor? exact = null;
				int? standard = null;
				foreach (var part in value.EnumerateObject())
				{
					if (part.Name == "rgb" && part.Value.ValueKind == JsonValueKind.String && ColorMath.TryParseHex(part.Value.GetString(), out var hex)) exact = hex;
					else if (part.Name == "slot" && part.Value.ValueKind == JsonValueKind.Number && part.Value.TryGetInt32(out var index) && index is >= 0 and <= 15) standard = index;
					else
					{
						error = $"'{part.Name}' in a colour: a colour has rgb (like #88c0d0) and slot (0 to 15)";
						return false;
					}
				}
				if (exact is null && standard is null)
				{
					error = "a colour needs rgb or slot";
					return false;
				}
				color = new ThemeColor(exact, standard ?? (exact is { } known ? ColorMath.StandardSlot(known) : null));
				return true;
			default:
				error = $"'{value}' is not a colour: use #rrggbb, a standard colour 0 to 15, or {{\"rgb\":..,\"slot\":..}}";
				return false;
		}
	}

	/// <summary>
	/// The palette as JSON that <see cref="TryParse(string, out ThemePalette?, out string?)"/> reads back:
	/// its name, mode, and each role set as <c>{"rgb":"#88c0d0","slot":6}</c>.
	/// </summary>
	public string ToJson()
	{
		using var stream = new MemoryStream();
		using (var json = new Utf8JsonWriter(stream))
		{
			json.WriteStartObject();
			json.WriteString("name", Name);
			json.WriteString("mode", Mode == ThemeMode.Dark ? "dark" : "light");
			json.WriteStartObject("colors");
			foreach (var role in Enum.GetValues<ThemeRole>())
			{
				if (this[role] is not { } color) continue;
				json.WriteStartObject(RoleName(role));
				if (color.Rgb is { } rgb) json.WriteString("rgb", rgb.ToHex());
				if (color.Slot is { } slot) json.WriteNumber("slot", slot);
				json.WriteEndObject();
			}
			json.WriteEndObject();
			json.WriteEndObject();
		}
		return System.Text.Encoding.UTF8.GetString(stream.ToArray());
	}

	/// <inheritdoc/>
	public bool Equals(ThemePalette? other) =>
		other is not null && Name == other.Name && Mode == other.Mode && Colors.Count == other.Colors.Count
		&& Colors.All(pair => other.Colors.TryGetValue(pair.Key, out var color) && color == pair.Value);

	/// <inheritdoc/>
	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(Name);
		hash.Add(Mode);
		foreach (var pair in Colors.OrderBy(pair => pair.Key))
		{
			hash.Add(pair.Key);
			hash.Add(pair.Value);
		}
		return hash.ToHashCode();
	}

	/// <summary>A role's name as JSON and help write it.</summary>
	public static string RoleName(ThemeRole role) => role.ToString().ToLower(CultureInfo.InvariantCulture);
}
