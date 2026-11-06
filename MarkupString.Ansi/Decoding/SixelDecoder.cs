namespace MarkupString.Ansi.Decoding;

/// <summary>Decodes sixel data (the part of <c>DCS … q … ST</c> after the <c>q</c>) to RGBA.</summary>
internal static class SixelDecoder
{
	// The VT340's sixteen colours, in percent.
	private static readonly (int R, int G, int B)[] Vt340 =
	[
		(0, 0, 0), (20, 20, 80), (80, 13, 13), (20, 80, 20), (80, 20, 80), (20, 80, 80), (80, 80, 20), (53, 53, 53),
		(26, 26, 26), (33, 33, 60), (60, 26, 26), (33, 60, 33), (60, 33, 60), (33, 60, 60), (60, 60, 33), (80, 80, 80),
	];

	/// <summary>Decodes <paramref name="data"/>.</summary>
	/// <param name="data">The sixel data.</param>
	/// <param name="transparent">Whether pixels no sixel sets are transparent (P2 = 1) rather than colour 0.</param>
	/// <param name="maxPixels">The largest picture taken.</param>
	/// <param name="width">Its width.</param>
	/// <param name="height">Its height.</param>
	/// <param name="rgba">Its pixels.</param>
	public static bool TryDecode(ReadOnlySpan<char> data, bool transparent, long maxPixels, out int width, out int height, out byte[] rgba)
	{
		width = height = 0;
		rgba = [];

		var palette = new uint[256];
		for (var i = 0; i < palette.Length; i++)
		{
			var (r, g, b) = Vt340[i % Vt340.Length];
			palette[i] = Pack(Percent(r), Percent(g), Percent(b));
		}

		var canvas = new Canvas(maxPixels);
		var color = palette[0];
		int x = 0, band = 0;
		var i0 = 0;

		while (i0 < data.Length)
		{
			var c = data[i0++];
			switch (c)
			{
				case '"':
					{
						// Raster attributes: aspect numerator and denominator, then width and height.
						var values = Numbers(data, ref i0, 4);
						if (values.Count >= 4 && values[2] > 0 && values[3] > 0)
						{
							if (!canvas.Reserve(values[2], values[3])) return false;
						}

						break;
					}
				case '#':
					{
						var values = Numbers(data, ref i0, 5);
						if (values.Count == 0) break;
						var register = Math.Clamp(values[0], 0, 255);
						if (values.Count >= 5)
						{
							palette[register] = values[1] == 1
								? Hls(values[2], values[3], values[4])
								: Pack(Percent(values[2]), Percent(values[3]), Percent(values[4]));
						}

						color = palette[register];
						break;
					}
				case '!':
					{
						var values = Numbers(data, ref i0, 1);
						var count = values.Count > 0 ? Math.Max(1, values[0]) : 1;
						if (i0 < data.Length && data[i0] is >= '?' and <= '~')
						{
							if (!canvas.Put(x, band, data[i0] - '?', count, color)) return false;
							x += count;
							i0++;
						}

						break;
					}
				case '$':
					x = 0;
					break;
				case '-':
					x = 0;
					band++;
					break;
				case >= '?' and <= '~':
					if (!canvas.Put(x, band, c - '?', 1, color)) return false;
					x++;
					break;
			}
		}

		if (canvas.Width == 0 || canvas.Height == 0) return false;
		width = canvas.Width;
		height = canvas.Height;
		rgba = canvas.ToRgba(transparent ? 0u : palette[0] | 0xFF000000u);
		return true;
	}

	private static List<int> Numbers(ReadOnlySpan<char> data, ref int i, int max)
	{
		var values = new List<int>(max);
		var value = 0;
		var any = false;
		while (i < data.Length)
		{
			var c = data[i];
			if (c is >= '0' and <= '9')
			{
				value = Math.Min(value * 10 + (c - '0'), 1 << 20);
				any = true;
			}
			else if (c == ';')
			{
				values.Add(value);
				value = 0;
				any = false;
			}
			else
			{
				break;
			}

			i++;
		}

		if (any || values.Count > 0) values.Add(value);
		return values;
	}

