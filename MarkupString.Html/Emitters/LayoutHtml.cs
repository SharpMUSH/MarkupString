using System.Collections.Immutable;
using System.Globalization;
using MarkupString.Layout;

namespace MarkupString.Html;

/// <summary>The HTML of each built-in block.</summary>
internal static class LayoutHtml
{
	private static KeyValuePair<Type, Action<Block, HtmlLayoutWriter>> Of<T>(BlockHtmlRenderer<T> renderer) where T : Block =>
		new(typeof(T), (block, html) => renderer((T)block, html));

	public static readonly KeyValuePair<Type, Action<Block, HtmlLayoutWriter>>[] BuiltIns =
	[
		Of<TextBlock>(Text),
		Of<Stack>((stack, html) =>
		{
			html.Write("<div class=\"ms-stack\">");
			foreach (var child in stack.Children.IsDefault ? [] : stack.Children) html.Block(child);
			html.Write("</div>");
		}),
		Of<Rule>((rule, html) => Rule(rule, "ms-rule", rule.Border, html)),
		Of<Frame>(Frame),
		Of<Flex>(Flex),
		Of<Sized>((sized, html) => html.Block(sized.Content)),
		Of<Figure>(Figure),
		Of<Fields>(Fields),
		Of<Tree>((tree, html) =>
		{
			html.Write("<ul class=\"ms-tree ms-guide-");
			html.Write(Css((tree.Guide ?? html.Context.Theme.Guide ?? TreeGuide.Line).Name));
			html.Write("\">");
			TreeItems(tree.Items, html);
			html.Write("</ul>");
		}),
		Of<Gauge>(Gauge),
		Of<Bullets>(Bullets),
		Of<Grid>(Grid),
		Of<Table>(Table),
		Of<Aligned>((aligned, html) =>
		{
			html.Write("<div class=\"ms-aligned\" style=\"text-align:");
			html.Write(aligned.Alignment switch { Alignment.Center => "center", Alignment.Right => "right", _ => "left" });
			html.Write("\">");
			html.Block(aligned.Content, html.Context with { TextAlignment = aligned.Alignment });
			html.Write("</div>");
		}),
		Of<Shaded>(Shaded),
		Of<Colored>(Colored),
		Of<Themed>(Themed),
	];

	private static void Text(TextBlock text, HtmlLayoutWriter html)
	{
		html.Write("<div class=\"ms-text\"");
		if (text.Alignment is { } alignment and not (Alignment.Left or Alignment.Full or Alignment.Paragraph))
		{
			html.Write(" style=\"text-align:");
			html.Write(alignment == Alignment.Center ? "center" : "right");
			html.Write("\"");
		}
		html.Write(">");
		html.Text(text.Content);
		html.Write("</div>");
	}

	private static void Frame(Frame frame, HtmlLayoutWriter html)
	{
		html.Write("<fieldset class=\"ms-box ms-border-");
		html.Write(Css(BorderOf(frame.Border, html).Name));
		html.Write("\">");
		if (frame.Title is { Length: > 0 } title)
		{
			html.Write("<legend class=\"ms-box-title\"");
			Align(frame.TitleAlignment, html);
			html.Write(">");
			html.Text(title);
			html.Write("</legend>");
		}
		foreach (var child in frame.Body is Stack { Children.IsDefault: false } body ? body.Children : [frame.Body])
		{
			if (child is Rule divider) Rule(divider, "ms-divider", divider.Border ?? frame.Border, html);
			else html.Block(child);
		}
		html.Write("</fieldset>");
	}

	private static BorderStyle BorderOf(BorderStyle? border, HtmlLayoutWriter html) => border ?? html.Context.Theme.Border ?? BorderStyle.Single;

	private static void Rule(Rule rule, string kind, BorderStyle? border, HtmlLayoutWriter html)
	{
		html.Write("<div class=\"");
		html.Write(kind);
		html.Write(" ms-border-");
		html.Write(Css(BorderOf(border, html).Name));
		html.Write("\" role=\"separator\"");
		Align(rule.TitleAlignment, html);
		html.Write(">");
		if (rule.Title is { Length: > 0 } title)
		{
			html.Write("<span class=\"ms-rule-title\">");
			html.Text(title);
			html.Write("</span>");
		}
		html.Write("</div>");
	}

