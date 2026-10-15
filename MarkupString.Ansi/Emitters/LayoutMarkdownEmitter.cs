using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using MarkupString.Layout;
namespace MarkupString.Ansi;

/// <summary>
/// Draws a <see cref="LayoutMarkup"/> as GitHub-flavoured Markdown, from its tree rather than its lines: a
/// table as a pipe table, a list as a list, a rule as <c>---</c>, a box as its title in bold over what it
/// holds. Markdown is not laid out to a width, so nothing is padded, wrapped or set side by side.
/// </summary>
/// <remarks>
/// <para>What Markdown has no structure for is written in reading order — the items of a row one after the
/// other, labelled values as a list of <c>**Label:** value</c>, a gauge as its figures — and a block this does
/// not know is written as the lines it draws, in a fenced code block, so nothing is ever left out.</para>
/// <para>A table cell holds one line of inline text in Markdown, so a block in a cell is written in reading
/// order with its lines joined by <c>&lt;br&gt;</c>, and a table in a cell as its rows of labelled values.</para>
/// </remarks>
internal sealed class LayoutMarkdownEmitter : IBlockEmitter
{
	public static LayoutMarkdownEmitter Instance { get; } = new();

	private LayoutMarkdownEmitter()
	{
	}

	public Type MarkupType => typeof(LayoutMarkup);

	public MarkupFormat Format => MarkupFormat.Markdown;

	public bool TryEmit(IBlockMarkup markup, MarkupText region, MarkupRegistry registry, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(registry);
		ArgumentNullException.ThrowIfNull(output);
		if (markup is not LayoutMarkup layout) return false;

		var chunks = new MarkdownLayoutWriter(registry, layout.Width, cell: false, LayoutTheme.Default).Blocks(layout.Root);
		for (var i = 0; i < chunks.Count; i++)
		{
			if (i > 0) output.Write("\n\n");
			output.Write(chunks[i].Text);
		}
		return true;
	}
}

/// <summary>Writes a layout's blocks as Markdown, each as one or more chunks that a blank line separates.</summary>
/// <param name="registry">The registry the render runs against, for the text in the blocks.</param>
/// <param name="width">The width the layout was laid out at, which a block written as its lines is drawn at.</param>
/// <param name="cell">Whether this is the inside of a table cell, which holds no block structure.</param>
/// <param name="theme">The theme the blocks are drawn under, for the parts that are text: a field's separator.</param>
internal sealed class MarkdownLayoutWriter(MarkupRegistry registry, int width, bool cell, LayoutTheme theme)
{
	/// <summary>One block of Markdown; <paramref name="Paragraph"/> when it is running text a label can go in front of.</summary>
	internal readonly record struct Chunk(string Text, bool Paragraph)
	{
		/// <summary>For a list, the character after each number, or the bullet; otherwise none.</summary>
		public char Marker { get; init; }

		/// <summary>Whether it is a list that may follow a line of a paragraph with no blank line between.</summary>
		public bool Interrupts { get; init; }
	}

	private LayoutTheme _theme = theme;

	/// <summary>The chunks <paramref name="block"/> is written as.</summary>
	public List<Chunk> Blocks(Block block)
	{
		var chunks = new List<Chunk>();
		Block(block, chunks);

		// In a cell a list is only text, and two of them are not read as one.
		if (!cell) Separate(chunks);
		return chunks;
	}

