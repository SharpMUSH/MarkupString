using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using MarkupString.Ansi;
using MarkupString.Layout;

/// <summary>
/// What a terminal is sent beyond colour, by what its connection says it can do: OSC 8 and MSLP links, and
/// pictures drawn into a figure's cells as Kitty placeholders, iTerm2 or sixel images, or half-block art.
/// </summary>
public class TerminalFeatureTests
{
	private const string Esc = "\e";
	private const string Bel = "\u0007";
	private const string Placeholder = "\U0010EEEE";

	private sealed class Source(TerminalPicture? picture) : ITerminalPictureSource
	{
		public HashSet<uint> Sent { get; } = [];

		public bool TryGetPicture(ImageMarkup image, [NotNullWhen(true)] out TerminalPicture? found)
		{
			found = picture;
			return found is not null;
		}

		public bool MarkTransmitted(uint imageId) => Sent.Add(imageId);
	}

	/// <summary>A picture of two columns, red on the left and blue on the right, opaque.</summary>
	private static TerminalPicture RedBlue(int width = 4, int height = 4)
	{
		var rgba = new byte[width * height * 4];
		for (var y = 0; y < height; y++)
			for (var x = 0; x < width; x++)
			{
				var o = (y * width + x) * 4;
				if (x < width / 2) rgba[o] = 255;
				else rgba[o + 2] = 255;
				rgba[o + 3] = 255;
			}
		return new TerminalPicture("red-blue", width, height, rgba);
	}

	private static readonly ImageMarkup Cat = new("cat.png", "A cat");

	private static MarkupText Render(MarkupText text, AnsiOutputOptions options) =>
		MarkupText.Plain(text.Render(MarkupFormat.Ansi, MarkupRegistry.Empty.WithAnsi().WithAnsiOutput(options)));

	private static string RenderString(MarkupText text, AnsiOutputOptions options) =>
		text.Render(MarkupFormat.Ansi, MarkupRegistry.Empty.WithAnsi().WithAnsiOutput(options));

	/// <summary>A figure laid out for a reader whose client draws pictures <paramref name="cells"/> big.</summary>
	private static MarkupText Laid(Block block, int width, PictureCells? cells = null) =>
		BlockLayout.Build(block, width, context: new LayoutContext { Pictures = (_, _) => cells ?? new PictureCells(4, 2) });

	// ── Links ───────────────────────────────────────────────────────────────────