	private static void Flex(Flex flex, HtmlLayoutWriter html)
	{
		html.Write("<div class=\"ms-flex");
		if (flex.Separator is not null) html.Write(" ms-divided");
		if (flex.Vertical) html.Write(" ms-vertical");
		html.Write("\" style=\"column-gap:");
		html.Write(Number(flex.Separator is { } separator ? separator.DisplayWidth : Math.Max(0, flex.Gap)));
		html.Write("ch");
		if (flex.Justify != FlexJustify.Start)
		{
			html.Write(";justify-content:");
			html.Write(flex.Justify switch
			{
				FlexJustify.End => "flex-end",
				FlexJustify.Center => "center",
				_ => "space-between",
			});
		}
		if (flex.Align != FlexAlign.Start)
		{
			html.Write(";align-items:");
			html.Write(flex.Align == FlexAlign.Center ? "center" : "flex-end");
		}
		html.Write("\">");

		foreach (var block in flex.Items.IsDefault ? [] : flex.Items)
		{
			// Each item asks for its terminal width and grows to fill its row, so items that do not fit
			// side by side wrap onto rows of their own. Spare width is left alone only when the layout
			// asked for it to be placed somewhere.
			var item = block as Sized ?? new Sized(block);
			var grow = flex.Justify == FlexJustify.Start || item.Basis.Kind == BlockSizeKind.Auto
				? Math.Max(1, item.Grow)
				: item.Grow;
			html.Write("<div class=\"ms-item\" style=\"flex:");
			html.Write(Number(grow));
			html.Write(" 1 ");
			html.Write(item.Basis.Kind switch
			{
				BlockSizeKind.Cells => Number(item.Basis.Value) + "ch",
				BlockSizeKind.Percent => Number(item.Basis.Value) + "%",
				_ => item.Min > 1 ? Number(item.Min) + "ch" : "0",
			});
			html.Write("\">");
			html.Block(item.Content);
			html.Write("</div>");
		}
		html.Write("</div>");
	}

	private static void Figure(Figure figure, HtmlLayoutWriter html)
	{
		html.Write("<div class=\"ms-figure ms-float-");
		html.Write(figure.Float.ToString().ToLowerInvariant());
		html.Write("\">");

		var image = figure.Image;
		if (html.AllowsImage(image.Source))
		{
			html.Write("<img class=\"ms-figure-image\" src=\"");
			html.Attribute(image.Source);
			html.Write("\" alt=\"");
			html.Attribute(image.Description ?? string.Empty);
			html.Write("\"");
			if (image.Width is { } width)
			{
				html.Write(" width=\"");
				html.Write(Number(width));
				html.Write("\"");
			}
			if (image.Height is { } height)
			{
				html.Write(" height=\"");
				html.Write(Number(height));
				html.Write("\"");
			}
			html.Write(">");
		}
		else if (figure.Art.Length > 0)
		{
			html.Write("<pre class=\"ms-figure-art\" role=\"img\" aria-label=\"");
			html.Attribute(image.Description ?? string.Empty);
			html.Write("\">");
			html.Text(figure.Art);
			html.Write("</pre>");
		}
		else if (image.Description is { Length: > 0 } description)
		{
			html.Write("<div class=\"ms-text\">[");
			html.Encode(description);
			html.Write("]</div>");
		}

		if (figure.Beside is { } beside) html.Block(beside);
		html.Write("</div>");
	}

	private static void Fields(Fields fields, HtmlLayoutWriter html)
	{
		var all = fields.Items.IsDefault ? [] : fields.Items;
		var separator = fields.Separator ?? html.Context.Theme.FieldSeparator ?? LayoutTheme.Defaults.FieldSeparator!;
		if (fields.Columns > 1 && all.Length > 1)
		{
			// The same dealing as the terminal's, as a row that wraps: a column per item.
			var per = (all.Length + Math.Min(fields.Columns, all.Length) - 1) / Math.Min(fields.Columns, all.Length);
			html.Write("<div class=\"ms-flex\" style=\"column-gap:");
			html.Write(Number(Math.Max(0, fields.Gap)));
			html.Write("ch\">");
			foreach (var chunk in all.Chunk(per))
			{
				html.Write("<div class=\"ms-item\" style=\"flex:1 1 ");
				html.Write(Number(chunk.Max(field => field.Label.DisplayWidth) + separator.DisplayWidth + 10));
				html.Write("ch\">");
				FieldList(chunk, fields, separator, html);
				html.Write("</div>");
			}
			html.Write("</div>");
			return;
		}
		FieldList(all.AsSpan(), fields, separator, html);
	}