	/// <summary>
	/// Two lists one after the other with the same marker are one list to a reader, blank line or not, so
	/// every second one is written with the other marker: <c>*</c> for <c>-</c>, <c>)</c> for <c>.</c>.
	/// </summary>
	private static void Separate(List<Chunk> chunks)
	{
		for (var i = 1; i < chunks.Count; i++)
		{
			if (chunks[i].Marker == '\0' || chunks[i].Marker != chunks[i - 1].Marker) continue;
			var (from, to) = chunks[i].Marker switch { '-' => ('-', '*'), '*' => ('*', '-'), '.' => ('.', ')'), _ => (')', '.') };
			var lines = chunks[i].Text.Split('\n');

			// Only an item's first line starts at the margin: the rest of an item is indented, or blank.
			for (var l = 0; l < lines.Length; l++)
			{
				if (lines[l].Length == 0 || lines[l][0] == ' ') continue;
				var at = lines[l].IndexOf(from);
				lines[l] = string.Concat(lines[l].AsSpan(0, at), to.ToString(), lines[l].AsSpan(at + 1));
			}
			chunks[i] = chunks[i] with { Text = string.Join('\n', lines), Marker = to };
		}
	}

	private void Block(Block block, List<Chunk> chunks)
	{
		switch (block)
		{
			case TextBlock text:
				Text(text.Content, chunks);
				break;
			case Stack stack:
				foreach (var child in stack.Children.IsDefault ? [] : stack.Children) Block(child, chunks);
				break;
			case Rule rule:
				Rule(rule, chunks);
				break;
			case Frame frame:
				Frame(frame, chunks);
				break;

			// A row has no width to share here: its items are read one after the other.
			case Flex flex:
				foreach (var item in flex.Items.IsDefault ? [] : flex.Items) Block(item, chunks);
				break;
			case Sized sized:
				Block(sized.Content, chunks);
				break;

			// Markdown has no alignment, gradient or colour; what they hold is written as it is.
			case Aligned aligned:
				Block(aligned.Content, chunks);
				break;
			case Shaded shaded:
				Block(shaded.Content, chunks);
				break;
			case Colored colored:
				Block(Painted(colored), chunks);
				break;
			case Themed themed:
				var before = _theme;
				_theme = themed.Within(_theme);
				try
				{
					Block(themed.Content, chunks);
				}
				finally
				{
					_theme = before;
				}
				break;

			case Figure figure:
				Figure(figure, chunks);
				break;
			case Fields fields:
				Fields(fields, chunks);
				break;
			case Tree tree:
				if (!tree.Items.IsDefaultOrEmpty) chunks.Add(List(tree.Items.Select(TreeEntry), start: null));
				break;
			case Bullets bullets:
				Bullets(bullets, chunks);
				break;
			case Grid grid:
				if (!grid.Items.IsDefaultOrEmpty) chunks.Add(List(grid.Items.Select(Texts), start: null));
				break;
			case Table table:
				Table(table, chunks);
				break;
			case Gauge gauge:
				Text(MarkupText.Join(MarkupText.NewLine, (LayoutContext.Default with { Linear = true, Theme = _theme }).Lines(gauge, width)), chunks);
				break;
			default:
				Fenced(string.Join('\n', (LayoutContext.Default with { Theme = _theme }).Lines(block, width).Select(line => line.ToPlainText())), chunks);
				break;
		}
	}

	/// <summary>
	/// Text as paragraphs, its line endings kept as hard breaks and a blank line between paragraphs; a layout
	/// standing on lines of its own inside it is written as its own blocks.
	/// </summary>
	private void Text(MarkupText content, List<Chunk> chunks)
	{
		var parts = BlockLayout.Blocks(content);
		if (parts is [TextBlock only])
		{
			Paragraphs(Inline(only.Content), chunks);
			return;
		}
		foreach (var part in parts) Block(part, chunks);
	}

	private List<Chunk> Texts(MarkupText content)
	{
		var chunks = new List<Chunk>();
		Text(content, chunks);
		return chunks;
	}

	/// <summary>
	/// Rendered Markdown as paragraphs: each line trimmed, since leading whitespace would make code of it and
	/// trailing whitespace a break, the lines of one paragraph joined by a hard break (a backslash at the end
	/// of the line), and blank lines between paragraphs. In a cell, the lines are only joined.
	/// </summary>
	private void Paragraphs(string markdown, List<Chunk> chunks)
	{
		var lines = new List<string>();
		foreach (var raw in markdown.Split('\n'))
		{
			var line = raw.Trim(' ', '\t');
			if (line.Length > 0)
			{
				lines.Add(line);
				continue;
			}
			Flush();
		}
		Flush();

		void Flush()
		{
			if (lines.Count == 0) return;
			chunks.Add(new Chunk(string.Join(cell ? "\n" : "\\\n", lines), Paragraph: true));
			lines.Clear();
		}
	}

