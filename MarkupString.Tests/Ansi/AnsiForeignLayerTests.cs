using System.Buffers;
using MarkupString.Ansi;

/// <summary>
/// The path a run takes when it carries a layer this package does not own. Every set emitter here
/// claims every run, so <c>AnsiEmitterSupport</c> is the only thing standing between a foreign layer and
/// being dropped. It keeps the nesting: foreign layers inside every ANSI layer are applied to the text
/// before the style is (<c>WriteInner</c>), and the rest wrap the styled output (<c>WriteWrapped</c>),
/// each innermost first.
/// </summary>
public class AnsiForeignLayerTests
{
	private const string Esc = "\e";

	/// <summary>A layer this package knows nothing about.</summary>
	private sealed record Tag(string Name) : IMarkup;

	/// <summary>Writes <c>&lt;name&gt;body&lt;/name&gt;</c> for one format.</summary>
	private sealed class TagEmitter(MarkupFormat format) : IMarkupEmitter
	{
		public Type MarkupType => typeof(Tag);
		public MarkupFormat Format => format;

		public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
		{
			var name = ((Tag)markup).Name;
			output.Write($"<{name}>");
			output.Write(body);
			output.Write($"</{name}>");
		}
	}

	/// <summary>
	/// A layer that is bold on a terminal and a <c>&lt;b&gt;</c> element in HTML: it offers a style
	/// for <see cref="MarkupFormat.Ansi"/> only, so it folds there and is delegated everywhere else.
	/// </summary>
	private sealed record BoldTag : IMarkup, IAnsiStyleSource
	{
		public bool TryGetAnsiStyle(MarkupFormat format, out AnsiStyle style)
		{
			style = format == MarkupFormat.Ansi ? AnsiStyle.None with { Bold = true } : AnsiStyle.None;
			return format == MarkupFormat.Ansi;
		}
	}

	private sealed class BoldTagEmitter : IMarkupEmitter
	{
		public Type MarkupType => typeof(BoldTag);
		public MarkupFormat Format => MarkupFormat.Html;

		public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
		{
			output.Write("<b>");
			output.Write(body);
			output.Write("</b>");
		}
	}

	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty
		.WithAnsi()
		.With(new TagEmitter(MarkupFormat.Ansi))
		.With(new TagEmitter(MarkupFormat.Html))
		.With(new BoldTagEmitter());

