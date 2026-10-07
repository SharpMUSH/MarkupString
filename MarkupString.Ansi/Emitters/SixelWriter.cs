using System.Buffers;
using System.Globalization;
using System.Text;
namespace MarkupString.Ansi;

/// <summary>
/// Writes RGBA pixels as sixel graphics: the 216-colour cube for a palette (each pixel's entry is arithmetic,
/// with no search), a transparent background (<c>P2 = 1</c>) when any pixel is transparent, square pixels,
/// and runs of a repeated column compressed with <c>!</c>.
/// </summary>
/// <remarks>
/// Each six-pixel band is read once, setting each pixel's bit in the column of its colour's line, so the
/// work is the pixels plus one line per colour the band uses rather than a pass over the band per colour.
/// Only the colours a band uses are written for it.
/// </remarks>
internal static class SixelWriter
{
	private const int Colours = 216;

	/// <summary>Encodes <paramref name="pixels"/>; <paramref name="height"/> is a whole number of six-pixel bands.</summary>
	internal static string Encode(byte[] pixels, int width, int height)
	{
		var pixelCount = width * height;
		var index = ArrayPool<byte>.Shared.Rent(pixelCount);
		// Each colour's sixel line for the band being written: one byte of six bits per column.
		var lines = ArrayPool<byte>.Shared.Rent(Colours * width);
		var line = ArrayPool<char>.Shared.Rent(width);
		try
		{
			// Each pixel's palette entry, or 255 where it is transparent and left as the screen was.
			Span<bool> used = stackalloc bool[Colours];
			for (var i = 0; i < pixelCount; i++)
			{
				var o = i * 4;
				if (pixels[o + 3] < 128)
				{
					index[i] = byte.MaxValue;
					continue;
				}

				var entry = Level(pixels[o]) * 36 + Level(pixels[o + 1]) * 6 + Level(pixels[o + 2]);
				index[i] = (byte)entry;
				used[entry] = true;
			}

			var text = new StringBuilder(pixelCount / 4 + 4096);
			// P2 = 1 leaves unset pixels as the screen was, which a picture with transparent parts or a letterbox
			// needs; an opaque one is sent P2 = 0, which some terminals (foot) draw faster.
			var transparent = index.AsSpan(0, pixelCount).Contains(byte.MaxValue);
			text.Append(CultureInfo.InvariantCulture, $"\eP0;{(transparent ? 1 : 0)};0q\"1;1;{width};{height}");
			for (var entry = 0; entry < Colours; entry++)
			{
				if (!used[entry]) continue;
				text.Append('#').Append(entry).Append(";2;")
					.Append(entry / 36 * 20).Append(';').Append(entry / 6 % 6 * 20).Append(';').Append(entry % 6 * 20);
			}

			Span<byte> bandColours = stackalloc byte[Colours];
			Span<bool> inBand = stackalloc bool[Colours];
			// The columns each colour of the band reaches, so a colour in one corner is not a line the band's width.
			Span<int> first = stackalloc int[Colours];
			Span<int> last = stackalloc int[Colours];
			for (var top = 0; top < height; top += 6)
			{
				var count = 0;
				var bottom = Math.Min(top + 6, height);
				for (var y = top; y < bottom; y++)
				{
					var bit = (byte)(1 << (y - top));
					var row = y * width;
					for (var x = 0; x < width; x++)
					{
						var colour = index[row + x];
						if (colour == byte.MaxValue) continue;
						if (!inBand[colour])
						{
							inBand[colour] = true;
							bandColours[count++] = colour;
							lines.AsSpan(colour * width, width).Clear();
							first[colour] = x;
							last[colour] = x;
						}
						else if (x < first[colour]) first[colour] = x;
						else if (x > last[colour]) last[colour] = x;

						lines[colour * width + x] |= bit;
					}
				}

				for (var c = 0; c < count; c++)
				{
					var colour = bandColours[c];
					inBand[colour] = false;
					var from = first[colour];
					var to = last[colour] + 1;
					var bits = lines.AsSpan(colour * width + from, to - from);
					for (var x = 0; x < bits.Length; x++) line[x] = (char)('?' + bits[x]);

					text.Append('#').Append(colour);
					if (from > 3) text.Append('!').Append(from).Append('?');
					else text.Append('?', from);
					AppendRuns(line.AsSpan(0, bits.Length), text);
					if (c < count - 1) text.Append('$');
				}

				if (bottom < height) text.Append('-');
			}

			text.Append("\e\\");
			return text.ToString();
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(index);
			ArrayPool<byte>.Shared.Return(lines);
			ArrayPool<char>.Shared.Return(line);
		}
	}

	/// <summary>A channel's nearest of the cube's six levels.</summary>
	private static int Level(byte value) => (value * 5 + 127) / 255;

	/// <summary>
	/// <paramref name="line"/> with each run of more than three equal sixels written <c>!n</c>, and the empty
	/// sixels at its end left off, since the next line starts from the band's left edge anyway.
	/// </summary>
	private static void AppendRuns(ReadOnlySpan<char> line, StringBuilder text)
	{
		var end = line.Length;
		while (end > 0 && line[end - 1] == '?') end--;

		// Sixels that are not part of a long run are copied in stretches rather than one at a time.
		var literal = 0;
		var x = 0;
		while (x < end)
		{
			var run = 1;
			while (x + run < end && line[x + run] == line[x]) run++;
			if (run > 3)
			{
				text.Append(line[literal..x]).Append('!').Append(run).Append(line[x]);
				literal = x + run;
			}

			x += run;
		}

		text.Append(line[literal..end]);
	}
}