	private static byte Percent(int value) => (byte)Math.Round(Math.Clamp(value, 0, 100) * 255 / 100.0);

	private static uint Pack(byte r, byte g, byte b) => 0xFF000000u | ((uint)b << 16) | ((uint)g << 8) | r;

	/// <summary>A sixel HLS colour: hue in degrees with blue at 0, lightness and saturation in percent.</summary>
	private static uint Hls(int hue, int lightness, int saturation)
	{
		var h = ((hue + 240) % 360) / 360.0;
		double l = Math.Clamp(lightness, 0, 100) / 100.0, s = Math.Clamp(saturation, 0, 100) / 100.0;
		if (s == 0) return Pack((byte)(l * 255), (byte)(l * 255), (byte)(l * 255));
		var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
		var p = 2 * l - q;
		return Pack(Channel(p, q, h + 1 / 3.0), Channel(p, q, h), Channel(p, q, h - 1 / 3.0));
	}

	private static byte Channel(double p, double q, double t)
	{
		if (t < 0) t += 1;
		if (t > 1) t -= 1;
		var v = t < 1 / 6.0 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6 : p;
		return (byte)Math.Round(v * 255);
	}

	private sealed class Canvas(long maxPixels)
	{
		private uint[] _pixels = [];
		private int _stride;
		private int _rows;

		public int Width { get; private set; }

		public int Height { get; private set; }

		public bool Reserve(int width, int height)
		{
			if (!Grow(width, height)) return false;
			Width = Math.Max(Width, width);
			Height = Math.Max(Height, height);
			return true;
		}

		public bool Put(int x, int band, int bits, int count, uint color)
		{
			if (bits == 0)
			{
				// Nothing drawn, but the picture is at least this wide.
				return Extend(x + count, band * 6 + 1);
			}

			var top = band * 6;
			if (!Extend(x + count, top + 6)) return false;
			for (var bit = 0; bit < 6; bit++)
			{
				if ((bits & (1 << bit)) == 0) continue;
				var y = top + bit;
				Height = Math.Max(Height, y + 1);
				_pixels.AsSpan(y * _stride + x, count).Fill(color);
			}

			return true;
		}

		public byte[] ToRgba(uint background)
		{
			var rgba = new byte[(long)Width * Height * 4];
			var output = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(rgba.AsSpan());
			for (var y = 0; y < Height; y++)
			{
				for (var x = 0; x < Width; x++)
				{
					var pixel = _pixels[y * _stride + x];
					output[y * Width + x] = pixel == 0 ? background : pixel;
				}
			}

			if (!BitConverter.IsLittleEndian)
			{
				for (var i = 0; i < output.Length; i++) output[i] = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(output[i]);
			}

			return rgba;
		}

		private bool Extend(int width, int height)
		{
			if (!Grow(width, height)) return false;
			Width = Math.Max(Width, width);
			return true;
		}

		private bool Grow(int width, int height)
		{
			if ((long)width * height > maxPixels) return false;
			if (width <= _stride && height <= _rows) return true;

			var stride = Math.Max(_stride, width);
			var rows = Math.Max(_rows, height);
			if (width > _stride) stride = (int)Math.Min(Math.Max(stride, _stride * 2L), Math.Max(width, maxPixels / Math.Max(1, rows)));
			if (height > _rows) rows = (int)Math.Min(Math.Max(rows, _rows * 2L), Math.Max(height, maxPixels / Math.Max(1, stride)));
			if ((long)stride * rows > maxPixels)
			{
				stride = width > _stride ? width : _stride;
				rows = height > _rows ? height : _rows;
			}

			var pixels = new uint[(long)stride * rows];
			for (var y = 0; y < _rows; y++) _pixels.AsSpan(y * _stride, _stride).CopyTo(pixels.AsSpan(y * stride));
			_pixels = pixels;
			_stride = stride;
			_rows = rows;
			return true;
		}
	}
}