	/// <summary>
	/// The fields as a definition list. The separator is kept after each label, trimmed, unless a
	/// leader draws a dotted line to the value instead.
	/// </summary>
	private static void FieldList(ReadOnlySpan<Field> items, Fields fields, MarkupText separator, HtmlLayoutWriter html)
	{
		var leader = fields.Leader is { Length: > 0 };
		html.Write("<dl class=\"ms-fields");
		if (fields.LabelAlignment == Alignment.Right) html.Write(" ms-label-right");
		if (leader) html.Write(" ms-leader");
		if (fields.Striped) html.Write(" ms-striped");
		html.Write("\">");
		var head = separator.Trim(TrimType.TrimEnd);
		foreach (var field in items)
		{
			html.Write("<div class=\"ms-field\"><dt>");
			if (field.Label.Length > 0)
			{
				html.Text(field.Label);
				if (!leader) html.Text(head);
			}
			html.Write("</dt><dd>");
			html.Block(field.Value);
			html.Write("</dd></div>");
		}
		html.Write("</dl>");
	}

	private static void TreeItems(ImmutableArray<TreeItem> items, HtmlLayoutWriter html)
	{
		if (items.IsDefault) return;
		foreach (var item in items)
		{
			html.Write("<li>");
			html.Block(item.Content);
			if (!item.Children.IsDefaultOrEmpty)
			{
				html.Write("<ul>");
				TreeItems(item.Children, html);
				html.Write("</ul>");
			}
			html.Write("</li>");
		}
	}

	private static void Gauge(Gauge gauge, HtmlLayoutWriter html)
	{
		var maximum = gauge.Maximum > 0 ? gauge.Maximum : 0;
		var value = double.IsFinite(gauge.Value) ? Math.Clamp(gauge.Value, 0, maximum) : 0;
		html.Write("<div class=\"ms-gauge\">");
		if (gauge.Label is { Length: > 0 } label)
		{
			html.Write("<span class=\"ms-gauge-label\">");
			html.Text(label);
			html.Write("</span>");
		}
		if (gauge.Gradient is { IsEmpty: false } gradient)
		{
			GaugeBar(gauge, gradient, maximum, value, html);
		}
		else
		{
			html.Write("<meter min=\"0\" max=\"");
			html.Write(Decimal(maximum));
			html.Write("\" value=\"");
			html.Write(Decimal(value));
			html.Write("\"");
			BarWidth(gauge, html);
			html.Write("></meter>");
		}
		var figures = gauge.Show switch
		{
			GaugeShow.Value => $"{Decimal(gauge.Value)}/{Decimal(gauge.Maximum)}",
			GaugeShow.Percent => Decimal(Math.Round(gauge.Maximum > 0 ? gauge.Value / gauge.Maximum * 100 : 0)) + "%",
			_ => null,
		};
		if (figures is not null)
		{
			html.Write("<span class=\"ms-gauge-value\">");
			html.Encode(figures);
			html.Write("</span>");
		}
		html.Write("</div>");
	}

	private static void BarWidth(Gauge gauge, HtmlLayoutWriter html)
	{
		if (gauge.BarWidth <= 0) return;
		html.Write(" style=\"flex:0 1 ");
		html.Write(Number(gauge.BarWidth));
		html.Write("ch\"");
	}

	/// <summary>
	/// A shaded gauge: a bar with a filled part, since a <c>&lt;meter&gt;</c> cannot be given a gradient
	/// from an inline style. Shaded by cell, the gradient spans the whole bar and the filled part shows
	/// as much of it as the value reaches; shaded by value, the filled part is one colour.
	/// </summary>
	private static void GaugeBar(Gauge gauge, ColorGradient gradient, double maximum, double value, HtmlLayoutWriter html)
	{
		var ratio = maximum > 0 ? value / maximum : 0;
		html.Write("<div class=\"ms-gauge-bar\" role=\"meter\" aria-valuemin=\"0\" aria-valuemax=\"");
		html.Write(Decimal(maximum));
		html.Write("\" aria-valuenow=\"");
		html.Write(Decimal(value));
		html.Write("\"");
		BarWidth(gauge, html);
		html.Write("><div class=\"ms-gauge-fill\" style=\"width:");
		html.Write(Decimal(Math.Round(ratio * 100, 2)));
		html.Write("%;");
		if (gauge.Shade == GaugeShade.Value)
		{
			html.Write("background-color:");
			html.Write(gradient.At(ratio).ToHex());
		}
		else if (ratio > 0)
		{
			html.Write("background-image:");
			html.Write(gradient.ToCss(9));
			html.Write(";background-image:");
			html.Write(gradient.ToCss());
			html.Write(";background-size:");
			html.Write(Decimal(Math.Round(100 / ratio, 2)));
			html.Write("% 100%");
		}
		html.Write("\"></div></div>");
	}

