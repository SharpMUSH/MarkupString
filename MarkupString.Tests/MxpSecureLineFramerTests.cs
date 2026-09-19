using MarkupString.Ansi;
namespace MarkupString.Tests;

/// <summary>
/// <see cref="ILineFramer"/> through <see cref="MxpSecureLineFramer"/>: an MXP client reads tags only
/// on a line opened in secure mode, so every line that has content gets <c>ESC[1z</c>, and only
/// where the registry asked for it.
/// </summary>
public class MxpSecureLineFramerTests
{
	private const string Secure = "\e[1z";

	private static readonly MarkupRegistry Plain = MarkupRegistry.Empty.WithAnsi();
	private static readonly MarkupRegistry Wire = MarkupRegistry.Empty.WithAnsi().WithMxpSecureLines();

	private static readonly MarkupText Link = MarkupText.Wrap(
		AnsiMarkup.Create(linkUrl: "north", linkKind: LinkKind.Command), "north");

	[Test]
	public async Task EveryLineWithContent_OpensInSecureMode()
	{
		var text = MarkupText.Concat(MarkupText.Concat(MarkupText.Plain("Exits:\n"), Link), MarkupText.Plain("\r\nlast"));

		await Assert.That(text.Render(MarkupFormat.Mxp, Wire))
			.IsEqualTo($"{Secure}Exits:\n{Secure}<SEND HREF=\"north\">north</SEND>\r\n{Secure}last");
	}

	[Test]
	public async Task AnEmptyLine_GetsNothing()
	{
		await Assert.That(MarkupText.Plain("a\n\nb\r\n\r\nc\n").Render(MarkupFormat.Mxp, Wire))
			.IsEqualTo($"{Secure}a\n\n{Secure}b\r\n\r\n{Secure}c\n");
	}

	[Test]
	public async Task OnlyMxp_AndOnlyWhereTheRegistryAskedForIt()
	{
		await Assert.That(Link.Render(MarkupFormat.Mxp, Plain)).DoesNotContain(Secure)
			.Because("a render for a test, a log or a preview wants the tags alone");
		await Assert.That(Link.Render(MarkupFormat.Pueblo, Wire)).DoesNotContain(Secure);
		await Assert.That(Link.Render(MarkupFormat.Ansi, Wire)).DoesNotContain(Secure);
	}

	private sealed class PreFramer : IFormatFramer
	{
		public MarkupFormat Format => MarkupFormat.Mxp;
		public void WritePreamble(System.Buffers.IBufferWriter<char> output) => output.Write("<PRE>");
		public void WriteEpilogue(bool anyRunEmitted, System.Buffers.IBufferWriter<char> output) => output.Write("\n</PRE>");
	}

	[Test]
	public async Task ADocumentFramer_WrapsTheLineFraming()
	{
		var registry = Wire.With(new PreFramer());

		await Assert.That(MarkupText.Plain("a\nb").Render(MarkupFormat.Mxp, registry))
			.IsEqualTo($"<PRE>{Secure}a\n{Secure}b\n</PRE>")
			.Because("the preamble comes before the first line's prefix, and the epilogue is not a line of the text");
	}

	[Test]
	public async Task TheRegistry_FindsTheLineFramer_AndAReplacementWins()
	{
		await Assert.That(Wire.FindLineFramer(MarkupFormat.Mxp)).IsSameReferenceAs(MxpSecureLineFramer.Instance);
		await Assert.That(Plain.FindLineFramer(MarkupFormat.Mxp)).IsNull();
		await Assert.That(Wire.FindFramer(MarkupFormat.Mxp)).IsNull()
			.Because("a line framer has its own slot, and does not displace a document framer");
	}
}
