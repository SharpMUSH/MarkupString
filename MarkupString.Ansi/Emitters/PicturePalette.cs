namespace MarkupString.Ansi;

/// <summary>A bounded, adaptive palette shared by GIF and Sixel encoders.</summary>
internal static class PicturePalette
{
	internal const int Bins = 32768;

	internal static int Bin(ReadOnlySpan<byte> pixels, int offset) =>
		(pixels[offset] >> 3) << 10 | (pixels[offset + 1] >> 3) << 5 | pixels[offset + 2] >> 3;

	/// <summary>
	/// A palette of at most <paramref name="maxColours"/> for the bins in <paramref name="histogram"/>, and each
	/// bin's index in it: the box of bins with the widest channel, weighted by how many pixels it holds, is split
	/// at its median until there are enough boxes, and each box's colour is the average of its pixels.
	/// </summary>
	internal static (List<(byte R, byte G, byte B)> Palette, byte[] Lookup) MedianCut(int[] histogram, int maxColours)
	{
		var used = new List<int>();
		for (var bin = 0; bin < Bins; bin++)
			if (histogram[bin] > 0) used.Add(bin);

		var boxes = new List<(int Start, int Length)>();
		var bins = used.ToArray();
		if (bins.Length > 0) boxes.Add((0, bins.Length));

		while (boxes.Count < maxColours)
		{
			var best = -1;
			long bestScore = 0;
			var bestChannel = 0;
			for (var b = 0; b < boxes.Count; b++)
			{
				var (start, length) = boxes[b];
				if (length < 2) continue;
				var (channel, range) = WidestChannel(bins.AsSpan(start, length));
				long pixels = 0;
				for (var i = start; i < start + length; i++) pixels += histogram[bins[i]];
				var score = range * pixels;
				if (range > 0 && score > bestScore)
				{
					(best, bestScore, bestChannel) = (b, score, channel);
				}
			}

			if (best < 0) break;

			var (boxStart, boxLength) = boxes[best];
			var span = bins.AsSpan(boxStart, boxLength);
			var shift = 10 - bestChannel * 5;
			span.Sort((x, y) => ((x >> shift) & 31).CompareTo((y >> shift) & 31));

			long total = 0;
			foreach (var bin in span) total += histogram[bin];
			long running = 0;
			var split = 1;
			for (var i = 0; i < span.Length - 1; i++)
			{
				running += histogram[span[i]];
				split = i + 1;
				if (running * 2 >= total) break;
			}

			boxes[best] = (boxStart, split);
			boxes.Add((boxStart + split, boxLength - split));
		}

		var palette = new List<(byte, byte, byte)>(boxes.Count);
		var lookup = new byte[Bins];
		foreach (var (start, length) in boxes)
		{
			long r = 0, g = 0, b = 0, count = 0;
			for (var i = start; i < start + length; i++)
			{
				var bin = bins[i];
				long weight = histogram[bin];
				r += (((bin >> 10) & 31) * 8 + 4) * weight;
				g += (((bin >> 5) & 31) * 8 + 4) * weight;
				b += ((bin & 31) * 8 + 4) * weight;
				count += weight;
				lookup[bin] = (byte)palette.Count;
			}

			palette.Add(((byte)(r / count), (byte)(g / count), (byte)(b / count)));
		}

		return (palette, lookup);
	}

	/// <summary>Which channel (0 red, 1 green, 2 blue) spans the most bins in <paramref name="bins"/>, and by how much.</summary>
	private static (int Channel, int Range) WidestChannel(ReadOnlySpan<int> bins)
	{
		var (best, bestRange) = (0, -1);
		for (var channel = 0; channel < 3; channel++)
		{
			var shift = 10 - channel * 5;
			int low = 31, high = 0;
			foreach (var bin in bins)
			{
				var value = (bin >> shift) & 31;
				low = Math.Min(low, value);
				high = Math.Max(high, value);
			}

			if (high - low > bestRange) (best, bestRange) = (channel, high - low);
		}

		return (best, bestRange);
	}

}