	private static void Bullets(Bullets bullets, HtmlLayoutWriter html)
	{
		var ordered = bullets.Ordered;
		html.Write(ordered ? "<ol" : "<ul");
		html.Write(" class=\"ms-bullets ms-bullet-");
		html.Write(bullets.Style.ToString().ToLowerInvariant());
		html.Write("\"");
		if (ordered)
		{
			html.Write(" type=\"");
			html.Write(bullets.Style switch { BulletStyle.Alpha => "a", BulletStyle.Roman => "i", _ => "1" });
			html.Write("\"");
			if (bullets.Start != 1)
			{
				html.Write(" start=\"");
				html.Write(Number(bullets.Start));
				html.Write("\"");
			}
		}
		html.Write(">");
		foreach (var item in bullets.Items.IsDefault ? [] : bullets.Items)
		{
			html.Write("<li>");
			if (bullets.Style == BulletStyle.Custom && bullets.Marker is { Length: > 0 } marker)
			{
				// A marker of the game's own is text, not CSS, so it keeps its colour and needs no escaping into a stylesheet.
				html.Write("<span class=\"ms-marker\" aria-hidden=\"true\">");
				html.Text(marker);
				html.Write("</span>");
			}
			html.Block(item);
			html.Write("</li>");
		}
		html.Write(ordered ? "</ol>" : "</ul>");
	}

	private static void Grid(Grid grid, HtmlLayoutWriter html)
	{
		html.Write("<ul class=\"ms-grid");
		html.Write(grid.Across ? "\" style=\"grid-template-columns:repeat(auto-fill,minmax(" : " ms-down\" style=\"columns:");
		var cell = grid.Items.IsDefaultOrEmpty ? 1 : Math.Max(1, grid.Items.Max(item => item.DisplayWidth));
		html.Write(Number(cell));
		html.Write(grid.Across ? "ch,1fr));column-gap:" : "ch;column-gap:");
		html.Write(Number(Math.Max(0, grid.Gap)));
		html.Write("ch\">");
		foreach (var item in grid.Items.IsDefault ? [] : grid.Items)
		{
			html.Write("<li>");
			html.Text(item);
			html.Write("</li>");
		}
		html.Write("</ul>");
	}

	private static void Table(Table table, HtmlLayoutWriter html)
	{
		var columns = table.Columns.IsDefault ? [] : table.Columns;
		html.Write("<div class=\"ms-table-wrap\"><table class=\"ms-table");
		if (table.Striped) html.Write(" ms-striped");
		html.Write("\"><thead><tr>");
		foreach (var column in columns)
		{
			html.Write("<th scope=\"col\"");
			Cell(column, html);
			html.Write(">");
			html.Text(column.Header);
			html.Write("</th>");
		}
		html.Write("</tr></thead><tbody>");
		foreach (var row in table.Rows.IsDefault ? [] : table.Rows)
		{
			html.Write("<tr>");
			for (var c = 0; c < columns.Length; c++)
			{
				html.Write("<td");
				Cell(columns[c], html);
				html.Write(">");
				if (!row.IsDefault && c < row.Length) html.Block(row[c]);
				html.Write("</td>");
			}
			html.Write("</tr>");
		}
		html.Write("</tbody></table></div>");
	}

	/// <summary>A cell's alignment and, for a column that may be left out on a narrow page, its class.</summary>
	private static void Cell(TableColumn column, HtmlLayoutWriter html)
	{
		if (column.Priority >= 2)
		{
			html.Write(" class=\"ms-p");
			html.Write(Number(Math.Min(column.Priority, 3)));
			html.Write("\"");
		}
		if (column.Alignment is Alignment.Right or Alignment.Center)
		{
			html.Write(" style=\"text-align:");
			html.Write(column.Alignment == Alignment.Right ? "right" : "center");
			html.Write("\"");
		}
	}

	/// <summary>
	/// A shaded block: its text clipped to the gradient, and its borders and rules drawn in it. A
	/// browser has no way to run a gradient along characters or words, so those run across.
	/// </summary>
	private static void Shaded(Shaded shaded, HtmlLayoutWriter html)
	{
		if (shaded.Gradient.IsEmpty)
		{
			html.Block(shaded.Content);
			return;
		}
		var direction = shaded.Flow switch
		{
			GradientFlow.Down => "to bottom",
			GradientFlow.Diagonal => "to bottom right",
			_ => "to right",
		};
		html.Write("<div class=\"ms-shaded\" style=\"--ms-shade:");
		html.Write(shaded.Gradient.ToCss(9, direction));
		html.Write(";background-image:var(--ms-shade);background-image:");
		html.Write(shaded.Gradient.ToCss(direction));
		html.Write("\">");
		html.Block(shaded.Content);
		html.Write("</div>");
	}