	[Test]
	public async Task AUrlLinkIsAnOsc8HyperlinkOnlyWhenTheClientReadsThem()
	{
		var link = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "https://example.com"), "site");

		await Assert.That(RenderString(link, new AnsiOutputOptions(Features: TerminalFeatures.Hyperlinks)))
			.IsEqualTo($"{Esc}]8;;https://example.com{Bel}site{Esc}]8;;{Bel}");
		await Assert.That(RenderString(link, new AnsiOutputOptions(Features: TerminalFeatures.None))).IsEqualTo("site");
	}

	[Test]
	public async Task ACommandLinkIsAnMslpLinkForAClientThatReadsThem()
	{
		var link = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "look north", linkKind: LinkKind.Command), "north");

		await Assert.That(RenderString(link, new AnsiOutputOptions(Features: TerminalFeatures.CommandLinks)))
			.IsEqualTo($"{Esc}]68;1;SEND;look north{Bel}{Esc}[4mnorth{Esc}[24m");
		await Assert.That(RenderString(link, new AnsiOutputOptions(Features: TerminalFeatures.Hyperlinks))).IsEqualTo("north");
	}

	[Test]
	public async Task AnUnderlinedCommandLinkStaysUnderlinedPastTheLink()
	{
		var link = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "look", linkKind: LinkKind.Command, underlined: true), "here");

		await Assert.That(RenderString(link, new AnsiOutputOptions(Features: TerminalFeatures.CommandLinks)))
			.IsEqualTo($"{Esc}[4m{Esc}]68;1;SEND;look{Bel}{Esc}[4mhere{Esc}[24m{Esc}[4m{Esc}[0m");
	}

	/// <summary>A control character would end the sequence early and let the rest of the command reach the terminal.</summary>
	[Test]
	public async Task ACommandWithAControlCharacterIsItsTextAlone()
	{
		var link = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: $"look{Bel}{Esc}]52;c;evil", linkKind: LinkKind.Command), "here");

		await Assert.That(RenderString(link, new AnsiOutputOptions(Features: TerminalFeatures.CommandLinks))).IsEqualTo("here");
	}

	// ── Layout ──────────────────────────────────────────────────────────────────

	[Test]
	public async Task AFigureForAReaderWithPicturesMarksEachRowOfItsArt()
	{
		var figure = new Figure(Cat, MarkupText.Plain("/\\_/\\\n( o.o )"));

		var laid = Laid(figure, 20);

		await Assert.That(laid.ToPlainText()).IsEqualTo(BlockLayout.Build(figure, 20).ToPlainText());
		var rows = laid.Runs.SelectMany(run => run.Markups).OfType<PictureCellsMarkup>().Distinct().ToArray();
		await Assert.That(rows.Select(r => (r.Row, r.Rows, r.Columns))).IsEquivalentTo(new[] { (0, 2, 7), (1, 2, 7) });
	}

	[Test]
	public async Task AFigureWithNoArtReservesTheCellsItIsGiven()
	{
		var figure = new Figure(Cat, MarkupText.Empty);

		var lines = Laid(figure, 20, new PictureCells(9, 3)).ToPlainText().Split('\n');

		await Assert.That(lines.Select(l => l.TrimEnd())).IsEquivalentTo(new[] { "", "[A cat]", "" });
	}

	[Test]
	public async Task AFigureIsItsArtWhenTheReaderGetsNoPicture()
	{
		var figure = new Figure(Cat, MarkupText.Plain("=^.^="));

		var laid = BlockLayout.Build(figure, 20, context: new LayoutContext { Pictures = (_, _) => null });

		await Assert.That(laid.Runs.SelectMany(run => run.Markups).OfType<PictureCellsMarkup>()).IsEmpty();
	}

	[Test]
	public async Task PictureCellsKeepThePictureShape()
	{
		await Assert.That(PictureCells.Fit(400, 200, 20)).IsEqualTo(new PictureCells(20, 5));
		await Assert.That(PictureCells.Fit(10, 1000, 2)).IsEqualTo(new PictureCells(2, 100));
		await Assert.That(PictureCells.Fit(1000, 10, 4)).IsEqualTo(new PictureCells(4, 1));
	}

	// ── Kitty ───────────────────────────────────────────────────────────────────

	[Test]
	public async Task KittySendsThePictureOnceThenPlaceholders()
	{
		var source = new Source(RedBlue());
		var options = new AnsiOutputOptions(Features: TerminalFeatures.KittyGraphics) { Pictures = source };
		var laid = Laid(new Figure(Cat, MarkupText.Empty), 10);

		var first = RenderString(laid, options);
		var second = RenderString(laid, options);

		await Assert.That(Regex.Matches(first, "a=T,U=1").Count).IsEqualTo(1);
		await Assert.That(first).Contains("f=100,c=4,r=2,q=2");
		await Assert.That(second).DoesNotContain("a=T");
		await Assert.That(Regex.Matches(second, Placeholder).Count).IsEqualTo(8);
		await Assert.That(source.Sent.Count).IsEqualTo(1);
	}

	[Test]
	public async Task KittyPlaceholdersCarryTheirRowAndColumnAndTheIdAsForeground()
	{
		var picture = RedBlue();
		var source = new Source(picture);
		var options = new AnsiOutputOptions(Features: TerminalFeatures.KittyGraphics) { Pictures = source };
		var laid = Laid(new Figure(Cat, MarkupText.Empty), 10, new PictureCells(2, 2));

		var lines = RenderString(laid, options).Split('\n');
		var id = source.Sent.Single();

		await Assert.That(lines[1]).Contains($"{Esc}[38;2;{id >> 16 & 255};{id >> 8 & 255};{id & 255}m");
		// Row 1 (U+030D), columns 0 (U+0305) and 1 (U+030D).
		await Assert.That(lines[1]).Contains($"{Placeholder}̍̅{Placeholder}̍̍");
	}

	/// <summary>The rows of a box round a picture stay the same width whether the picture is drawn or not.</summary>
	[Test]
	public async Task KittyKeepsABoxAroundThePictureAligned()
	{
		var options = new AnsiOutputOptions(Features: TerminalFeatures.KittyGraphics) { Pictures = new Source(RedBlue()) };
		var laid = Laid(new Frame(new Figure(Cat, MarkupText.Plain("=^.^=\n(   )")) { Float = FigureFloat.Left, Beside = new TextBlock(MarkupText.Plain("A cat")) }), 20);

		var widths = RenderString(laid, options).Split('\n').Select(VisibleCells).Distinct().ToArray();

		await Assert.That(widths).IsEquivalentTo(new[] { 20 });
	}

	[Test]
	public async Task KittyWithoutThePixelsSendsTheArt()
	{
		var options = new AnsiOutputOptions(Features: TerminalFeatures.KittyGraphics) { Pictures = new Source(null) };

		var output = RenderString(Laid(new Figure(Cat, MarkupText.Plain("=^.^=")), 10), options);

		await Assert.That(output).StartsWith("=^.^=");
		await Assert.That(output).DoesNotContain(Placeholder);
	}

	[Test]
	public async Task KittySendsEachFrameOfAMovingPictureThenStartsIt()
	{
		var red = RedBlue(2, 2).Rgba;
		var blue = red.ToArray().Reverse().ToArray();
		var picture = new TerminalPicture("moving", 2, 2,
			[new TerminalPictureFrame(red, TimeSpan.FromMilliseconds(100)), new TerminalPictureFrame(blue, TimeSpan.FromMilliseconds(250))]);
		var source = new Source(picture);
		var options = new AnsiOutputOptions(Features: TerminalFeatures.KittyGraphics) { Pictures = source };
		var laid = Laid(new Figure(Cat, MarkupText.Empty), 10);

		var first = RenderString(laid, options);
		var second = RenderString(laid, options);
		var id = source.Sent.Single();

		await Assert.That(Regex.Matches(first, "a=T,U=1").Count).IsEqualTo(1);
		await Assert.That(first).Contains($"a=f,i={id},f=100,X=1,z=250,q=2,m=0;");
		await Assert.That(first).Contains($"a=a,i={id},r=1,z=100,q=2");
		await Assert.That(first.IndexOf($"a=a,i={id},s=3,v=1,q=2", StringComparison.Ordinal))
			.IsGreaterThan(first.IndexOf("a=f,", StringComparison.Ordinal));
		await Assert.That(second).DoesNotContain("a=f,").And.DoesNotContain("a=a,");
	}

	[Test]
	public async Task AMovingPictureIsItsFirstFrameWhereItCannotMove()
	{
		var still = RedBlue(2, 2);
		var moving = new TerminalPicture("moving", 2, 2,
			[new TerminalPictureFrame(still.Rgba, TimeSpan.FromMilliseconds(100)), new TerminalPictureFrame(new byte[16], TimeSpan.FromMilliseconds(100))]);
		var laid = Laid(new Figure(Cat, MarkupText.Empty), 10);
		string Render(TerminalPicture picture) =>
			RenderString(laid, new AnsiOutputOptions(Features: TerminalFeatures.BlockArt) { Pictures = new Source(picture) });

		await Assert.That(Render(moving)).IsEqualTo(Render(still));
		await Assert.That(moving.Rgba.ToArray()).IsEquivalentTo(still.Rgba.ToArray());
	}

	[Test]
	public async Task AMovingPictureNeedsFramesOfItsOwnSize()
	{
		await Assert.That(() => new TerminalPicture("bad", 2, 2,
			[new TerminalPictureFrame(new byte[16], TimeSpan.Zero), new TerminalPictureFrame(new byte[4], TimeSpan.Zero)]))
			.Throws<ArgumentException>();
		await Assert.That(() => new TerminalPicture("none", 2, 2, Array.Empty<TerminalPictureFrame>())).Throws<ArgumentException>();
		await Assert.That(new TerminalPicture("one", 2, 2, [new TerminalPictureFrame(new byte[16], TimeSpan.Zero)]).Frames).IsEmpty();
	}

	[Test]
	public async Task KittyTransmissionIsChunkedAtFourKilobytes()
	{
		var random = new Random(7);
		var noise = new byte[64 * 64 * 4];
		random.NextBytes(noise);
		var options = new AnsiOutputOptions(Features: TerminalFeatures.KittyGraphics) { Pictures = new Source(new TerminalPicture("noise", 64, 64, noise)) };

		var output = RenderString(Laid(new Figure(Cat, MarkupText.Empty), 40, new PictureCells(8, 4)), options);
		var chunks = Regex.Matches(output, $"{Esc}_G([^;]*);([^{Esc}]*){Esc}\\\\").ToArray();

		await Assert.That(chunks.Length).IsGreaterThan(1);
		await Assert.That(chunks.All(c => c.Groups[2].Value.Length <= 4096)).IsTrue();
		await Assert.That(chunks[^1].Groups[1].Value).IsEqualTo("m=0,q=2");
		await Assert.That(chunks.SkipLast(1).Skip(1).All(c => c.Groups[1].Value == "m=1,q=2")).IsTrue();
		var payload = Convert.FromBase64String(string.Concat(chunks.Select(c => c.Groups[2].Value)));
		await Assert.That(payload.Take(4)).IsEquivalentTo(new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // a PNG
	}

	// ── iTerm2 and sixel ────────────────────────────────────────────────────────

	[Test]
	public async Task InlineImagesMakeRoomDrawAndStepOverTheCells()
	{
		var options = new AnsiOutputOptions(Features: TerminalFeatures.InlineImages) { Pictures = new Source(RedBlue()) };

		var lines = RenderString(Laid(new Figure(Cat, MarkupText.Empty), 10, new PictureCells(4, 2)), options).Split('\n');

		await Assert.That(lines[0]).StartsWith($"{Esc}D{Esc}D{Esc}[2A{Esc}7{Esc}]1337;File=inline=1;size=");
		await Assert.That(lines[0]).Contains($";width=4;height=2;preserveAspectRatio=1:");
		await Assert.That(lines[0]).Contains($"{Bel}{Esc}8{Esc}[4C");
		await Assert.That(lines[1]).StartsWith($"{Esc}[4C");
		var base64 = Regex.Match(lines[0], ":([A-Za-z0-9+/=]+)\u0007").Groups[1].Value;
		var png = Convert.FromBase64String(base64);
		await Assert.That(png.Take(8)).IsEquivalentTo(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
		// IEND's CRC is fixed: the encoder's checksum is right if this is.
		await Assert.That(png.TakeLast(4)).IsEquivalentTo(new byte[] { 0xAE, 0x42, 0x60, 0x82 });
	}

	[Test]
	public async Task SixelIsTheCellsSizeInWholeBands()
	{
		var options = new AnsiOutputOptions(Features: TerminalFeatures.Sixel) { Pictures = new Source(RedBlue()), CellWidth = 8, CellHeight = 16 };

		var output = RenderString(Laid(new Figure(Cat, MarkupText.Empty), 10, new PictureCells(2, 2)), options);
		var sixel = Regex.Match(output, $"{Esc}P0;[01];0q\"1;1;(\\d+);(\\d+)(.*?){Esc}\\\\").Groups;

		await Assert.That(sixel[1].Value).IsEqualTo("16");
		await Assert.That(sixel[2].Value).IsEqualTo("30");
		// Red is cube entry 180 (5,0,0), blue entry 5 (0,0,5).
		await Assert.That(sixel[3].Value).Contains("#180;2;100;0;0");
		await Assert.That(sixel[3].Value).Contains("#5;2;0;0;100");
		await Assert.That(Regex.Matches(sixel[3].Value, "-").Count).IsEqualTo(4);
	}

	/// <summary>
	/// Decoding the sixels gives back every pixel's cube colour, and leaves the transparent ones unset: the
	/// band is read once and each colour written only over the columns it reaches, which this pins down.
	/// </summary>
	[Test]
	public async Task SixelDecodesBackToThePicture()
	{
		const int width = 16, height = 12;
		var rgba = new byte[width * height * 4];
		var expected = new int[width * height];
		for (var y = 0; y < height; y++)
			for (var x = 0; x < width; x++)
			{
				var i = y * width + x;
				var o = i * 4;
				if ((x + y) % 7 == 0)
				{
					expected[i] = -1;
					continue;
				}

				// A colour in one corner only, one band-wide stripe, and a checker elsewhere.
				var (r, g, b) = x < 3 && y < 3 ? (0, 255, 0) : y == 7 ? (255, 255, 0) : (x + y) % 2 == 0 ? (255, 0, 0) : (0, 0, 255);
				(rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]) = ((byte)r, (byte)g, (byte)b, 255);
				expected[i] = r / 51 * 36 + g / 51 * 6 + b / 51;
			}

		var options = new AnsiOutputOptions(Features: TerminalFeatures.Sixel)
		{
			Pictures = new Source(new TerminalPicture("sixel-roundtrip", width, height, rgba)),
			CellWidth = 8,
			CellHeight = 12
		};
		var output = RenderString(Laid(new Figure(Cat, MarkupText.Empty), 10, new PictureCells(2, 1)), options);
		var body = Regex.Match(output, $"{Esc}P0;1;0q\"1;1;16;12(.*?){Esc}\\\\").Groups[1].Value;

		await Assert.That(Decode(body, width, height)).IsEquivalentTo(expected);
	}

	/// <summary>The palette entry each pixel of <paramref name="body"/> sets, -1 for none.</summary>
	private static int[] Decode(string body, int width, int height)
	{
		var pixels = Enumerable.Repeat(-1, width * height).ToArray();
		var colour = 0;
		var x = 0;
		var top = 0;
		for (var i = 0; i < body.Length;)
		{
			var c = body[i];
			if (c == '#')
			{
				var match = Regex.Match(body[(i + 1)..], "^(\\d+)(;2;\\d+;\\d+;\\d+)?");
				colour = int.Parse(match.Groups[1].Value);
				i += 1 + match.Length;
				continue;
			}

			var repeat = 1;
			if (c == '!')
			{
				var match = Regex.Match(body[(i + 1)..], "^\\d+");
				repeat = int.Parse(match.Value);
				i += 1 + match.Length;
				c = body[i];
			}

			i++;
			if (c == '$') { x = 0; continue; }
			if (c == '-') { x = 0; top += 6; continue; }
			for (var n = 0; n < repeat; n++, x++)
				for (var bit = 0; bit < 6; bit++)
					if (((c - '?') & (1 << bit)) != 0) pixels[(top + bit) * width + x] = colour;
		}

		return pixels;
	}

	/// <summary>
	/// The PNG inflates and unfilters back to the picture's pixels: RGB with no alpha when every pixel is
	/// opaque, RGBA when any is not, rows after the first filtered Up.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task ThePngDecodesBackToThePicture(bool opaque)
	{
		const int width = 7, height = 5;
		var rgba = new byte[width * height * 4];
		for (var i = 0; i < rgba.Length; i++) rgba[i] = (byte)(i * 37 % 251);
		for (var i = 3; i < rgba.Length; i += 4) rgba[i] = opaque ? (byte)255 : (byte)(i % 256);
		var options = new AnsiOutputOptions(Features: TerminalFeatures.InlineImages)
		{
			Pictures = new Source(new TerminalPicture($"png-{opaque}", width, height, rgba))
		};

		var output = RenderString(Laid(new Figure(Cat, MarkupText.Empty), 10, new PictureCells(4, 2)), options);
		var png = Convert.FromBase64String(Regex.Match(output, ":([A-Za-z0-9+/=]+)\u0007").Groups[1].Value);

		var channels = png[25] == 2 ? 3 : 4;
		await Assert.That(png[25]).IsEqualTo(opaque ? (byte)2 : (byte)6);
		var idatLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33));
		using var inflate = new System.IO.Compression.ZLibStream(new MemoryStream(png, 41, idatLength), System.IO.Compression.CompressionMode.Decompress);
		using var raw = new MemoryStream();
		inflate.CopyTo(raw);
		var data = raw.ToArray();

		var stride = width * channels;
		var decoded = new byte[width * height * 4];
		var previous = new byte[stride];
		for (var y = 0; y < height; y++)
		{
			var filter = data[y * (stride + 1)];
			var row = data.AsSpan(y * (stride + 1) + 1, stride).ToArray();
			if (filter == 2) for (var i = 0; i < stride; i++) row[i] += previous[i];
			for (var x = 0; x < width; x++)
				for (var c = 0; c < 4; c++)
					decoded[(y * width + x) * 4 + c] = c < channels ? row[x * channels + c] : (byte)255;
			previous = row;
		}

		await Assert.That(decoded).IsEquivalentTo(rgba);
	}

	/// <summary>A picture is encoded once for every connection shown it at one size: each is sent the same.</summary>
	[Test]
	public async Task EveryConnectionShownAPictureIsSentTheSameEncoding()
	{
		var picture = RedBlue(32, 32);
		var laid = Laid(new Figure(Cat, MarkupText.Empty), 10, new PictureCells(4, 2));
		var bigger = Laid(new Figure(Cat, MarkupText.Empty), 10, new PictureCells(6, 3));

		foreach (var feature in new[] { TerminalFeatures.KittyGraphics, TerminalFeatures.InlineImages, TerminalFeatures.Sixel, TerminalFeatures.BlockArt })
		{
			string Send(MarkupText text) =>
				RenderString(text, new AnsiOutputOptions(Features: feature) { Pictures = new Source(picture) });

			await Assert.That(Send(laid)).IsEqualTo(Send(laid)).Because($"{feature} for a second connection");
			await Assert.That(Send(bigger)).IsNotEqualTo(Send(laid)).Because($"{feature} at another size");
		}
	}

	// ── Half blocks ─────────────────────────────────────────────────────────────

	[Test]
	public async Task BlockArtDrawsTwoPixelsACell()
	{
		var options = new AnsiOutputOptions(Features: TerminalFeatures.BlockArt) { Pictures = new Source(RedBlue()) };

		var output = RenderString(Laid(new Figure(Cat, MarkupText.Empty), 10, new PictureCells(2, 1)), options);

		await Assert.That(output).IsEqualTo($"{Esc}[38;2;255;0;0;48;2;255;0;0m▀{Esc}[38;2;0;0;255;48;2;0;0;255m▀{Esc}[0m" + new string(' ', 8));
	}

	[Test]
	public async Task BlockArtFollowsTheColourDepth()
	{
		var options = new AnsiOutputOptions(AnsiColorDepth.Standard, TerminalFeatures.BlockArt) { Pictures = new Source(RedBlue()) };

		var output = RenderString(Laid(new Figure(Cat, MarkupText.Empty), 2, new PictureCells(2, 1)), options);

		await Assert.That(output).DoesNotContain("38;2");
		await Assert.That(output.Count(c => c == '▀')).IsEqualTo(2);
	}

	[Test]
	[Arguments(AnsiColorDepth.Attributes)]
	[Arguments(AnsiColorDepth.None)]
	public async Task BlockArtWithoutColourIsTheTextArt(AnsiColorDepth depth)
	{
		var options = new AnsiOutputOptions(depth, TerminalFeatures.BlockArt) { Pictures = new Source(RedBlue()) };

		var output = RenderString(Laid(new Figure(Cat, MarkupText.Empty), 10, new PictureCells(2, 1)), options);

		await Assert.That(output).DoesNotContain("▀");
		await Assert.That(output).DoesNotContain("▄");
	}

	/// <summary>A terminal that does not know its cell size reports 0; that is drawn at the default size, not divided by.</summary>
	[Test]
	[Arguments(TerminalFeatures.Sixel)]
	[Arguments(TerminalFeatures.InlineImages)]
	[Arguments(TerminalFeatures.KittyGraphics)]
	public async Task AnUnknownCellSizeIsTheDefault(TerminalFeatures feature)
	{
		var laid = Laid(new Figure(Cat, MarkupText.Empty), 10, new PictureCells(2, 2));
		var unknown = new AnsiOutputOptions(Features: feature) { Pictures = new Source(RedBlue()), CellWidth = 0, CellHeight = -1 };
		var known = new AnsiOutputOptions(Features: feature) { Pictures = new Source(RedBlue()) };

		await Assert.That(unknown.CellWidth).IsEqualTo(10);
		await Assert.That(unknown.CellHeight).IsEqualTo(20);
		await Assert.That(RenderString(laid, unknown)).IsEqualTo(RenderString(laid, known));
	}

	[Test]
	public async Task KittyIsPreferredWhenTheClientHasSeveralWays()
	{
		var options = new AnsiOutputOptions(Features: TerminalFeatures.Pictures) { Pictures = new Source(RedBlue()) };

		var output = RenderString(Laid(new Figure(Cat, MarkupText.Empty), 10), options);

		await Assert.That(output).Contains(Placeholder);
		await Assert.That(output).DoesNotContain("1337");
	}

	/// <summary>The cells a line covers on a Kitty terminal: escape sequences none, a placeholder and its diacritics one.</summary>
	private static int VisibleCells(string line)
	{
		var text = Regex.Replace(line, $"{Esc}_G.*?{Esc}\\\\|{Esc}\\[[0-9;]*m", "");
		var cells = 0;
		foreach (var rune in text.EnumerateRunes())
		{
			if (Rune.GetUnicodeCategory(rune) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
			cells++;
		}
		return cells;
	}
}
