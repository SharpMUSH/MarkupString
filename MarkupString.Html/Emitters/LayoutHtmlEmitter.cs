using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using MarkupString.Layout;

namespace MarkupString.Html;

/// <summary>
/// Draws a <see cref="LayoutMarkup"/> as page structure: a box as a <c>&lt;fieldset&gt;</c> with its
/// title as the legend, columns as a wrapping flex row, a figure as an image floated beside its text.
/// The classes it writes are styled by <see cref="LayoutCss.Fixed"/>.
/// </summary>
/// <remarks>
/// Widths become hints: an item asks for its terminal width in <c>ch</c> and the row wraps when the
/// page is narrower, so a 78-column box reads on a phone. Border characters are not drawn; the
/// border is CSS, and <see cref="BorderStyle.Name"/> picks its look.
/// </remarks>
internal sealed class LayoutHtmlEmitter(Func<string, bool>? allowImage = null) : IBlockEmitter
{
	public Type MarkupType => typeof(LayoutMarkup);

	public MarkupFormat Format => MarkupFormat.Html;

	public bool TryEmit(IBlockMarkup markup, MarkupText region, MarkupRegistry registry, IBufferWriter<char> output)
	{
		if (markup is not LayoutMarkup layout) return false;
		output.Write("<div class=\"ms-layout\" style=\"max-width:");
		output.Write(Number(layout.Width));
		output.Write("ch\">");
		Node(layout.Root, registry, output);
		output.Write("</div>");
		return true;
	}

	private void Node(LayoutNode node, MarkupRegistry registry, IBufferWriter<char> output)
	{
		switch (node)
		{
			case TextNode text:
				output.Write("<div class=\"ms-text\"");
				if (text.Alignment is not (Alignment.Left or Alignment.Full or Alignment.Paragraph))
				{
					output.Write(" style=\"text-align:");
					output.Write(text.Alignment == Alignment.Center ? "center" : "right");
					output.Write("\"");
				}
				output.Write(">");
				Text(text.Content, registry, output);
				output.Write("</div>");
				break;

			case StackNode stack:
				output.Write("<div class=\"ms-stack\">");
				foreach (var child in stack.Children) Node(child, registry, output);
				output.Write("</div>");
				break;

			case BoxNode box:
				output.Write("<fieldset class=\"ms-box ms-border-");
				output.Write(Css(box.Border.Name));
				output.Write("\">");
				if (box.Title is { Length: > 0 } title)
				{
					output.Write("<legend class=\"ms-box-title\"");
					Align(box.TitleAlignment, output);
					output.Write(">");
					Text(title, registry, output);
					output.Write("</legend>");
				}
				foreach (var child in box.Body is StackNode body ? body.Children : [box.Body])
				{
					if (child is RuleNode divider) Rule(divider, "ms-divider", registry, output);
					else Node(child, registry, output);
				}
				output.Write("</fieldset>");
				break;

			case RuleNode rule:
				Rule(rule, "ms-rule", registry, output);
				break;

			case FlexNode flex:
				Flex(flex, registry, output);
				break;

			case FigureNode figure:
				Figure(figure, registry, output);
				break;

			case FieldsNode fields:
				Fields(fields, registry, output);
				break;

			case GaugeNode gauge:
				Gauge(gauge, registry, output);
				break;

			case BulletsNode bullets:
				Bullets(bullets, registry, output);
				break;

			case GridNode grid:
				output.Write("<ul class=\"ms-grid");
				output.Write(grid.Across ? "\" style=\"grid-template-columns:repeat(auto-fill,minmax(" : " ms-down\" style=\"columns:");
				var cell = grid.Items.IsDefaultOrEmpty ? 1 : Math.Max(1, grid.Items.Max(item => item.DisplayWidth));
				output.Write(Number(cell));
				output.Write(grid.Across ? "ch,1fr));column-gap:" : "ch;column-gap:");
				output.Write(Number(Math.Max(0, grid.Gap)));
				output.Write("ch\">");
				if (!grid.Items.IsDefault)
				{
					foreach (var item in grid.Items)
					{
						output.Write("<li>");
						Text(item, registry, output);
						output.Write("</li>");
					}
				}
				output.Write("</ul>");
				break;

			case TableNode table:
				Table(table, registry, output);
				break;

			case TreeNode tree:
				output.Write("<ul class=\"ms-tree ms-guide-");
				output.Write(Css(tree.Guide.Name));
				output.Write("\">");
				TreeItems(tree.Items, registry, output);
				output.Write("</ul>");
				break;
		}
	}

