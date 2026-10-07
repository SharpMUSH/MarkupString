using System.Buffers;
namespace MarkupString.Ansi;

/// <summary>
/// Writes RGBA pixels as sixel graphics: the 216-colour cube for a palette, a transparent background
/// (<c>P2 = 1</c>), square pixels, and runs of a repeated column compressed with <c>!</c>.
/// </summary>
internal static class SixelWriter
{
	/// <summary>Writes <paramref name="pixels"/>; <paramref name="height"/> is a whole number of six-pixel bands.</summary>
	internal static void Write(byte[] pixels, int width, int height, IBufferWriter<char> output)
	{
		// Each pixel's palette entry, or -1 where it is transparent and left as the screen was.
		var index = new short[width * height];
		var used = new bool[216];
		for (var i = 0; i < index.Length; i++)
		{
			var o = i * 4;
			if (pixels[o + 3] < 128)
			{
				index[i] = -1;
				continue;
			}

			var entry = Level(pixels[o]) * 36 + Level(pixels[o + 1]) * 6 + Level(pixels[o + 2]);
			index[i] = (short)entry;
			used[entry] = true;
		}

		output.Write($"\eP0;1;0q\"1;1;{width};{height}");
		for (var entry = 0; entry < 216; entry++)
		{
			if (!used[entry]) continue;
			output.Write($"#{entry};2;{entry / 36 * 20};{entry / 6 % 6 * 20};{entry % 6 * 20}");
		}

		var bandColours = new List<int>();
		var line = new char[width];
		for (var top = 0; top < height; top += 6)
		{
			bandColours.Clear();
			var seen = new bool[216];
			for (var y = top; y < Math.Min(top + 6, height); y++)
				for (var x = 0; x < width; x++)
					if (index[y * width + x] is var e and >= 0 && !seen[e])
					{
						seen[e] = true;
						bandColours.Add(e);
					}

			for (var c = 0; c < bandColours.Count; c++)
			{
				var colour = bandColours[c];
				for (var x = 0; x < width; x++)
				{
					var bits = 0;
					for (var bit = 0; bit < 6 && top + bit < height; bit++)
						if (index[(top + bit) * width + x] == colour) bits |= 1 << bit;
					line[x] = (char)('?' + bits);
				}

				output.Write($"#{colour}");
				WriteRuns(line, output);
				if (c < bandColours.Count - 1) output.Write("$");
			}

			if (top + 6 < height) output.Write("-");
		}

		output.Write("\e\\");
	}

	/// <summary>A channel's nearest of the cube's six levels.</summary>
	private static int Level(byte value) => (value * 5 + 127) / 255;

	private static void WriteRuns(char[] line, IBufferWriter<char> output)
	{
		var x = 0;
		while (x < line.Length)
		{
			var run = 1;
			while (x + run < line.Length && line[x + run] == line[x]) run++;
			if (run > 3) output.Write($"!{run}{line[x]}");
			else for (var i = 0; i < run; i++) output.Write([line[x]]);
			x += run;
		}
	}
}