	/// <summary><paramref name="text"/> rendered as inline Markdown.</summary>
	private string Inline(MarkupText text)
	{
		var buffer = new ArrayBufferWriter<char>(Math.Max(16, text.Length * 2));
		MarkupTextRenderer.RenderFragment(text, MarkupFormat.Markdown, registry, buffer);
		return new string(buffer.WrittenSpan);
	}

	private static readonly AnsiMarkup Bold = AnsiMarkup.Create(bold: true);

	/// <summary>
	/// <paramref name="text"/> in bold, on one line, or null when there is nothing to show. The bold is a layer
	/// over the text, so bold already in it is folded into it rather than written twice.
	/// </summary>
	private string? Strong(MarkupText? text)
	{
		if (text is null || text.ToPlainText().AsSpan().IndexOfAnyExcept(" \t\r\n") < 0) return null;
		// The bold is closed before each line ending and opened after it, so the lines join into one as they are.
		var inline = Inline(MarkupText.Wrap(Bold, text)).ReplaceLineEndings(" ").Trim(' ', '\t');
		return inline.Length == 0 ? null : inline;
	}

	/// <summary>
	/// The titles along an edge as they are read, left, middle, right, a comma between, or null for none —
	/// what <see cref="Layout.Rule.DrawLinear"/> reads out.
	/// </summary>
	private static MarkupText? Titles(MarkupText? title, Alignment alignment, ImmutableArray<EdgeTitle> more)
	{
		var titles = new List<EdgeTitle>();
		if (title is { Length: > 0 }) titles.Add(new EdgeTitle(title, alignment));
		foreach (var extra in more.IsDefault ? [] : more)
			if (extra is { Text.Length: > 0 }) titles.Add(extra);
		if (titles.Count == 0) return null;
		return MarkupText.Join(MarkupText.Plain(", "), titles
			.OrderBy(edge => edge.Side switch { Alignment.Left => 0, Alignment.Right => 2, _ => 1 })
			.Select(edge => edge.Text));
	}

	/// <summary>A rule as <c>---</c>, or, when it carries a title, as the title in bold: the title is what the line is there to set off.</summary>
	private void Rule(Rule rule, List<Chunk> chunks)
	{
		if (Strong(Titles(rule.Title, rule.TitleAlignment, rule.Titles)) is { } title) chunks.Add(new Chunk(title, Paragraph: true));
		else if (!cell) chunks.Add(new Chunk("---", Paragraph: false));
	}

	/// <summary>A box as its title in bold, what it holds, and the titles on its bottom edge as a line of text.</summary>
	private void Frame(Frame frame, List<Chunk> chunks)
	{
		if (Strong(Titles(frame.Title, frame.TitleAlignment, frame.Titles)) is { } title) chunks.Add(new Chunk(title, Paragraph: true));
		Block(frame.Body, chunks);
		if (Titles(null, Alignment.Center, frame.BottomTitles) is { } bottom) Text(bottom, chunks);
	}

	/// <summary>The block's text under the block's layer, which comes out as whatever Markdown makes of it: bold stays, colour goes.</summary>
	private static Block Painted(Colored colored) =>
		colored.Markup is IPointMarkup or IBlockMarkup
			? colored.Content
			: colored.Content.MapText(text => MarkupText.Wrap(colored.Markup, text));