	private void Fields(FieldsNode fields, MarkupRegistry registry, IBufferWriter<char> output)
	{
		var all = fields.Fields.IsDefault ? [] : fields.Fields;
		var options = fields.Options;
		if (options.Columns > 1 && all.Length > 1)
		{
			// The same dealing as the terminal's, as a row that wraps: a column per item.
			var per = (all.Length + Math.Min(options.Columns, all.Length) - 1) / Math.Min(options.Columns, all.Length);
			output.Write("<div class=\"ms-flex\" style=\"column-gap:");
			output.Write(Number(Math.Max(0, options.Gap)));
			output.Write("ch\">");
			foreach (var chunk in all.Chunk(per))
			{
				output.Write("<div class=\"ms-item\" style=\"flex:1 1 ");
				output.Write(Number(chunk.Max(field => field.Label.DisplayWidth) + options.Separator.DisplayWidth + 10));
				output.Write("ch\">");
				FieldList(chunk, options, registry, output);
				output.Write("</div>");
			}
			output.Write("</div>");
			return;
		}
		FieldList(all.AsSpan(), options, registry, output);
	}

	/// <summary>
	/// The fields as a definition list. The separator is kept after each label, trimmed, unless a
	/// leader draws a dotted line to the value instead.
	/// </summary>
	private void FieldList(ReadOnlySpan<Field> fields, FieldsOptions options, MarkupRegistry registry, IBufferWriter<char> output)
	{
		var leader = options.Leader is { Length: > 0 };
		output.Write("<dl class=\"ms-fields");
		if (options.LabelAlignment == Alignment.Right) output.Write(" ms-label-right");
		if (leader) output.Write(" ms-leader");
		output.Write("\">");
		var separator = options.Separator.Trim(TrimType.TrimEnd);
		foreach (var field in fields)
		{
			output.Write("<div class=\"ms-field\"><dt>");
			if (field.Label.Length > 0)
			{
				Text(field.Label, registry, output);
				if (!leader) Text(separator, registry, output);
			}
			output.Write("</dt><dd>");
			Node(field.Value, registry, output);
			output.Write("</dd></div>");
		}
		output.Write("</dl>");
	}

	private void Gauge(GaugeNode gauge, MarkupRegistry registry, IBufferWriter<char> output)
	{
		var maximum = gauge.Maximum > 0 ? gauge.Maximum : 0;
		var value = double.IsFinite(gauge.Value) ? Math.Clamp(gauge.Value, 0, maximum) : 0;
		output.Write("<div class=\"ms-gauge\">");
		if (gauge.Label is { Length: > 0 } label)
		{
			output.Write("<span class=\"ms-gauge-label\">");
			Text(label, registry, output);
			output.Write("</span>");
		}
		output.Write("<meter min=\"0\" max=\"");
		output.Write(Decimal(maximum));
		output.Write("\" value=\"");
		output.Write(Decimal(value));
		output.Write("\"");
		if (gauge.Options.BarWidth > 0)
		{
			output.Write(" style=\"flex:0 1 ");
			output.Write(Number(gauge.Options.BarWidth));
			output.Write("ch\"");
		}
		output.Write("></meter>");
		var figures = gauge.Options.Show switch
		{
			GaugeShow.Value => $"{Decimal(gauge.Value)}/{Decimal(gauge.Maximum)}",
			GaugeShow.Percent => Decimal(Math.Round(gauge.Maximum > 0 ? gauge.Value / gauge.Maximum * 100 : 0)) + "%",
			_ => null,
		};
		if (figures is not null)
		{
			output.Write("<span class=\"ms-gauge-value\">");
			MarkupTextRenderer.EncodeText(figures, TextEncoding.Html, output);
			output.Write("</span>");
		}
		output.Write("</div>");
	}

