using System.Text.Json;
using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Layout;

namespace MarkupString.Tests.Layout;

/// <summary>A theme comes from a player or a game, so whatever it says must read, fail, or draw without breaking.</summary>
public class MalformedThemeTests
{
	private static MarkupText P(string text) => MarkupText.Plain(text);

	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi().WithHtml();

	public static IEnumerable<string> Pieces() =>
		["​", "́", "\u001b[31m", "\n", "\t", "\r\n", "界界", "😀", new string('x', 400), " ", "\u0000", "\ud800"];

	[Test]
	[MethodDataSource(nameof(Pieces))]
	public async Task AnyPiece_DrawsOrIsRefused(string piece)
	{
		var text = JsonSerializer.Serialize(piece);
		var spec = "{\"preset\":\"fantasy\",\"look\":{\"title\":[" + text + "," + text + "],\"bullet\":" + text
			+ ",\"gauge\":[" + string.Join(",", Enumerable.Repeat(text, 4)) + "],\"separator\":" + text + ",\"rule\":" + text + "}}";
		if (!ThemePalette.TryParse(spec, out var palette, out _)) return;

		var theme = palette!.ToLayoutTheme();
		var sheet = new Stack([
			new Bullets([P("one")]),
			new Gauge(3, 6) { Label = P("HP") },
			new Fields([new Field(P("Name"), P("Ann").ToBlock())]) { Striped = true },
			new Tree([new TreeItem(P("root"), [new TreeItem(P("leaf"))])]),
			new Table([new TableColumn(P("A")), new TableColumn(P("B"))], [[P("a").ToBlock(), P("b").ToBlock()]]) { Striped = true },
			new Rule(P("Rule")),
		]).Bordered(P("Sheet"));

		foreach (var width in new[] { 1, 4, 12, 40, 120 })
			foreach (var context in new[] { LayoutContext.Default, new LayoutContext { AsciiOnly = true }, new LayoutContext { Linear = true } })
			{
				var laid = BlockLayout.Build(sheet.Themed(theme), width, context: context);
				_ = laid.Render(MarkupFormat.Ansi, Registry);
				_ = laid.Render(MarkupFormat.Html, Registry);
				_ = BlockLayout.Relayout(laid, width, context with { Theme = theme });
			}
		await Assert.That(theme).IsNotNull();
	}

	public static IEnumerable<string> Specs() =>
	[
		"", " ", "{", "}", "[]", "null", "42", "\"\"", "{}", "{\"preset\":{}}", "{\"preset\":{\"preset\":{\"preset\":\"nord\"}}}",
		new string('[', 5000) + new string(']', 5000), new string('{', 2000),
		"{\"seed\":\"#zzzzzz\"}", "{\"seed\":\"#fff\",\"contrast\":1e400}", "{\"seed\":\"#ffffff\",\"contrast\":-0}",
		"{\"seed\":\"#000000\",\"harmony\":\"1\"}", "{\"mode\":\"2\"}", "{\"mode\":null}", "{\"colors\":{\"0\":\"#ffffff\"}}",
		"{\"colors\":{\"accent\":99}}", "{\"colors\":{\"accent\":{\"slot\":1e10}}}", "{\"colors\":{\"accent\":{}}}",
		"{\"base16\":[1,2,3]}", "{\"look\":{\"border\":\"\"}}", "{\"look\":{\"title\":[\"a\"]}}", "{\"look\":{\"gauge\":[\"a\",\"b\",\"c\",null]}}",
		"{\"look\":{\"bullet\":5}}", "{\"look\":[]}", "{\"name\":null}", "{\"preset\":\"nord\",\"seed\":\"#ffffff\"}",
		"{\"preset\":\"nord\",\"preset\":\"nope\"}", "\"\ud800\"", "nord extra", "\u0000",
	];

	[Test]
	[MethodDataSource(nameof(Specs))]
	public async Task AnySpec_ReadsOrSaysWhy(string spec)
	{
		var read = ThemePalette.TryParse(spec, out var palette, out var error);
		if (read) _ = palette!.ToLayoutTheme();
		await Assert.That(read || error is { Length: > 0 }).IsTrue();
	}
}
