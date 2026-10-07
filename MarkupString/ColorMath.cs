using System.Globalization;

namespace MarkupString;

/// <summary>A colour as Oklab's lightness, chroma and hue: how light it looks, how vivid, and which way round the wheel.</summary>
/// <param name="L">Lightness, 0 (black) to 1 (white).</param>
/// <param name="C">Chroma, 0 (grey) up to about 0.37 for the most vivid sRGB colours.</param>
/// <param name="H">Hue in degrees, 0 to 360; <see cref="double.NaN"/> for a grey, which has none.</param>
public readonly record struct OklchColor(double L, double C, double H);

/// <summary>
/// What a theme is made with: Oklab's lightness, chroma and hue, WCAG contrast, and the standard
/// terminal colour nearest in kind.
/// </summary>
public static class ColorMath
{
	/// <summary><paramref name="color"/> in OKLCH.</summary>
	public static OklchColor ToOklch(RgbColor color)
	{
		var (l, c, h) = ColorGradient.Polar(ColorGradient.ToOklab(color));
		return new OklchColor(l, c, h);
	}

	/// <summary>
	/// <paramref name="color"/> in sRGB. One outside sRGB loses chroma, keeping its lightness and hue, until
	/// it fits. A hue of <see cref="double.NaN"/> is grey.
	/// </summary>
	public static RgbColor FromOklch(OklchColor color)
	{
		if (double.IsNaN(color.H) || color.C <= 0) return ColorGradient.FromOklab(color.L, 0, 0);
		var hue = color.H * Math.PI / 180;
		return ColorGradient.FromOklab(color.L, color.C * Math.Cos(hue), color.C * Math.Sin(hue));
	}

	/// <summary>WCAG 2's relative luminance: 0 for black, 1 for white.</summary>
	public static double Luminance(RgbColor color) =>
		0.2126 * ColorGradient.ToLinear(color.R) + 0.7152 * ColorGradient.ToLinear(color.G) + 0.0722 * ColorGradient.ToLinear(color.B);

	/// <summary>
	/// WCAG 2's contrast ratio between two colours, 1 (the same) to 21 (black on white). Text wants 4.5,
	/// lines and other parts that are not text 3 (WCAG 1.4.3 and 1.4.11).
	/// </summary>
	public static double Contrast(RgbColor first, RgbColor second)
	{
		var a = Luminance(first);
		var b = Luminance(second);
		return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
	}

	/// <summary>
	/// <paramref name="color"/> made lighter or darker, keeping its hue, until its contrast with
	/// <paramref name="background"/> reaches <paramref name="ratio"/>: lighter on a dark background, darker
	/// on a light one, and no further than it must. As far as it goes when the ratio cannot be reached.
	/// </summary>
	public static RgbColor WithContrast(RgbColor color, RgbColor background, double ratio)
	{
		if (Contrast(color, background) >= ratio) return color;
		var oklch = ToOklch(color);
		var lighter = Luminance(background) < 0.18;
		var (low, high) = lighter ? (oklch.L, 1.0) : (0.0, oklch.L);
		RgbColor At(double lightness) => FromOklch(oklch with { L = lightness });
		if (Contrast(At(lighter ? high : low), background) < ratio) return At(lighter ? high : low);
		for (var i = 0; i < 30; i++)
		{
			var mid = (low + high) / 2;
			var enough = Contrast(At(mid), background) >= ratio;
			if (enough == lighter) high = mid;
			else low = mid;
		}
		return At(lighter ? high : low);
	}

	/// <summary><paramref name="color"/> turned <paramref name="degrees"/> round the OKLCH hue wheel; a grey is unchanged.</summary>
	public static RgbColor Rotate(RgbColor color, double degrees)
	{
		var oklch = ToOklch(color);
		return double.IsNaN(oklch.H) ? color : FromOklch(oklch with { H = Wrap(oklch.H + degrees) });
	}

	/// <summary>A hue in 0 to 360.</summary>
	internal static double Wrap(double hue)
	{
		hue %= 360;
		return hue < 0 ? hue + 360 : hue;
	}

	/// <summary>The signed shortest turn from hue <paramref name="from"/> to hue <paramref name="to"/>, -180 to 180.</summary>
	internal static double Turn(double from, double to)
	{
		var delta = Wrap(to - from);
		return delta > 180 ? delta - 360 : delta;
	}

	/// <summary>Reads <c>#rgb</c> or <c>#rrggbb</c>, the <c>#</c> optional.</summary>
	public static bool TryParseHex(string? text, out RgbColor color)
	{
		color = default;
		if (string.IsNullOrWhiteSpace(text)) return false;
		var hex = text.Trim().TrimStart('#');
		if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
		if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return false;
		color = new RgbColor((byte)(value >> 16), (byte)(value >> 8), (byte)value);
		return true;
	}

	/// <summary>
	/// The standard terminal colour (0-7 normal, 8-15 bright) of the same kind as <paramref name="color"/>:
	/// for a grey or a faint tint, black, dark grey, light grey or white by lightness; otherwise the nearest of red,
	/// yellow, green, cyan, blue and magenta by hue, bright when the colour is light. A sixteen-colour
	/// client shows a pastel blue as blue this way, where the nearest colour by RGB is often grey.
	/// </summary>
	public static int StandardSlot(RgbColor color)
	{
		var (l, c, h) = ToOklch(color);
		if (c < 0.065 || double.IsNaN(h))
			return l switch { < 0.3 => 0, < 0.6 => 8, < 0.85 => 7, _ => 15 };
		var slot = 0;
		var nearest = double.MaxValue;
		foreach (var (anchor, index) in HueAnchors)
		{
			var distance = Math.Abs(Turn(h, anchor));
			if (distance < nearest) (nearest, slot) = (distance, index);
		}
		return l > 0.7 ? slot + 8 : slot;
	}

	/// <summary>
	/// Where each standard colour sits on the OKLCH wheel. Yellow is pulled toward orange, as the
	/// VGA "yellow" a MU* client shows is brown.
	/// </summary>
	private static readonly (double Hue, int Slot)[] HueAnchors = [(25, 1), (80, 3), (145, 2), (195, 6), (260, 4), (330, 5)];

	/// <summary>The usual VGA value of standard colour <paramref name="slot"/> (0-15), as most MU* clients draw it.</summary>
	public static RgbColor StandardColor(int slot)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(slot);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 15);
		return Vga[slot];
	}

	private static readonly RgbColor[] Vga =
	[
		new(0, 0, 0), new(170, 0, 0), new(0, 170, 0), new(170, 85, 0),
		new(0, 0, 170), new(170, 0, 170), new(0, 170, 170), new(170, 170, 170),
		new(85, 85, 85), new(255, 85, 85), new(85, 255, 85), new(255, 255, 85),
		new(85, 85, 255), new(255, 85, 255), new(85, 255, 255), new(255, 255, 255),
	];
}
