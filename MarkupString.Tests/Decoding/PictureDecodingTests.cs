using System.Diagnostics.CodeAnalysis;
using MarkupString.Ansi;
using MarkupString.Ansi.Decoding;
using MarkupString.Decoding;
using MarkupString.Display;
using MarkupString.Layout;

/// <summary>Pictures read back from what this package writes for each terminal, and from what other programs send.</summary>
public class PictureDecodingTests
{
	private static readonly DecoderRegistry Registry = VtDecoding.Terminal.WithPictures();
	private static readonly ImageMarkup Cat = new("cat.png", "A cat");

	private sealed class Source(TerminalPicture picture) : ITerminalPictureSource
	{
		public bool TryGetPicture(ImageMarkup image, [NotNullWhen(true)] out TerminalPicture? found)
		{
			found = picture;
			return true;
		}

		public bool MarkTransmitted(uint imageId) => true;
	}

	/// <summary>Red on the left, blue on the right, opaque.</summary>
	private static TerminalPicture RedBlue(int width = 4, int height = 4)
	{
		var rgba = new byte[width * height * 4];
		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
			{
				var o = (y * width + x) * 4;
				if (x < width / 2) rgba[o] = 255;
				else rgba[o + 2] = 255;
				rgba[o + 3] = 255;
			}
		}

