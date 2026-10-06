using System.Collections.Immutable;
using System.Globalization;

namespace MarkupString;

/// <summary>A 24-bit sRGB colour.</summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
	/// <summary>The colour as a lowercase <c>#rrggbb</c> string.</summary>
	public string ToHex() => string.Create(CultureInfo.InvariantCulture, $"#{R:x2}{G:x2}{B:x2}");
}

/// <summary>
/// A markup layer that colours its text's foreground, and can be made again in another colour. A
/// format's colour markup implements it so the format-free layout code can shade a gradient with it.
/// </summary>
public interface IColorMarkup : IMarkup
{
	/// <summary>The foreground this layer sets, or <see langword="null"/> when it sets none or only the terminal knows it.</summary>
	RgbColor? Foreground { get; }

	/// <summary>The same layer with its foreground replaced.</summary>
	IColorMarkup WithForeground(RgbColor color);
}

/// <summary>The space a <see cref="ColorGradient"/> blends its colours in.</summary>
public enum GradientSpace
{
	/// <summary>
	/// Lightness, chroma and hue in Oklab's polar form, hue taking the shorter way round. Keeps the
	/// midpoints as vivid and as evenly bright as the ends; red to green passes through yellow.
	/// </summary>
	Oklch,

	/// <summary>
	/// Oklab, straight across. Perceptually even, with no hue swing; colours far apart in hue meet in
	/// a softer middle. What CSS blends in when no space is named.
	/// </summary>
	Oklab,

	/// <summary>Hue, saturation and lightness, hue taking the shorter way round: a brighter, uneven rainbow sweep.</summary>
	Hsl,
}

/// <summary>
/// Colours blended one into the next, evenly spaced from 0 to 1. <see cref="At"/> gives the colour
/// at a point; <see cref="ToCss()"/> the same gradient for a page. Colours are blended in
/// <see cref="Space"/> rather than in sRGB, whose midpoints go grey and dark.
/// </summary>
/// <param name="Stops">
/// The colours, as colour layers (an ANSI colour, for one): each one's <see cref="IColorMarkup.Foreground"/>
/// is the colour, and the first is the layer a shaded piece is drawn with. Stops without a known
/// colour are ignored.
/// </param>
/// <param name="Space">The space the colours are blended in.</param>
public sealed record ColorGradient(ImmutableArray<IColorMarkup> Stops, GradientSpace Space = GradientSpace.Oklch)
{
	private RgbColor[] Colors => [.. (Stops.IsDefault ? [] : Stops).Select(stop => stop.Foreground).OfType<RgbColor>()];

	/// <summary>Whether there is a colour to draw: at least one stop with a known colour.</summary>
	public bool IsEmpty => Colors.Length == 0;

	/// <summary>The colour at <paramref name="position"/>, from 0 (the first stop) to 1 (the last).</summary>
	public RgbColor At(double position)
	{
		var colors = Colors;
		if (colors.Length == 0) return default;
		if (colors.Length == 1 || !double.IsFinite(position)) return colors[0];

		var scaled = Math.Clamp(position, 0, 1) * (colors.Length - 1);
		var segment = Math.Min((int)scaled, colors.Length - 2);
		return Blend(colors[segment], colors[segment + 1], scaled - segment, Space);
	}

	/// <summary>
	/// <paramref name="text"/> drawn in the colour at <paramref name="position"/>, with the first stop's
	/// layer; the text unchanged when the gradient has no colour.
	/// </summary>
	public MarkupText Paint(MarkupText text, double position)
	{
		ArgumentNullException.ThrowIfNull(text);
		var template = Stops.IsDefault ? null : Stops.FirstOrDefault(stop => stop.Foreground is not null);
		return template is null || text.Length == 0 ? text : MarkupText.Wrap(template.WithForeground(At(position)), text);
	}

	/// <summary>
	/// The gradient as a CSS <c>linear-gradient</c> from left to right, in its own space
	/// (<c>linear-gradient(to right in oklch, …)</c>); empty when there is no colour.
	/// </summary>
	public string ToCss()
	{
		var colors = Colors;
		if (colors.Length == 0) return string.Empty;
		var list = string.Join(", ", (colors.Length == 1 ? [colors[0], colors[0]] : colors).Select(color => color.ToHex()));
		return $"linear-gradient(to right in {Space.ToString().ToLowerInvariant()}, {list})";
	}

	/// <summary>
	/// The gradient as a plain CSS <c>linear-gradient</c> through <paramref name="samples"/> colours
	/// worked out here, for a browser that cannot blend in <see cref="Space"/> itself. Blending in sRGB
	/// between close samples draws nearly the same thing.
	/// </summary>
	public string ToCss(int samples)
	{
		if (IsEmpty) return string.Empty;
		samples = Math.Max(2, samples);
		var list = string.Join(", ", Enumerable.Range(0, samples).Select(i => At(i / (double)(samples - 1)).ToHex()));
		return $"linear-gradient(to right, {list})";
	}

	/// <inheritdoc/>
	public bool Equals(ColorGradient? other) =>
		other is not null && Space == other.Space
		&& (Stops.IsDefault ? [] : Stops).SequenceEqual(other.Stops.IsDefault ? [] : other.Stops);