	private void Bullets(BulletsNode bullets, MarkupRegistry registry, IBufferWriter<char> output)
	{
		var settings = bullets.Options;
		var ordered = settings.Style is BulletStyle.Number or BulletStyle.Alpha or BulletStyle.Roman;
		output.Write(ordered ? "<ol" : "<ul");
		output.Write(" class=\"ms-bullets ms-bullet-");
		output.Write(settings.Style.ToString().ToLowerInvariant());
		output.Write("\"");
		if (ordered)
		{
			output.Write(" type=\"");
			output.Write(settings.Style switch { BulletStyle.Alpha => "a", BulletStyle.Roman => "i", _ => "1" });
			output.Write("\"");
			if (settings.Start != 1)
			{
				output.Write(" start=\"");
				output.Write(Number(settings.Start));
				output.Write("\"");
			}
		}
		output.Write(">");
		if (!bullets.Items.IsDefault)
		{
			foreach (var item in bullets.Items)
			{
				output.Write("<li>");
				if (settings.Style == BulletStyle.Custom && settings.Marker is { Length: > 0 } marker)
				{
					// A marker of the game's own is text, not CSS, so it keeps its colour and needs no escaping into a stylesheet.
					output.Write("<span class=\"ms-marker\" aria-hidden=\"true\">");
					Text(marker, registry, output);
					output.Write("</span>");
				}
				Node(item, registry, output);
				output.Write("</li>");
			}
		}
		output.Write(ordered ? "</ol>" : "</ul>");
	}

	private void Table(TableNode table, MarkupRegistry registry, IBufferWriter<char> output)
	{
		var columns = table.Columns.IsDefault ? [] : table.Columns;
		output.Write("<div class=\"ms-table-wrap\"><table class=\"ms-table\"><thead><tr>");
		foreach (var column in columns)
		{
			output.Write("<th scope=\"col\"");
			Cell(column, output);
			output.Write(">");
			Text(column.Header, registry, output);
			output.Write("</th>");
		}
		output.Write("</tr></thead><tbody>");
		if (!table.Rows.IsDefault)
		{
			foreach (var row in table.Rows)
			{
				output.Write("<tr>");
				for (var c = 0; c < columns.Length; c++)
				{
					output.Write("<td");
					Cell(columns[c], output);
					output.Write(">");
					if (!row.IsDefault && c < row.Length) Node(row[c], registry, output);
					output.Write("</td>");
				}
				output.Write("</tr>");
			}
		}
		output.Write("</tbody></table></div>");
	}

	/// <summary>A cell's alignment and, for a column that may be left out on a narrow page, its class.</summary>
	private static void Cell(TableColumn column, IBufferWriter<char> output)
	{
		if (column.Priority >= 2)
		{
			output.Write(" class=\"ms-p");
			output.Write(Number(Math.Min(column.Priority, 3)));
			output.Write("\"");
		}
		if (column.Alignment is Alignment.Right or Alignment.Center)
		{
			output.Write(" style=\"text-align:");
			output.Write(column.Alignment == Alignment.Right ? "right" : "center");
			output.Write("\"");
		}
	}

	private static string Decimal(double value) =>
		double.IsFinite(value) ? value.ToString("0.##", CultureInfo.InvariantCulture) : "0";

	private void TreeItems(ImmutableArray<TreeItem> items, MarkupRegistry registry, IBufferWriter<char> output)
	{
		if (items.IsDefault) return;
		foreach (var item in items)
		{
			output.Write("<li>");
			Node(item.Content, registry, output);
			if (!item.Children.IsDefaultOrEmpty)
			{
				output.Write("<ul>");
				TreeItems(item.Children, registry, output);
				output.Write("</ul>");
			}
			output.Write("</li>");
		}
	}

	private static void Rule(RuleNode rule, string kind, MarkupRegistry registry, IBufferWriter<char> output)
	{
		output.Write("<div class=\"");
		output.Write(kind);
		output.Write(" ms-border-");
		output.Write(Css(rule.Border.Name));
		output.Write("\" role=\"separator\"");
		Align(rule.TitleAlignment, output);
		output.Write(">");
		if (rule.Title is { Length: > 0 } title)
		{
			output.Write("<span class=\"ms-rule-title\">");
			Text(title, registry, output);
			output.Write("</span>");
		}
		output.Write("</div>");
	}

