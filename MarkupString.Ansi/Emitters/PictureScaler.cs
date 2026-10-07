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

		for (var y = 0; y < height; y++)
		{
			var y0 = (int)((long)y * sourceHeight / height);
			var y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * sourceHeight / height));
			for (var x = 0; x < width; x++)
			{
				var x0 = (int)((long)x * sourceWidth / width);
				var x1 = Math.Max(x0 + 1, (int)((long)(x + 1) * sourceWidth / width));
				long r = 0, g = 0, b = 0, a = 0, count = 0;
				for (var sy = y0; sy < y1; sy++)
				{
					var row = sy * sourceWidth * 4;
					for (var sx = x0; sx < x1; sx++)
					{
						var i = row + sx * 4;
						var alpha = source[i + 3];
						r += source[i] * alpha;
						g += source[i + 1] * alpha;
						b += source[i + 2] * alpha;
						a += alpha;
						count++;
					}
				}

				var o = (y * width + x) * 4;
				if (a > 0)
				{
					result[o] = (byte)(r / a);
					result[o + 1] = (byte)(g / a);
					result[o + 2] = (byte)(b / a);
				}
				result[o + 3] = (byte)(a / count);
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
