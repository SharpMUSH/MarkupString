using System.Text.RegularExpressions;
using MarkupString.Ansi;

namespace MarkupString.Tests.Ansi;

/// <summary>
/// The stylesheet and the emitter that writes the class names have to agree. Nothing links them at
/// compile time, so an attribute that gains a class without a rule renders as unstyled text, and a
/// rule left behind after a class is renamed is dead weight nobody notices.
/// </summary>
public class AnsiCssTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi();

	/// <summary>Every attribute the HTML emitter can turn into a class, and nothing else.</summary>
	private static readonly AnsiStyle[] EveryAttribute =
	[
		AnsiStyle.None with { Bold = true },
		AnsiStyle.None with { Faint = true },
		AnsiStyle.None with { Italic = true },
		AnsiStyle.None with { Underlined = true },
		AnsiStyle.None with { StrikeThrough = true },
		AnsiStyle.None with { Overlined = true },
		AnsiStyle.None with { Blink = true },
		AnsiStyle.None with { Inverted = true },
	];

	[Test]
	public async Task EveryClassTheEmitterWrites_HasARule()
	{
		foreach (var style in EveryAttribute)
		{
			var html = MarkupText.Wrap(new AnsiMarkup(style), "x").Render(MarkupFormat.Html, Registry);

			foreach (var name in ClassNames(html))
			{
				await Assert.That(AnsiCss.Fixed).Contains($".{name} {{");
			}
		}
	}

	[Test]
	public async Task CommandLink_ClassHasARule()
	{
		var link = AnsiMarkup.Create(linkText: "go north", linkUrl: "north", linkKind: LinkKind.Command);
		var html = MarkupText.Wrap(link, "north").Render(MarkupFormat.Html, Registry);

		await Assert.That(ClassNames(html)).Contains("ms-cmd-link");
		await Assert.That(AnsiCss.Fixed).Contains(".ms-cmd-link {");
	}

	[Test]
	public async Task EveryRule_IsForAClassTheEmitterCanWrite()
	{
		var emitted = EveryAttribute
			.SelectMany(style => ClassNames(MarkupText.Wrap(new AnsiMarkup(style), "x").Render(MarkupFormat.Html, Registry)))
			.Append("ms-cmd-link")
			.ToHashSet(StringComparer.Ordinal);

		foreach (Match rule in Regex.Matches(AnsiCss.Fixed, @"^\.(?<name>[\w-]+) \{", RegexOptions.Multiline))
		{
			await Assert.That(emitted).Contains(rule.Groups["name"].Value);
		}
	}

	/// <summary>The keyframes .ms-blink animates against have to be in the same sheet as the rule.</summary>
	[Test]
	public async Task BlinkKeyframes_AreDeclared()
		=> await Assert.That(AnsiCss.Fixed).Contains("@keyframes ms-blink {");

	private static IEnumerable<string> ClassNames(string html) =>
		Regex.Matches(html, "class=\"(?<names>[^\"]*)\"")
			.SelectMany(m => m.Groups["names"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
