namespace MarkupString.Ansi;

/// <summary>Resizing a picture's RGBA pixels for a terminal.</summary>
internal static class PictureScaler
{
	/// <summary>
	/// The largest size within <paramref name="boxWidth"/> by <paramref name="boxHeight"/> with the picture's shape,
	/// at least a pixel each way; no larger than the picture itself unless <paramref name="upscale"/>.
	/// </summary>
	internal static (int Width, int Height) FitWithin(int width, int height, int boxWidth, int boxHeight, bool upscale)
	{
		var scale = Math.Min((double)boxWidth / width, (double)boxHeight / height);
		if (!upscale) scale = Math.Min(1, scale);
		return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
	}

	/// <summary>
	/// <paramref name="source"/> resized to <paramref name="width"/> by <paramref name="height"/>: each new pixel the
	/// average of the pixels it covers, weighted by how opaque they are so a transparent edge does not darken.
	/// </summary>
	internal static byte[] Scale(ReadOnlySpan<byte> source, int sourceWidth, int sourceHeight, int width, int height)
	{
		var result = new byte[width * height * 4];
		if (width == sourceWidth && height == sourceHeight)
		{
			source.CopyTo(result);
			return result;
		}

		// Each output column's span of source columns, worked out once rather than for every row.
		var spans = new int[width + 1];
		for (var x = 0; x <= width; x++) spans[x] = (int)((long)x * sourceWidth / width);

		for (var y = 0; y < height; y++)
		{
			var y0 = (int)((long)y * sourceHeight / height);
			var y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * sourceHeight / height));
			for (var x = 0; x < width; x++)
			{
				var x0 = spans[x];
				var x1 = Math.Max(x0 + 1, spans[x + 1]);
				var o = (y * width + x) * 4;

				// Enlarging, or a reduction small enough that a pixel covers one source pixel: a copy.
				if (y1 - y0 == 1 && x1 - x0 == 1)
				{
					source.Slice((y0 * sourceWidth + x0) * 4, 4).CopyTo(result.AsSpan(o, 4));
					continue;
				}

				long r = 0, g = 0, b = 0, a = 0;
				for (var sy = y0; sy < y1; sy++)
				{
					var row = source.Slice((sy * sourceWidth + x0) * 4, (x1 - x0) * 4);
					for (var i = 0; i < row.Length; i += 4)
					{
						var alpha = row[i + 3];
						r += row[i] * alpha;
						g += row[i + 1] * alpha;
						b += row[i + 2] * alpha;
						a += alpha;
					}
				}

				if (a > 0)
				{
					result[o] = (byte)(r / a);
					result[o + 1] = (byte)(g / a);
					result[o + 2] = (byte)(b / a);
				}
				result[o + 3] = (byte)(a / ((long)(y1 - y0) * (x1 - x0)));
			}
		}

		return result;
	}

	/// <summary>
	/// <paramref name="picture"/> as large as fits <paramref name="width"/> by <paramref name="height"/> with its
	/// shape kept, centred, the rest transparent.
	/// </summary>
	internal static byte[] Letterbox(TerminalPicture picture, int width, int height)
	{
		var (fitWidth, fitHeight) = FitWithin(picture.Width, picture.Height, width, height, upscale: true);
		fitWidth = Math.Min(fitWidth, width);
		fitHeight = Math.Min(fitHeight, height);
		var scaled = Scale(picture.Rgba.Span, picture.Width, picture.Height, fitWidth, fitHeight);
		if (fitWidth == width && fitHeight == height) return scaled;

		var result = new byte[width * height * 4];
		var left = (width - fitWidth) / 2;
		var top = (height - fitHeight) / 2;
		for (var y = 0; y < fitHeight; y++)
			scaled.AsSpan(y * fitWidth * 4, fitWidth * 4).CopyTo(result.AsSpan(((top + y) * width + left) * 4));
		return result;
	}
}