	/// <summary>The CSS custom property each part's colour is written to, and read from by <see cref="LayoutCss"/>.</summary>
	private static readonly (string Property, Func<LayoutTheme, IMarkup?> Part)[] ThemeProperties =
	[
		("--ms-border", theme => theme.BorderColor),
		("--ms-title", theme => theme.TitleColor),
		("--ms-heading", theme => theme.HeadingColor),
		("--ms-label", theme => theme.LabelColor),
		("--ms-separator", theme => theme.SeparatorColor),
		("--ms-bullet", theme => theme.BulletColor),
		("--ms-guide", theme => theme.GuideColor),
		("--ms-header-rule", theme => theme.HeaderRuleColor),
		("--ms-gauge", theme => theme.GaugeFilledColor),
		("--ms-gauge-empty", theme => theme.GaugeEmptyColor),
		("--ms-stripe", theme => theme.StripeColor),
	];

	/// <summary>
	/// A themed block. The colours it sets are written as custom properties, so a page can set the same
	/// ones to theme every layout; a fallback theme writes them as <c>-default</c>, under any the page sets.
	/// </summary>
	private static void Themed(Themed themed, HtmlLayoutWriter html)
	{
		var colors = ThemeProperties
			.Select(entry => (entry.Property, Color: (entry.Part(themed.Theme) as IColorMarkup) switch
			{
				// The stripe is a background; every other part is a colour of text or lines.
				{ Background: { } back } when entry.Property == "--ms-stripe" => back.ToHex(),
				{ Foreground: { } fore } when entry.Property != "--ms-stripe" => fore.ToHex(),
				_ => null,
			}))
			.Where(entry => entry.Color is not null)
			.ToArray();
		if (colors.Length == 0)
		{
			html.Block(themed.Content, html.Context with { Theme = themed.Within(html.Context.Theme) });
			return;
		}

		html.Write("<div class=\"ms-themed\" style=\"");
		foreach (var (property, color) in colors)
		{
			html.Write(property);
			if (themed.Fallback) html.Write("-default");
			html.Write(":");
			html.Write(color!);
			html.Write(";");
		}
		html.Write("\">");
		html.Block(themed.Content, html.Context with { Theme = themed.Within(html.Context.Theme) });
		html.Write("</div>");
	}

	/// <summary>A coloured block: a colour as the block's own, any other layer opened round it as it renders.</summary>
	private static void Colored(Colored colored, HtmlLayoutWriter html)
	{
		if (colored.Markup is IColorMarkup { Foreground: { } color })
		{
			html.Write("<div class=\"ms-colored\" style=\"color:");
			html.Write(color.ToHex());
			html.Write("\">");
			html.Block(colored.Content);
			html.Write("</div>");
			return;
		}

		// The layer's own opening and closing, taken from how it renders round a stand-in character.
		const string standIn = "";
		var buffer = new System.Buffers.ArrayBufferWriter<char>();
		MarkupTextRenderer.RenderFragment(MarkupText.Wrap(colored.Markup, standIn), MarkupFormat.Html, html.Registry, buffer);
		var sample = new string(buffer.WrittenSpan);
		var at = sample.IndexOf(standIn, StringComparison.Ordinal);
		if (at < 0)
		{
			html.Block(colored.Content);
			return;
		}
		html.Write(sample[..at]);
		html.Block(colored.Content);
		html.Write(sample[(at + standIn.Length)..]);
	}

	private static void Align(Alignment alignment, HtmlLayoutWriter html)
	{
		if (alignment is Alignment.Left or Alignment.Right)
		{
			html.Write(" data-align=\"");
			html.Write(alignment == Alignment.Left ? "left" : "right");
			html.Write("\"");
		}
	}

	/// <summary>A preset name as a class suffix: letters, digits and dashes only.</summary>
	private static string Css(string name)
	{
		foreach (var c in name)
			if (!(char.IsAsciiLetterOrDigit(c) || c == '-')) return "custom";
		return name.Length == 0 ? "custom" : name.ToLowerInvariant();
	}

	private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

	private static string Decimal(double value) =>
		double.IsFinite(value) ? value.ToString("0.##", CultureInfo.InvariantCulture) : "0";
}