	private void Flex(FlexNode flex, MarkupRegistry registry, IBufferWriter<char> output)
	{
		var options = flex.Options;
		output.Write("<div class=\"ms-flex");
		if (options.Separator is not null) output.Write(" ms-divided");
		if (options.Vertical) output.Write(" ms-vertical");
		output.Write("\" style=\"column-gap:");
		output.Write(Number(options.Separator is { } separator ? separator.DisplayWidth : Math.Max(0, options.Gap)));
		output.Write("ch");
		if (options.Justify != FlexJustify.Start)
		{
			output.Write(";justify-content:");
			output.Write(options.Justify switch
			{
				FlexJustify.End => "flex-end",
				FlexJustify.Center => "center",
				_ => "space-between",
			});
		}
		if (options.Align != FlexAlign.Start)
		{
			output.Write(";align-items:");
			output.Write(options.Align == FlexAlign.Center ? "center" : "flex-end");
		}
		output.Write("\">");

		foreach (var item in flex.Items)
		{
			// Each item asks for its terminal width and grows to fill its row, so items that do not fit
			// side by side wrap onto rows of their own. Spare width is left alone only when the layout
			// asked for it to be placed somewhere.
			var grow = options.Justify == FlexJustify.Start || item.Basis.Kind == BlockSizeKind.Auto
				? Math.Max(1, item.Grow)
				: item.Grow;
			output.Write("<div class=\"ms-item\" style=\"flex:");
			output.Write(Number(grow));
			output.Write(" 1 ");
			output.Write(item.Basis.Kind switch
			{
				BlockSizeKind.Cells => Number(item.Basis.Value) + "ch",
				BlockSizeKind.Percent => Number(item.Basis.Value) + "%",
				_ => item.Min > 1 ? Number(item.Min) + "ch" : "0",
			});
			output.Write("\">");
			Node(item.Content, registry, output);
			output.Write("</div>");
		}
		output.Write("</div>");
	}

	private void Figure(FigureNode figure, MarkupRegistry registry, IBufferWriter<char> output)
	{
		output.Write("<div class=\"ms-figure ms-float-");
		output.Write(figure.Float.ToString().ToLowerInvariant());
		output.Write("\">");

		var image = figure.Image;
		if (IsShowable(image.Source))
		{
			output.Write("<img class=\"ms-figure-image\" src=\"");
			Attribute(image.Source, output);
			output.Write("\" alt=\"");
			Attribute(image.Description ?? string.Empty, output);
			output.Write("\"");
			if (image.Width is { } width)
			{
				output.Write(" width=\"");
				output.Write(Number(width));
				output.Write("\"");
			}
			if (image.Height is { } height)
			{
				output.Write(" height=\"");
				output.Write(Number(height));
				output.Write("\"");
			}
			output.Write(">");
		}
		else if (figure.Art.Length > 0)
		{
			output.Write("<pre class=\"ms-figure-art\" role=\"img\" aria-label=\"");
			Attribute(image.Description ?? string.Empty, output);
			output.Write("\">");
			Text(figure.Art, registry, output);
			output.Write("</pre>");
		}
		else if (image.Description is { Length: > 0 } description)
		{
			output.Write("<div class=\"ms-text\">[");
			MarkupTextRenderer.EncodeText(description, TextEncoding.Html, output);
			output.Write("]</div>");
		}

		if (figure.Beside is { } beside) Node(beside, registry, output);
		output.Write("</div>");
	}

	/// <summary>Whether the page may load <paramref name="source"/>: an http(s) or relative address the host allows.</summary>
	private bool IsShowable(string source)
	{
		if (string.IsNullOrWhiteSpace(source) || source.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) return false;

		// On Unix a rooted path parses as an absolute file: address and "//host" as one on another
		// host, so both are decided before the parser sees them.
		if (source.StartsWith("//", StringComparison.Ordinal) || source.StartsWith('\\')) return false;
		if (!source.StartsWith('/'))
		{
			if (!Uri.TryCreate(source, UriKind.RelativeOrAbsolute, out var uri)) return false;
			if (uri.IsAbsoluteUri && uri.Scheme is not ("http" or "https")) return false;
		}
		return allowImage?.Invoke(source) ?? true;
	}

	private static void Text(MarkupText text, MarkupRegistry registry, IBufferWriter<char> output) =>
		MarkupTextRenderer.RenderFragment(text, MarkupFormat.Html, registry, output);

	private static void Align(Alignment alignment, IBufferWriter<char> output)
	{
		if (alignment is Alignment.Left or Alignment.Right)
		{
			output.Write(" data-align=\"");
			output.Write(alignment == Alignment.Left ? "left" : "right");
			output.Write("\"");
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

	private static void Attribute(string value, IBufferWriter<char> output)
	{
		foreach (var c in value)
		{
			switch (c)
			{
				case '&': output.Write("&amp;"); break;
				case '"': output.Write("&quot;"); break;
				case '\'': output.Write("&#39;"); break;
				case '<': output.Write("&lt;"); break;
				case '>': output.Write("&gt;"); break;
				default:
					if (!char.IsControl(c)) output.Write([c]);
					break;
			}
		}
	}
}
