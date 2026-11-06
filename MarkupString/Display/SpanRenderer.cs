using System.Globalization;
using System.Text;
namespace MarkupString.Display;

/// <summary>
/// A stretch of text with one style, or a point (a bell, a sound) when <see cref="Point"/> is set and
/// <see cref="Text"/> is empty.
/// </summary>
/// <param name="Text">The text.</param>
/// <param name="Style">How it looks.</param>
public readonly record struct DisplaySpan(string Text, DisplayStyle Style)
{
	/// <summary>The point that stands here, or null for text.</summary>
	public IPointMarkup? Point { get; init; }
}

/// <summary>
/// Turns text into spans, each one style, for an output that draws runs of text: a web page, a rich text
/// control. Adjacent text with the same style is one span; a picture row is one span of its blank cells.
/// </summary>
public static class SpanRenderer
{
	/// <summary>The spans of <paramref name="text"/>, in order.</summary>
	public static IReadOnlyList<DisplaySpan> Render(MarkupText text, DisplayRegistry registry, DisplayOptions options)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(registry);
		ArgumentNullException.ThrowIfNull(options);

		var spans = new List<DisplaySpan>();
		var styles = new Dictionary<MarkupSet, DisplayStyle>(ReferenceEqualityComparer.Instance);
		var fold = options.Capabilities.Unicode == UnicodeLevel.Ascii;
		var source = text.Text;
		var at = 0;

		foreach (var run in text.Runs)
		{
			if (run.Start > at) Add(spans, source[at..run.Start], DisplayStyle.Plain, fold);

			var point = FindPoint(run.Markups);
			if (point is not null)
			{
				if (Shows(point, options.Capabilities)) spans.Add(new DisplaySpan(string.Empty, DisplayStyle.Plain) { Point = point });
			}
			else
			{
				if (!styles.TryGetValue(run.Markups, out var style))
				{
					styles[run.Markups] = style = registry.Resolve(run.Markups, options);
				}

				Add(spans, source.Substring(run.Start, run.Length), style, fold);
			}

			at = run.End;
		}

		if (at < source.Length) Add(spans, source[at..], DisplayStyle.Plain, fold);
		return spans;
	}

	/// <summary>
	/// The inline CSS for <paramref name="style"/>: colours, weight, slant and decorations. Empty for plain text.
	/// Blink is left to <see cref="CssClasses"/>, since CSS has no blink of its own.
	/// </summary>
	public static string Css(DisplayStyle style)
	{
		var css = new StringBuilder();
		if (style.Foreground is { } foreground && !style.Has(TextAttributes.Conceal)) Append(css, "color", Hex(foreground));
		if (style.Background is { } background) Append(css, "background-color", Hex(background));
		if (style.Has(TextAttributes.Bold)) Append(css, "font-weight", "bold");
		if (style.Has(TextAttributes.Faint)) Append(css, "opacity", "0.6");
		if (style.Has(TextAttributes.Italic)) Append(css, "font-style", "italic");

		var lines = new List<string>(3);
		if (style.Has(TextAttributes.Underline)) lines.Add("underline");
		if (style.Has(TextAttributes.Overline)) lines.Add("overline");
		if (style.Has(TextAttributes.Strikethrough)) lines.Add("line-through");
		if (lines.Count > 0) Append(css, "text-decoration", string.Join(' ', lines));

		if (style.Has(TextAttributes.Conceal)) Append(css, "color", "transparent");
		return css.ToString();
	}

	/// <summary>
	/// The class names a page styles itself: <c>ms-blink</c>, <c>ms-conceal</c>, <c>ms-link</c>, <c>ms-command</c>,
	/// <c>ms-prompt</c>, <c>ms-menu</c>, <c>ms-picture</c>, and <c>ms-invert</c> for inverse text with no colours of its own to
	/// swap (an output that shows attributes but no colour). Empty for plain text.
	/// </summary>
	public static string CssClasses(DisplayStyle style)
	{
		var classes = new List<string>(3);
		if (style.Has(TextAttributes.Blink)) classes.Add("ms-blink");
		if (style.Has(TextAttributes.Conceal)) classes.Add("ms-conceal");
		if (style.Has(TextAttributes.Inverse) && style.Foreground is null && style.Background is null) classes.Add("ms-invert");
		if (style.Interaction is { } interaction)
		{
			classes.Add(interaction.Kind switch
			{
				InteractionKind.Url => "ms-link",
				InteractionKind.Command => "ms-command",
				_ => "ms-prompt",
			});
			if (interaction.Choices is { Count: > 1 }) classes.Add("ms-menu");
		}

		if (style.Picture is not null) classes.Add("ms-picture");
		return string.Join(' ', classes);
	}

	/// <summary><paramref name="color"/> as <c>#rrggbb</c>.</summary>
	public static string Hex(RgbColor color) => string.Create(CultureInfo.InvariantCulture, $"#{color.R:x2}{color.G:x2}{color.B:x2}");

	internal static IPointMarkup? FindPoint(MarkupSet markups)
	{
		foreach (var markup in markups)
		{
			if (markup is IPointMarkup point) return point;
		}

		return null;
	}

	internal static bool Shows(IPointMarkup point, OutputCapabilities capabilities) => point switch
	{
		SoundMarkup or SoundStopMarkup => capabilities.Sound,
		_ => true,
	};

	private static void Add(List<DisplaySpan> spans, string text, DisplayStyle style, bool fold)
	{
		if (text.Length == 0) return;
		if (fold) text = AsciiFold.Default.Fold(text);
		if (spans.Count > 0 && spans[^1] is { Point: null } last && last.Style == style)
		{
			spans[^1] = last with { Text = last.Text + text };
			return;
		}

		spans.Add(new DisplaySpan(text, style));
	}

	private static void Append(StringBuilder css, string property, string value)
	{
		if (css.Length > 0) css.Append(';');
		css.Append(property).Append(':').Append(value);
	}
}