	private static readonly AnsiMarkup Red = AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false));

	private static string Render(MarkupText text, MarkupFormat format) => text.Render(format, Registry);

	// ── Delegation of layers this package does not own ────────────────────────────

	/// <summary>
	/// Set <c>[Tag("t"), AnsiMarkup(red)]</c>. The tag is inside the colour, so the colour's sequence
	/// and reset bracket it.
	/// </summary>
	[Test]
	public async Task Ansi_ForeignLayerInsideAnAnsiLayer_StaysInsideTheSgr()
	{
		var text = MarkupText.Wrap(Red, MarkupText.Wrap(new Tag("t"), "x"));
		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo($"{Esc}[31m<t>x</t>{Esc}[0m");
	}

	/// <summary>
	/// Set <c>[AnsiMarkup(red), Tag("t")]</c> — the same two layers the other way round, and the tag
	/// wraps the colour.
	/// </summary>
	[Test]
	public async Task Ansi_ForeignLayerOutsideAnAnsiLayer_WrapsTheSgr()
	{
		var text = MarkupText.Wrap(new Tag("t"), MarkupText.Wrap(Red, "x"));
		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo($"<t>{Esc}[31mx{Esc}[0m</t>");
	}

	/// <summary>Set <c>[Tag("a"), Tag("b"), AnsiMarkup(red)]</c>: foreign wrappers nest innermost first.</summary>
	[Test]
	public async Task Ansi_TwoForeignLayers_NestInOrder()
	{
		var text = MarkupText.Wrap(Red, MarkupText.Wrap(new Tag("b"), MarkupText.Wrap(new Tag("a"), "x")));
		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo($"{Esc}[31m<b><a>x</a></b>{Esc}[0m");
	}

	/// <summary>The same set in HTML: both tags are inside the colour, so the span wraps them.</summary>
	[Test]
	public async Task Html_TwoForeignLayers_NestInsideTheSpan()
	{
		var text = MarkupText.Wrap(Red, MarkupText.Wrap(new Tag("b"), MarkupText.Wrap(new Tag("a"), "x")));
		await Assert.That(Render(text, MarkupFormat.Html))
			.IsEqualTo("<span style=\"color: #aa0000\"><b><a>x</a></b></span>");
	}

	/// <summary>A foreign layer alone, with no ANSI layer to fold: only its own emitter runs.</summary>
	[Test]
	public async Task Html_ForeignLayerAlone_RendersOnlyItsOwnTag()
	{
		var text = MarkupText.Wrap(new Tag("t"), "x");
		await Assert.That(Render(text, MarkupFormat.Html)).IsEqualTo("<t>x</t>");
	}

	/// <summary>
	/// No emitter is registered for <c>Tag</c> in BBCode, so the layer contributes nothing and the
	/// body passes through the ANSI rendering untouched.
	/// </summary>
	[Test]
	public async Task BBCode_ForeignLayerWithNoEmitter_PassesItsBodyThrough()
	{
		var text = MarkupText.Wrap(Red, MarkupText.Wrap(new Tag("t"), "x"));
		await Assert.That(Render(text, MarkupFormat.BBCode)).IsEqualTo("[color=#aa0000]x[/color]");
	}

	/// <summary>And with nothing else on the run either, the text is all that is left.</summary>
	[Test]
	public async Task BBCode_ForeignLayerAloneWithNoEmitter_RendersBareText()
	{
		await Assert.That(Render(MarkupText.Wrap(new Tag("t"), "x"), MarkupFormat.BBCode)).IsEqualTo("x");
	}

	// ── A style source that only claims some formats ──────────────────────────────

	/// <summary><see cref="BoldTag"/> answers yes for Ansi, so it folds into the run's SGR.</summary>
	[Test]
	public async Task Ansi_FormatSpecificStyleSource_Folds()
	{
		await Assert.That(Render(MarkupText.Wrap(new BoldTag(), "x"), MarkupFormat.Ansi))
			.IsEqualTo($"{Esc}[1mx{Esc}[0m");
	}

	/// <summary>Folding means one sequence for the whole run, not one per layer.</summary>
	[Test]
	public async Task Ansi_FormatSpecificStyleSourceWithAnsiLayer_SharesOneSequence()
	{
		var text = MarkupText.Wrap(Red, MarkupText.Wrap(new BoldTag(), "x"));
		await Assert.That(Render(text, MarkupFormat.Ansi)).IsEqualTo($"{Esc}[1;31mx{Esc}[0m");
	}

	/// <summary>
	/// In HTML the same layer answers no, so it is delegated: its <c>&lt;b&gt;</c> appears and no
	/// span is opened for it.
	/// </summary>
	[Test]
	public async Task Html_FormatSpecificStyleSource_IsDelegatedToItsOwnEmitter()
	{
		await Assert.That(Render(MarkupText.Wrap(new BoldTag(), "x"), MarkupFormat.Html)).IsEqualTo("<b>x</b>");
	}

	/// <summary>
	/// With an ANSI layer alongside it, the span carries only the colour — the bold is the
	/// delegated tag, not an <c>ms-bold</c> class.
	/// </summary>
	[Test]
	public async Task Html_FormatSpecificStyleSourceWithAnsiLayer_TagSitsInsideTheColourSpan()
	{
		var text = MarkupText.Wrap(Red, MarkupText.Wrap(new BoldTag(), "x"));
		await Assert.That(Render(text, MarkupFormat.Html))
			.IsEqualTo("<span style=\"color: #aa0000\"><b>x</b></span>");
	}
}