	/// <summary>
	/// A picture as an image, when it has an address a page may load; otherwise its text art in a code block,
	/// or its description. What is beside it follows.
	/// </summary>
	private void Figure(Figure figure, List<Chunk> chunks)
	{
		var image = figure.Image;
		if (MarkdownSyntax.ImageSource(image.Source) is { } source)
		{
			var buffer = new ArrayBufferWriter<char>();
			MarkdownSyntax.WriteImage(image.Description, source, buffer);
			chunks.Add(new Chunk(new string(buffer.WrittenSpan), Paragraph: true));
		}
		else if (!cell && figure.Art.ToPlainText().AsSpan().ContainsAnyExcept(" \t\r\n"))
		{
			Fenced(figure.Art.ToPlainText(), chunks);
		}
		else if ((image.Description is { Length: > 0 } description ? description : image.Source) is { Length: > 0 } shown)
		{
			Paragraphs(Inline(MarkupText.Plain($"[{shown}]")), chunks);
		}

		if (figure.Beside is { } beside) Block(beside, chunks);
	}

	/// <summary>
	/// Labelled values as a list of <c>**Label:** value</c>. GitHub-flavoured Markdown has no definition list,
	/// and a list keeps a value of several blocks under its label.
	/// </summary>
	private void Fields(Fields fields, List<Chunk> chunks)
	{
		if (fields.Items.IsDefaultOrEmpty) return;
		var separator = fields.Separator ?? _theme.FieldSeparator ?? LayoutTheme.Defaults.FieldSeparator!;
		var head = separator.Trim(TrimType.TrimEnd);
		var leader = fields.Leader is { Length: > 0 };
		chunks.Add(List(fields.Items.Select(field =>
			Labelled(field.Label.Length > 0 ? Strong(leader ? field.Label : MarkupText.Concat(field.Label, head)) : null, Blocks(field.Value))), start: null));
	}

	/// <summary><paramref name="value"/> after <paramref name="label"/>: on the same line when it starts with running text.</summary>
	private static List<Chunk> Labelled(string? label, List<Chunk> value)
	{
		if (label is null) return value;
		if (value.Count > 0 && value[0].Paragraph) value[0] = new Chunk(label + " " + value[0].Text, Paragraph: true);
		else value.Insert(0, new Chunk(label, Paragraph: true));
		return value;
	}

	private List<Chunk> TreeEntry(TreeItem item)
	{
		var content = Blocks(item.Content);
		if (!item.Children.IsDefaultOrEmpty) content.Add(List(item.Children.Select(TreeEntry), start: null));
		return content;
	}

	/// <summary>
	/// A list: <c>-</c> for bullets, dashes, stars and a marker of the game's own; numbers for a numbered list,
	/// and for letters and numerals, which Markdown cannot number with. A list with no markers is its items.
	/// </summary>
	private void Bullets(Bullets bullets, List<Chunk> chunks)
	{
		if (bullets.Items.IsDefaultOrEmpty) return;
		if (bullets.Style == BulletStyle.None)
		{
			foreach (var item in bullets.Items) Block(item, chunks);
			return;
		}

		// Markdown numbers a list from zero up, in at most nine digits.
		var start = Math.Clamp(bullets.Start, 0, 999_999_999 - bullets.Items.Length);
		chunks.Add(List(bullets.Items.Select(Blocks), bullets.Ordered ? start : null));
	}

	/// <summary>
	/// List items, each its chunks: the first line after the marker, the rest indented under it so they stay
	/// in the item, and a blank line between the chunks of one item, except before a list that can follow a
	/// line of text directly. Numbered from <paramref name="start"/>, or bulleted when it is null.
	/// </summary>
	private static Chunk List(IEnumerable<List<Chunk>> items, int? start)
	{
		var list = new StringBuilder();
		var number = start ?? 0;
		foreach (var content in items)
		{
			if (list.Length > 0) list.Append('\n');
			var marker = start is null ? "-" : (number++).ToString(CultureInfo.InvariantCulture) + ".";
			list.Append(marker);
			var indent = new string(' ', marker.Length + 1);
			for (var c = 0; c < content.Count; c++)
			{
				var lines = content[c].Text.Split('\n');
				for (var l = 0; l < lines.Length; l++)
				{
					if (c == 0 && l == 0)
					{
						list.Append(' ').Append(lines[0]);
						continue;
					}
					list.Append('\n');
					if (l == 0 && !(content[c].Interrupts && content[c - 1].Paragraph)) list.Append('\n');
					if (lines[l].Length > 0) list.Append(indent).Append(lines[l]);
				}
			}
		}

		// A numbered list can break into a paragraph only when it counts from one.
		return new Chunk(list.ToString(), Paragraph: false) { Marker = start is null ? '-' : '.', Interrupts = start is null or 1 };
	}

