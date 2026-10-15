using System.Text.Json;
using MarkupString.Ansi;
using MarkupString.Html;
namespace MarkupString.Tests;

/// <summary>
/// Data markup says what text is, not how it looks: no format draws it, taking the visual markup off keeps
/// it, and it is carried and stored like any other layer.
/// </summary>
public class DataMarkupTests
{
	private sealed record Separator : IDataMarkup;

	private sealed class SeparatorCodec : IMarkupCodec
	{
		public string Kind => "test-separator";
		public Type MarkupType => typeof(Separator);
		public void Write(Utf8JsonWriter writer, IMarkup markup) { }
		public IMarkup Read(JsonElement element) => new Separator();
	}

	/// <summary>Draws a separator as <c>{|}</c>, to show that the renderer never calls an emitter for data.</summary>
	private sealed class SeparatorEmitter : IMarkupEmitter
	{
		public Type MarkupType => typeof(Separator);
		public MarkupFormat Format => MarkupFormat.Ansi;
		public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, System.Buffers.IBufferWriter<char> output)
		{
			output.Write("{");
			output.Write(body);
			output.Write("}");
		}
	}

	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml().With(new SeparatorCodec());

	private static readonly AnsiMarkup Red = AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false));

	private static MarkupText Marked(string text) => MarkupText.Wrap(new Separator(), text);

	private static bool IsData(MarkupText text, int index) =>
		text.Runs.Any(run => index >= run.Start && index < run.End && run.Markups.Any(markup => markup is IDataMarkup));

	[Test]
	public async Task DataIsWrittenAsItsText()
	{
		var text = MarkupText.Concat([MarkupText.Plain("a"), Marked("|"), MarkupText.Plain("b")]);

		await Assert.That(text.Render(MarkupFormat.Ansi, Registry)).IsEqualTo("a|b");
		await Assert.That(text.Render(MarkupFormat.Html, Registry)).IsEqualTo("a|b");
		await Assert.That(text.Render(MarkupFormat.Plain, Registry)).IsEqualTo("a|b");
	}

	[Test]
	public async Task DataUnderColourIsWrittenInTheColour()
	{
		var coloured = MarkupText.Concat([MarkupText.Plain("a"), MarkupText.Wrap(Red, Marked("|")), MarkupText.Plain("b")]);
		var colourOnly = MarkupText.Concat([MarkupText.Plain("a"), MarkupText.Wrap(Red, "|"), MarkupText.Plain("b")]);

		await Assert.That(coloured.Render(MarkupFormat.Ansi, Registry)).IsEqualTo(colourOnly.Render(MarkupFormat.Ansi, Registry));
		await Assert.That(coloured.Render(MarkupFormat.Html, Registry)).IsEqualTo(colourOnly.Render(MarkupFormat.Html, Registry));
	}

	[Test]
	public async Task AnEmitterForDataIsNeverCalled()
	{
		var registry = Registry.With(new SeparatorEmitter());

		await Assert.That(MarkupText.Concat([MarkupText.Plain("a"), Marked("|")]).Render(MarkupFormat.Ansi, registry)).IsEqualTo("a|");
	}

	[Test]
	public async Task WithoutVisualMarkup_KeepsTheDataAndDropsTheColour()
	{
		var text = MarkupText.Concat([MarkupText.Wrap(Red, "a"), MarkupText.Wrap(Red, Marked("|")), MarkupText.Plain("b"), Marked("|")]);

		var stripped = text.WithoutVisualMarkup();

		await Assert.That(stripped.Text).IsEqualTo("a|b|");
		await Assert.That(stripped.Runs.Length).IsEqualTo(2);
		await Assert.That(stripped.Runs.All(run => run.Markups.Count == 1 && run.Markups[0] is Separator)).IsTrue();
		await Assert.That(IsData(stripped, 1)).IsTrue();
		await Assert.That(IsData(stripped, 3)).IsTrue();
		await Assert.That(IsData(stripped, 0)).IsFalse();
	}

	[Test]
	public async Task WithoutVisualMarkup_JoinsDataThatColourHadSplit()
	{
		var text = MarkupText.Concat([MarkupText.Wrap(Red, Marked("|")), Marked("|")]);

		var stripped = text.WithoutVisualMarkup();

		await Assert.That(stripped.Runs.Length).IsEqualTo(1);
		await Assert.That(stripped.Runs[0].Length).IsEqualTo(2);
	}

	[Test]
	public async Task WithoutVisualMarkup_WithNoData_IsThePlainText()
	{
		var text = MarkupText.Concat([MarkupText.Wrap(Red, "a"), MarkupText.Bell(), MarkupText.Plain("b")]);

		var stripped = text.WithoutVisualMarkup();

		await Assert.That(stripped.Text).IsEqualTo("ab");
		await Assert.That(stripped.Runs.IsDefaultOrEmpty).IsTrue();
	}

	[Test]
	public async Task WithoutVisualMarkup_DropsPointsWithTheirCarriers()
	{
		var text = MarkupText.Concat([Marked("|"), MarkupText.Bell(), MarkupText.Plain("b")]);

		var stripped = text.WithoutVisualMarkup();

		await Assert.That(stripped.Text).IsEqualTo("|b");
		await Assert.That(IsData(stripped, 0)).IsTrue();
	}

	[Test]
	public async Task WithoutDataMarkup_KeepsTheColourAndDropsTheData()
	{
		var text = MarkupText.Concat([MarkupText.Wrap(Red, "a"), MarkupText.Wrap(Red, Marked("|")), MarkupText.Plain("b"), Marked("|")]);

		var unmarked = text.WithoutDataMarkup();

		await Assert.That(unmarked.Text).IsEqualTo("a|b|");
		await Assert.That(IsData(unmarked, 1)).IsFalse();
		await Assert.That(IsData(unmarked, 3)).IsFalse();
		await Assert.That(unmarked.Render(MarkupFormat.Ansi, Registry))
			.IsEqualTo(MarkupText.Concat([MarkupText.Wrap(Red, "a|"), MarkupText.Plain("b|")]).Render(MarkupFormat.Ansi, Registry));
	}

	[Test]
	public async Task WithoutDataMarkup_KeepsPoints()
	{
		var text = MarkupText.Concat([Marked("|"), MarkupText.Bell(), MarkupText.Plain("b")]);

		var unmarked = text.WithoutDataMarkup();

		await Assert.That(unmarked.Text).IsEqualTo(text.Text);
		await Assert.That(unmarked.ToPlainText()).IsEqualTo("|b");
		await Assert.That(IsData(unmarked, 0)).IsFalse();
	}

	[Test]
	public async Task WithoutDataMarkup_WithNoData_IsTheSameText()
	{
		var text = MarkupText.Concat([MarkupText.Wrap(Red, "a"), MarkupText.Plain("b")]);

		await Assert.That(ReferenceEquals(text.WithoutDataMarkup(), text)).IsTrue();
	}

	[Test]
	public async Task DataIsIgnoredByEquality()
		=> await Assert.That(MarkupText.Concat([MarkupText.Plain("a"), Marked("|")])).IsEqualTo(MarkupText.Plain("a|"));

	[Test]
	public async Task DataIsStoredAndReadBack()
	{
		var text = MarkupText.Concat([MarkupText.Plain("a"), Marked("|"), MarkupText.Plain("b")]);

		var back = MarkupTextSerializer.Deserialize(MarkupTextSerializer.Serialize(text, Registry), Registry);

		await Assert.That(IsData(back, 1)).IsTrue();
		await Assert.That(IsData(back, 0)).IsFalse();
	}
}