		return new TerminalPicture("red-blue", width, height, rgba);
	}

	/// <summary>A figure of the picture in 4 × 2 cells, then a line of text, written for <paramref name="features"/>.</summary>
	private static string Wire(TerminalFeatures features, TerminalPicture picture)
	{
		var laid = BlockLayout.Build(new Figure(Cat, MarkupText.Empty), 10, context: new LayoutContext { Pictures = (_, _) => new PictureCells(4, 2) });
		var options = new AnsiOutputOptions(Features: features) { Pictures = new Source(picture), CellWidth = 8, CellHeight = 16 };
		return laid.Render(MarkupFormat.Ansi, MarkupRegistry.Empty.WithAnsi().WithAnsiOutput(options)) + "\nafter\n";
	}

	private static (IReadOnlyList<DisplayLine> Lines, Decoder Decoder) Decode(string wire)
	{
		var decoder = Registry.CreateDecoder();
		var lines = new List<DisplayLine>(decoder.Feed(wire));
		if (decoder.HasPendingLine) lines.Add(decoder.EndLine());
		return (lines, decoder);
	}

	/// <summary>The picture rows on each line: (row, rows, columns, first column, cells).</summary>
	private static List<(int Row, int Rows, int Columns, int Cells)> PictureRows(IReadOnlyList<DisplayLine> lines)
	{
		var rows = new List<(int, int, int, int)>();
		foreach (var line in lines)
		{
			var layout = CellRenderer.Layout(line.Content, 0, AnsiDisplay.Default, new DisplayOptions());
			var cells = layout.Rows.SelectMany(row => row.Cells).Where(cell => cell.Style.Picture?.Row is not null).ToList();
			if (cells.Count == 0) continue;
			var row = cells[0].Style.Picture!.Row!.Value;
			rows.Add((row.Row, row.Rows, row.Columns, cells.Count));
		}

		return rows;
	}

	private static async Task AssertSamePixels(StoredPicture stored, TerminalPicture expected)
	{
		await Assert.That(stored.Pixels).IsNotNull();
		await Assert.That(stored.Width).IsEqualTo(expected.Width);
		await Assert.That(stored.Height).IsEqualTo(expected.Height);
		await Assert.That(stored.Pixels!.Rgba.ToArray()).IsEquivalentTo(expected.Rgba.ToArray());
	}

	[Test]
	public async Task Kitty_Placeholders_RoundTrip()
	{
		var picture = RedBlue();
		var (lines, decoder) = Decode(Wire(TerminalFeatures.KittyGraphics, picture));

		await Assert.That(PictureRows(lines)).IsEquivalentTo([(0, 2, 4, 4), (1, 2, 4, 4)]);
		await Assert.That(decoder.Pictures().Count).IsEqualTo(1);
		var image = lines.SelectMany(line => line.Content.Runs).SelectMany(run => run.Markups).OfType<ImageMarkup>().First();
		await Assert.That(decoder.Pictures().TryGet(image, out var stored)).IsTrue();
		await AssertSamePixels(stored!, picture);
		await Assert.That(lines[^1].Content.ToPlainText()).IsEqualTo("after");
		await Assert.That(decoder.Diagnostics.PicturesFailed).IsEqualTo(0);
	}

	[Test]
	public async Task InlineImages_Overlay_RoundTrip()
	{
		var picture = RedBlue();
		var (lines, decoder) = Decode(Wire(TerminalFeatures.InlineImages, picture));

		await Assert.That(PictureRows(lines)).IsEquivalentTo([(0, 2, 4, 4), (1, 2, 4, 4)]);
		await Assert.That(decoder.Pictures().TryGet(lines[0].Content.Runs.SelectMany(run => run.Markups).OfType<ImageMarkup>().First(), out var stored)).IsTrue();
		await AssertSamePixels(stored!, picture);
		await Assert.That(stored!.MediaType).IsEqualTo("image/png");
		await Assert.That(lines[^1].Content.ToPlainText()).IsEqualTo("after");
	}

	[Test]
	public async Task Sixel_Overlay_RoundTrip()
	{
		var picture = RedBlue(16, 12);
		var (lines, decoder) = Decode(Wire(TerminalFeatures.Sixel, picture));

		var rows = PictureRows(lines);
		await Assert.That(rows.Select(row => row.Row)).IsEquivalentTo([0, 1]);
		await Assert.That(rows.All(row => row.Columns == 4 && row.Cells == 4)).IsTrue();
		await Assert.That(decoder.Pictures().Count).IsEqualTo(1);
		await Assert.That(lines[^1].Content.ToPlainText()).IsEqualTo("after");
		await Assert.That(decoder.Diagnostics.PicturesFailed).IsEqualTo(0);
	}

	[Test]
	public async Task Sixel_DecodesColoursAndSize()
	{
		// Two colours, red in the left column and blue in the right, six pixels tall, raster 2 × 6.
		var (lines, decoder) = Decode("\eP0;1q\"1;1;2;6#1;2;100;0;0#2;2;0;0;100#1~$#2?~\e\\");
		var stored = decoder.Pictures().TryGet(lines[0].Content.Runs.SelectMany(run => run.Markups).OfType<ImageMarkup>().First(), out var found) ? found : null;
		await Assert.That(stored!.Width).IsEqualTo(2);
		await Assert.That(stored.Height).IsEqualTo(6);
		var rgba = stored.Pixels!.Rgba.ToArray();
		await Assert.That(rgba[..4]).IsEquivalentTo(new byte[] { 255, 0, 0, 255 });
		await Assert.That(rgba[4..8]).IsEquivalentTo(new byte[] { 0, 0, 255, 255 });
	}

	[Test]
	public async Task Kitty_PlacedImage_TakesRowsAndMovesTheCursor()
	{
		// a=T without placeholders: 16 × 32 pixels of RGB at 8 × 16 cells is 2 × 2 cells.
		var rgb = Convert.ToBase64String(new byte[16 * 32 * 3]);
		var (lines, _) = Decode($"ab\e_Ga=T,f=24,s=16,v=32;{rgb}\e\\cd\n");
		await Assert.That(lines.Count).IsEqualTo(3);
		await Assert.That(PictureRows(lines)).IsEquivalentTo([(0, 2, 2, 2), (1, 2, 2, 2)]);
		await Assert.That(lines[0].Content.ToPlainText()).IsEqualTo("ab  ");
		await Assert.That(lines[2].Content.ToPlainText()).IsEqualTo("    cd");
	}

	[Test]
	public async Task Kitty_Chunks_AndReplies()
	{
		var bytes = Convert.ToBase64String(new byte[8 * 16 * 4]);
		var decoder = Registry.CreateDecoder();
		var replies = new List<string>();
		decoder.EventRaised += e => { if (e is ReplyEvent reply) replies.Add(reply.Text); };
		decoder.Feed($"\e_Ga=t,i=7,s=8,v=16,m=1;{bytes[..100]}\e\\\e_Gm=1;{bytes[100..200]}\e\\\e_Gm=0;{bytes[200..]}\e\\");
		decoder.Feed("\e_Ga=p,i=7\e\\");
		decoder.Feed("\e_Ga=p,i=9\e\\");
		decoder.Feed("\e_Ga=t,t=f,i=8;L3RtcC94\e\\");
		await Assert.That(replies).IsEquivalentTo([
			"\e_Gi=7;OK\e\\",
			"\e_Gi=7;OK\e\\",
			"\e_Gi=9;ENOENT:no such image\e\\",
			"\e_Gi=8;EINVAL:only direct transmission is read\e\\",
		]);
		await Assert.That(decoder.Pictures().Count).IsEqualTo(1);
	}

	[Test]
	public async Task Kitty_Quiet_SendsNothing()
	{
		var decoder = Registry.CreateDecoder();
		var replies = 0;
		decoder.EventRaised += e => { if (e is ReplyEvent) replies++; };
		decoder.Feed($"\e_Ga=t,i=7,s=1,v=1,q=2;{Convert.ToBase64String(new byte[4])}\e\\");
		await Assert.That(replies).IsEqualTo(0);
	}

	[Test]
	public async Task Kitty_MovingPicture_RoundTrip()
	{
		var red = RedBlue(2, 2).Rgba.ToArray();
		var blue = red.Reverse().ToArray();
		var picture = new TerminalPicture("moving", 2, 2,
			[new TerminalPictureFrame(red, TimeSpan.FromMilliseconds(100)), new TerminalPictureFrame(blue, TimeSpan.FromMilliseconds(250))]);
		var (lines, decoder) = Decode(Wire(TerminalFeatures.KittyGraphics | TerminalFeatures.MovingPictures, picture));

		var image = lines.SelectMany(line => line.Content.Runs).SelectMany(run => run.Markups).OfType<ImageMarkup>().First();
		await Assert.That(decoder.Pictures().TryGet(image, out var stored)).IsTrue();
		await Assert.That(stored!.IsMoving).IsTrue();
		var frames = stored.Pixels!.Frames;
		await Assert.That(frames.Select(frame => frame.Duration.TotalMilliseconds)).IsEquivalentTo([100.0, 250.0]);
		await Assert.That(frames[0].Rgba.ToArray()).IsEquivalentTo(red);
		await Assert.That(frames[1].Rgba.ToArray()).IsEquivalentTo(blue);
		await Assert.That(stored.MediaType).IsEqualTo("image/gif");
		await Assert.That(stored.ToDataUrl()).StartsWith("data:image/gif;base64,R0lGOD");
		await Assert.That(decoder.Diagnostics.PicturesFailed).IsEqualTo(0);
	}

	[Test]
	public async Task Kitty_Frame_BlendsOverItsBase_AndEditsInPlace()
	{
		// A 2 × 1 black root frame; frame 2 puts half-transparent white over its right pixel; then frame 2's left pixel is
		// replaced with red.
		var decoder = Registry.CreateDecoder();
		decoder.Feed($"\e_Ga=t,i=3,f=32,s=2,v=1;{Convert.ToBase64String([0, 0, 0, 255, 0, 0, 0, 255])}\e\\");
		decoder.Feed($"\e_Ga=f,i=3,f=32,s=1,v=1,x=1,c=1,z=70;{Convert.ToBase64String([255, 255, 255, 128])}\e\\");
		decoder.Feed($"\e_Ga=f,i=3,f=32,s=1,v=1,r=2,X=1;{Convert.ToBase64String([255, 0, 0, 255])}\e\\");
		decoder.Feed("\e_Ga=a,i=3,r=1,z=500\e\\");

		var stored = decoder.Pictures().TryGet(KeyOf(decoder), out var found) ? found : null;
		var frames = stored!.Pixels!.Frames;
		await Assert.That(frames.Count).IsEqualTo(2);
		await Assert.That(frames[0].Rgba.ToArray()).IsEquivalentTo(new byte[] { 0, 0, 0, 255, 0, 0, 0, 255 });
		await Assert.That(frames[1].Rgba.ToArray()).IsEquivalentTo(new byte[] { 255, 0, 0, 255, 128, 128, 128, 255 });
		await Assert.That(frames.Select(frame => frame.Duration.TotalMilliseconds)).IsEquivalentTo([500.0, 70.0]);
		await Assert.That(decoder.Diagnostics.PicturesFailed).IsEqualTo(0);
	}

	[Test]
	public async Task Kitty_Frame_ForAnUnknownImage_Fails()
	{
		var decoder = Registry.CreateDecoder();
		decoder.Feed($"\e_Ga=f,i=4,f=32,s=1,v=1;{Convert.ToBase64String(new byte[4])}\e\\");
		await Assert.That(decoder.Diagnostics.PicturesFailed).IsEqualTo(1);
	}

	private static string KeyOf(Decoder decoder)
	{
		var placed = decoder.Feed("\e_Ga=p,i=3,c=2,r=1\e\\\n");
		return placed.SelectMany(line => line.Content.Runs).SelectMany(run => run.Markups).OfType<ImageMarkup>().First().Source[PictureStore.Scheme.Length..];
	}

	[Test]
	public async Task InlineImage_NotInline_IsADownload()
	{
		var decoder = Registry.CreateDecoder();
		var events = new List<DecodeEvent>();
		decoder.EventRaised += events.Add;
		decoder.Feed($"\e]1337;File=name={Convert.ToBase64String("a.txt"u8.ToArray())};size=3:{Convert.ToBase64String("abc"u8.ToArray())}\a");
		await Assert.That(events.OfType<DownloadEvent>().Single()).IsEqualTo(new DownloadEvent("a.txt", 3));
		await Assert.That(decoder.Pictures().Count).IsEqualTo(0);
	}

	[Test]
	public async Task InlineImage_Gif_IsKeptForABrowser()
	{
		// A 1 × 1 GIF; its size comes from the header.
		var gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
		var decoder = Registry.CreateDecoder();
		var lines = decoder.Feed($"\e]1337;File=inline=1;width=3:{Convert.ToBase64String(gif)}\a\n");
		var image = lines[0].Content.Runs.SelectMany(run => run.Markups).OfType<ImageMarkup>().Single();
		await Assert.That(decoder.Pictures().TryGet(image, out var stored)).IsTrue();
		await Assert.That(stored!.Pixels).IsNull();
		await Assert.That(stored.MediaType).IsEqualTo("image/gif");
		await Assert.That(stored.ToDataUrl()).StartsWith("data:image/gif;base64,R0lGOD");
		await Assert.That(image.Row!.Value.Columns).IsEqualTo(3);
	}

	[Test]
	public async Task Store_EvictsTheLeastRecentlyUsed()
	{
		var store = new PictureStore { Budget = 100 };
		var first = store.Add(4, 4, new byte[64]);
		var second = store.Add(4, 4, Enumerable.Repeat((byte)1, 64).ToArray());
		await Assert.That(store.TryGet(first.Key, out _)).IsFalse();
		await Assert.That(store.TryGet(second.Key, out _)).IsTrue();
		await Assert.That(store.Used).IsEqualTo(64);
	}

	[Test]
	public async Task Store_SamePictureTwice_IsOne()
	{
		var store = new PictureStore();
		var a = store.Add(1, 1, [1, 2, 3, 4]);
		var b = store.Add(1, 1, [1, 2, 3, 4]);
		await Assert.That(b).IsSameReferenceAs(a);
		await Assert.That(store.Count).IsEqualTo(1);
	}

	[Test]
	public async Task Png_DecodesPaletteAndInterlace()
	{
		// 2 × 2 interlaced palette PNG: red, green / blue, white.
		var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAMAAAEyb82AAAAADFBMVEX/AAAA/wAAAP/////7AGD2AAAAD0lEQVR4nGNgYGBkYGIGAAASAAe4hQWPAAAAAElFTkSuQmCC");
		var decoder = Registry.CreateDecoder();
		var lines = decoder.Feed($"\e]1337;File=inline=1:{Convert.ToBase64String(png)}\a\n");
		var image = lines[0].Content.Runs.SelectMany(run => run.Markups).OfType<ImageMarkup>().Single();
		await Assert.That(decoder.Pictures().TryGet(image, out var stored)).IsTrue();
		await Assert.That(stored!.Pixels!.Rgba.ToArray()).IsEquivalentTo(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255 });
	}
}