	/// <inheritdoc/>
	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(Space);
		foreach (var stop in Stops.IsDefault ? [] : Stops) hash.Add(stop);
		return hash.ToHashCode();
	}

	/// <summary>The colour <paramref name="t"/> of the way from <paramref name="from"/> to <paramref name="to"/>.</summary>
	public static RgbColor Blend(RgbColor from, RgbColor to, double t, GradientSpace space)
	{
		t = Math.Clamp(t, 0, 1);
		switch (space)
		{
			case GradientSpace.Hsl:
				{
					var (h1, s1, l1) = ToHsl(from);
					var (h2, s2, l2) = ToHsl(to);
					if (double.IsNaN(h1)) h1 = double.IsNaN(h2) ? 0 : h2;
					if (double.IsNaN(h2)) h2 = h1;
					return FromHsl(Hue(h1, h2, t), Lerp(s1, s2, t), Lerp(l1, l2, t));
				}
			case GradientSpace.Oklab:
				{
					var (l1, a1, b1) = ToOklab(from);
					var (l2, a2, b2) = ToOklab(to);
					return FromOklab(Lerp(l1, l2, t), Lerp(a1, a2, t), Lerp(b1, b2, t));
				}
			default:
				{
					var (l1, c1, h1) = Polar(ToOklab(from));
					var (l2, c2, h2) = Polar(ToOklab(to));
					if (double.IsNaN(h1)) h1 = double.IsNaN(h2) ? 0 : h2;
					if (double.IsNaN(h2)) h2 = h1;
					var hue = Hue(h1, h2, t) * Math.PI / 180;
					var chroma = Lerp(c1, c2, t);
					return FromOklab(Lerp(l1, l2, t), chroma * Math.Cos(hue), chroma * Math.Sin(hue));
				}
		}
	}

	private static double Lerp(double from, double to, double t) => from + (to - from) * t;

	/// <summary>A hue <paramref name="t"/> of the way round the shorter arc, in degrees.</summary>
	private static double Hue(double from, double to, double t)
	{
		var delta = to - from;
		if (delta > 180) delta -= 360;
		else if (delta < -180) delta += 360;
		var hue = (from + delta * t) % 360;
		return hue < 0 ? hue + 360 : hue;
	}

	/// <summary>Below this chroma a colour is grey, and its hue means nothing.</summary>
	private const double Achromatic = 0.0004;

	private static (double L, double C, double H) Polar((double L, double A, double B) lab)
	{
		var chroma = Math.Sqrt(lab.A * lab.A + lab.B * lab.B);
		var hue = chroma < Achromatic ? double.NaN : Math.Atan2(lab.B, lab.A) * 180 / Math.PI;
		return (lab.L, chroma, hue < 0 ? hue + 360 : hue);
	}

	private static double ToLinear(byte channel)
	{
		var c = channel / 255.0;
		return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
	}

	private static double FromLinear(double c) =>
		c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;

	private static (double L, double A, double B) ToOklab(RgbColor color)
	{
		double r = ToLinear(color.R), g = ToLinear(color.G), b = ToLinear(color.B);
		var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
		var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
		var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
		return (
			0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
			1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
			0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
	}

	private static (double R, double G, double B) LinearRgb(double lightness, double a, double b)
	{
		var l = Math.Pow(lightness + 0.3963377774 * a + 0.2158037573 * b, 3);
		var m = Math.Pow(lightness - 0.1055613458 * a - 0.0638541728 * b, 3);
		var s = Math.Pow(lightness - 0.0894841775 * a - 1.2914855480 * b, 3);
		return (
			4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
			-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
			-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
	}

	/// <summary>
	/// An Oklab colour in sRGB. One outside sRGB loses chroma, keeping its lightness and hue, until it
	/// fits, the way CSS maps a colour into gamut, rather than having its channels clipped.
	/// </summary>
	private static RgbColor FromOklab(double lightness, double a, double b)
	{
		lightness = Math.Clamp(lightness, 0, 1);
		static bool Fits((double R, double G, double B) c) =>
			c.R is >= -1e-6 and <= 1 + 1e-6 && c.G is >= -1e-6 and <= 1 + 1e-6 && c.B is >= -1e-6 and <= 1 + 1e-6;

		var linear = LinearRgb(lightness, a, b);
		if (!Fits(linear))
		{
			double low = 0, high = 1;
			for (var i = 0; i < 24; i++)
			{
				var mid = (low + high) / 2;
				if (Fits(LinearRgb(lightness, a * mid, b * mid))) low = mid;
				else high = mid;
			}
			linear = LinearRgb(lightness, a * low, b * low);
		}
		return new RgbColor(Channel(FromLinear(linear.R)), Channel(FromLinear(linear.G)), Channel(FromLinear(linear.B)));
	}

	private static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);

	private static (double H, double S, double L) ToHsl(RgbColor color)
	{
		double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
		var max = Math.Max(r, Math.Max(g, b));
		var min = Math.Min(r, Math.Min(g, b));
		var lightness = (max + min) / 2;
		var delta = max - min;
		if (delta < 1e-9) return (double.NaN, 0, lightness);

		var saturation = delta / (1 - Math.Abs(2 * lightness - 1));
		var hue = max == r ? (g - b) / delta % 6 : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
		hue *= 60;
		return (hue < 0 ? hue + 360 : hue, saturation, lightness);
	}

	private static RgbColor FromHsl(double hue, double saturation, double lightness)
	{
		var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
		var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
		var m = lightness - chroma / 2;
		var (r, g, b) = (hue % 360) switch
		{
			< 60 => (chroma, x, 0.0),
			< 120 => (x, chroma, 0.0),
			< 180 => (0.0, chroma, x),
			< 240 => (0.0, x, chroma),
			< 300 => (x, 0.0, chroma),
			_ => (chroma, 0.0, x),
		};
		return new RgbColor(Channel(r + m), Channel(g + m), Channel(b + m));
	}
}