	/// <summary>
	/// A pipe table: the headings, a delimiter row that carries each column's alignment, and a row per row,
	/// short rows filled with empty cells. In a cell, where a table cannot go, each row as labelled values.
	/// </summary>
	private void Table(Table table, List<Chunk> chunks)
	{
		if (table.Columns.IsDefaultOrEmpty) return;
		var columns = table.Columns;
		var rows = table.Rows.IsDefault ? [] : table.Rows;
		if (cell)
		{
			foreach (var row in rows)
				Block(new Fields([.. columns.Select((column, c) => new Field(column.Header, !row.IsDefault && c < row.Length ? row[c] : new TextBlock(MarkupText.Empty)))]), chunks);
			return;
		}

		var markdown = new StringBuilder();
		Row(columns.Select(column => Cell(new TextBlock(column.Header))), markdown);
		markdown.Append('\n');
		Row(columns.Select(column => column.Alignment switch
		{
			Alignment.Right => "---:",
			Alignment.Center => ":---:",
			_ => "---",
		}), markdown);
		foreach (var row in rows)
		{
			markdown.Append('\n');
			Row(Enumerable.Range(0, columns.Length).Select(c => !row.IsDefault && c < row.Length ? Cell(row[c]) : string.Empty), markdown);
		}
		chunks.Add(new Chunk(markdown.ToString(), Paragraph: false));
	}

	private static void Row(IEnumerable<string> cells, StringBuilder markdown)
	{
		markdown.Append('|');
		foreach (var text in cells)
		{
			markdown.Append(' ');
			if (text.Length > 0) markdown.Append(text).Append(' ');
			markdown.Append('|');
		}
	}

	/// <summary>A block as one table cell: its chunks in reading order, their lines joined by <c>&lt;br&gt;</c>.</summary>
	private string Cell(Block block)
	{
		var chunks = new MarkdownLayoutWriter(registry, width, cell: true, _theme).Blocks(block);
		return string.Join("<br>", chunks.Select(chunk => chunk.Text.Replace("\n", "<br>", StringComparison.Ordinal)));
	}

	/// <summary>
	/// Text that keeps its own spacing, in a fenced code block whose fence is longer than any run of backticks
	/// in it; in a cell, where a code block cannot go, its lines as text.
	/// </summary>
	private void Fenced(string plain, List<Chunk> chunks)
	{
		var stripped = new ArrayBufferWriter<char>(Math.Max(16, plain.Length));
		MarkupTextRenderer.EncodeText(plain.ReplaceLineEndings("\n").TrimEnd('\n'), TextEncoding.StripControls, stripped);
		var content = new string(stripped.WrittenSpan);
		if (content.AsSpan().IndexOfAnyExcept(" \t\n") < 0) return;
		if (cell)
		{
			Paragraphs(Inline(MarkupText.Plain(content)), chunks);
			return;
		}

		var longest = 0;
		var run = 0;
		foreach (var c in content)
		{
			run = c == '`' ? run + 1 : 0;
			longest = Math.Max(longest, run);
		}
		var fence = new string('`', Math.Max(3, longest + 1));
		chunks.Add(new Chunk(fence + "\n" + content + "\n" + fence, Paragraph: false));
	}
}
