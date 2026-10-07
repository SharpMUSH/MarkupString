namespace MarkupString.Layout;

/// <summary>
/// One piece of a block layout — a box, a rule, columns, a table, text — that knows how to draw
/// itself as lines of text at whatever width it is given. A tree of blocks is laid out by
/// <see cref="BlockLayout"/>, and drawn as page structure by a format that can (HTML) from the same tree.
/// </summary>
/// <remarks>
/// <para>A block has no width of its own: widths come from whoever draws it, so the same tree fits a
/// 78-column terminal, a 40-column phone client and a browser window.</para>
/// <para>Optional properties are <see langword="null"/> until set. An unset look — a border, a guide, a
/// gauge's pieces — comes from the <see cref="LayoutTheme"/> in the <see cref="LayoutContext"/> it is
/// drawn in, so a whole sheet changes look in one place (<see cref="BlockExtensions.Themed"/>), and a
/// block that sets one keeps it.</para>
/// <para>To add a block of your own, derive from this, override <see cref="Draw"/> (and
/// <see cref="DrawLinear"/> where a screen reader should hear something else), then register a
/// <see cref="BlockCodec"/> so it survives the serializer and, if you like, an HTML renderer.</para>
/// </remarks>
public abstract record Block
{
	/// <summary>Creates a block.</summary>
	protected Block()
	{
	}

	/// <summary>Draws the block at <paramref name="width"/> display cells, adding a line at a time to <paramref name="lines"/>.</summary>
	/// <param name="context">The look and the reader: draw children through <see cref="LayoutContext.Draw"/>.</param>
	/// <param name="width">The width in display cells, at least one.</param>
	/// <param name="lines">Where the lines go.</param>
	public abstract void Draw(LayoutContext context, int width, IList<MarkupText> lines);

	/// <summary>
	/// Draws the block for a screen reader (<see cref="LayoutContext.Linear"/>): the content alone in
	/// reading order, with no borders, fill or columns. By default, as <see cref="Draw"/> does.
	/// </summary>
	public virtual void DrawLinear(LayoutContext context, int width, IList<MarkupText> lines) => Draw(context, width, lines);

	/// <summary>
	/// How wide the block wants to be given at most <paramref name="width"/> cells: the fewest cells it
	/// can be drawn in without giving way, and the widest of its lines drawn at that width. By default
	/// it is drawn, its text to the left, and its lines measured.
	/// </summary>
	public virtual BlockMeasure Measure(LayoutContext context, int width)
	{
		ArgumentNullException.ThrowIfNull(context);
		var natural = 0;
		foreach (var line in (context with { TextAlignment = Alignment.Left }).Lines(this, width))
			natural = Math.Max(natural, line.Trim(TrimType.TrimEnd).DisplayWidth);
		return new BlockMeasure(1, natural);
	}

	/// <summary>Text as a block.</summary>
	public static implicit operator Block(MarkupText text) => new TextBlock(text);
}

/// <summary>How wide a <see cref="Block"/> wants to be.</summary>
/// <param name="Min">The fewest cells it can be drawn in without giving way.</param>
/// <param name="Natural">The cells it takes when nothing holds it in.</param>
public readonly record struct BlockMeasure(int Min, int Natural);

/// <summary>
/// What a block is drawn under: the reader's needs and the look it inherits. Passed down the tree;
/// a modifier changes it for the blocks inside it.
/// </summary>
public sealed record LayoutContext
{
	/// <summary>Every part shown in whatever characters the blocks name, in the default look.</summary>
	public static LayoutContext Default { get; } = new();

	/// <summary>Plain ASCII only, for a client that cannot show anything else: box drawing becomes its nearest ASCII (<see cref="BorderStyle.ToAscii"/>).</summary>
	public bool AsciiOnly { get; init; }

	/// <summary>
	/// The content alone in reading order, for a screen reader: no borders, fill or columns, each
	/// title on a line of its own, each item after the one before it, a picture as its description.
	/// </summary>
	public bool Linear { get; init; }

	/// <summary>The look a block takes where it sets none of its own.</summary>
	public LayoutTheme Theme { get; init; } = LayoutTheme.Default;

	/// <summary>Where text with no alignment of its own sits: a table column's, or one set with <see cref="BlockExtensions.Aligned"/>.</summary>
	public Alignment TextAlignment { get; init; } = Alignment.Left;

	/// <summary>Draws <paramref name="block"/> at <paramref name="width"/> cells, for this reader.</summary>
	public void Draw(Block block, int width, IList<MarkupText> lines)
	{
		ArgumentNullException.ThrowIfNull(block);
		ArgumentNullException.ThrowIfNull(lines);
		width = Math.Max(1, width);
		if (Linear) block.DrawLinear(this, width, lines);
		else block.Draw(this, width, lines);
	}

	/// <summary>The lines <paramref name="block"/> draws at <paramref name="width"/> cells.</summary>
	public List<MarkupText> Lines(Block block, int width)
	{
		var lines = new List<MarkupText>();
		Draw(block, width, lines);
		return lines;
	}

	/// <summary>
	/// <paramref name="piece"/> for this reader: as it is, or for an ASCII-only one its box-drawing
	/// characters translated, or <paramref name="fallback"/> when it holds anything else.
	/// </summary>
	public MarkupText Glyph(MarkupText piece, string fallback)
	{
		ArgumentNullException.ThrowIfNull(piece);
		return !AsciiOnly ? piece : BorderStyle.AsciiText(piece) ?? MarkupText.Plain(fallback);
	}

	/// <summary><paramref name="style"/>, or the theme's when it is null, as this reader sees it.</summary>
	public BorderStyle Border(BorderStyle? style)
	{
		style ??= Theme.Border ?? BorderStyle.Single;
		return AsciiOnly ? style.ToAscii() : style;
	}

	/// <summary><paramref name="guide"/>, or the theme's when it is null, as this reader sees it.</summary>
	public TreeGuide Guide(TreeGuide? guide)
	{
		guide ??= Theme.Guide ?? TreeGuide.Line;
		return AsciiOnly ? guide.ToAscii() : guide;
	}

	/// <summary>
	/// <paramref name="text"/> in the theme's colour for <paramref name="part"/>, or as it is when the theme
	/// sets none. Colour the text sets itself wins.
	/// </summary>
	/// <param name="part">The part's colour: <c>theme =&gt; theme.BorderColor</c>.</param>
	/// <param name="text">The piece.</param>
	public MarkupText Paint(Func<LayoutTheme, IMarkup?> part, MarkupText text)
	{
		ArgumentNullException.ThrowIfNull(part);
		ArgumentNullException.ThrowIfNull(text);
		return part(Theme) is { } markup && text.Length > 0 ? MarkupText.Wrap(markup, text) : text;
	}

	/// <summary>
	/// The lines from <paramref name="first"/> on, a row of a striped table or list, laid on the theme's
	/// stripe colour when <paramref name="row"/> is odd (the second, the fourth, ...). Colour a cell sets
	/// itself wins.
	/// </summary>
	internal void Stripe(IList<MarkupText> lines, int first, int row)
	{
		if (row % 2 == 0 || Theme.StripeColor is not { } markup) return;
		for (var i = first; i < lines.Count; i++)
			if (lines[i].Length > 0) lines[i] = MarkupText.Wrap(markup, lines[i]);
	}

	/// <summary><paramref name="style"/> with every piece in the theme's border colour.</summary>
	internal BorderStyle Paint(BorderStyle style)
	{
		if (Theme.BorderColor is not { } markup) return style;
		MarkupText Piece(MarkupText piece) => piece.Length > 0 ? MarkupText.Wrap(markup, piece) : piece;
		return style with
		{
			TopLeft = Piece(style.TopLeft),
			Top = Piece(style.Top),
			TopRight = Piece(style.TopRight),
			Left = Piece(style.Left),
			Right = Piece(style.Right),
			BottomLeft = Piece(style.BottomLeft),
			Bottom = Piece(style.Bottom),
			BottomRight = Piece(style.BottomRight),
			TeeLeft = Piece(style.TeeLeft),
			TeeRight = Piece(style.TeeRight),
			TitleOpen = Piece(style.TitleOpen),
			TitleClose = Piece(style.TitleClose),
		};
	}

	/// <summary><paramref name="guide"/> with every piece in the theme's guide colour.</summary>
	internal TreeGuide Paint(TreeGuide guide)
	{
		if (Theme.GuideColor is not { } markup) return guide;
		MarkupText Piece(MarkupText piece) => piece.Length > 0 ? MarkupText.Wrap(markup, piece) : piece;
		return guide with { Branch = Piece(guide.Branch), Last = Piece(guide.Last), Pipe = Piece(guide.Pipe), Blank = Piece(guide.Blank) };
	}
}

/// <summary>
/// The look of a layout: its borders, tree guides, gauge pieces, bullet and field separator, and the
/// colour of each part. Each is <see langword="null"/> until set, and an unset one comes from the theme
/// around it, and in the end from <see cref="Defaults"/>, which colours nothing.
/// </summary>
/// <remarks>
/// <para>Apply one to part of a tree with <see cref="BlockExtensions.Themed"/>: <c>sheet.Themed(new() { Border = BorderStyle.Heavy })</c>.</para>
/// <para>The colours are markup layers, so a part can be bold as well as coloured. A palette makes a whole
/// set of them at once (<see cref="ThemePalette.ToTheme"/>). Colour a piece sets itself wins over its part's.</para>
/// </remarks>
public sealed record LayoutTheme
{
	/// <summary>A theme that sets nothing, so everything comes from <see cref="Defaults"/>.</summary>
	public static LayoutTheme Default { get; } = new();

	/// <summary>
	/// What every unset piece ends up as: light box lines, light tree guides, <c>[█████░░░░░]</c>
	/// gauges, <c>•</c> bullets, <c>": "</c> after a label and <c>-</c> under table headings.
	/// </summary>
	public static LayoutTheme Defaults { get; } = new()
	{
		Border = BorderStyle.Single,
		Guide = TreeGuide.Line,
		GaugeFilled = MarkupText.Plain("█"),
		GaugeEmpty = MarkupText.Plain("░"),
		GaugeOpen = MarkupText.Plain("["),
		GaugeClose = MarkupText.Plain("]"),
		Bullet = MarkupText.Plain("•"),
		FieldSeparator = MarkupText.Plain(": "),
		HeaderRule = MarkupText.Plain("-"),
	};

	/// <summary>Boxes and rules.</summary>
	public BorderStyle? Border { get; init; }

	/// <summary>Tree guide lines.</summary>
	public TreeGuide? Guide { get; init; }

	/// <summary>A gauge's filled part, repeated.</summary>
	public MarkupText? GaugeFilled { get; init; }

	/// <summary>A gauge's empty part, repeated.</summary>
	public MarkupText? GaugeEmpty { get; init; }

	/// <summary>Before a gauge's bar.</summary>
	public MarkupText? GaugeOpen { get; init; }

	/// <summary>After a gauge's bar.</summary>
	public MarkupText? GaugeClose { get; init; }

	/// <summary>A bulleted list's marker.</summary>
	public MarkupText? Bullet { get; init; }

	/// <summary>Between a label and its value.</summary>
	public MarkupText? FieldSeparator { get; init; }

	/// <summary>The line under a table's headings, repeated; empty for none.</summary>
	public MarkupText? HeaderRule { get; init; }

	/// <summary>The colour of boxes, rules and the brackets round a gauge.</summary>
	public IMarkup? BorderColor { get; init; }

	/// <summary>The colour of a title set into a box's edge or a rule.</summary>
	public IMarkup? TitleColor { get; init; }

	/// <summary>The colour of a table's headings.</summary>
	public IMarkup? HeadingColor { get; init; }

	/// <summary>The colour of a field's label.</summary>
	public IMarkup? LabelColor { get; init; }

	/// <summary>The colour of the separator after a label, and between table columns.</summary>
	public IMarkup? SeparatorColor { get; init; }

	/// <summary>The colour of a list's markers.</summary>
	public IMarkup? BulletColor { get; init; }

	/// <summary>The colour of tree guide lines.</summary>
	public IMarkup? GuideColor { get; init; }

	/// <summary>The colour of the line under a table's headings.</summary>
	public IMarkup? HeaderRuleColor { get; init; }

	/// <summary>The colour of a gauge's filled part, when it has no gradient.</summary>
	public IMarkup? GaugeFilledColor { get; init; }

	/// <summary>The colour of a gauge's empty part.</summary>
	public IMarkup? GaugeEmptyColor { get; init; }

	/// <summary>
	/// What is laid under every second row of a table or list that is striped
	/// (<see cref="Table.Striped"/>, <see cref="Fields.Striped"/>): a background colour.
	/// </summary>
	public IMarkup? StripeColor { get; init; }

	/// <summary>Whether it sets any colour.</summary>
	public bool HasColor =>
		BorderColor is not null || TitleColor is not null || HeadingColor is not null || LabelColor is not null
		|| SeparatorColor is not null || BulletColor is not null || GuideColor is not null || HeaderRuleColor is not null
		|| GaugeFilledColor is not null || GaugeEmptyColor is not null || StripeColor is not null;

	/// <summary>This theme over <paramref name="below"/>: what this sets, and what it leaves unset from there.</summary>
	public LayoutTheme Over(LayoutTheme below)
	{
		ArgumentNullException.ThrowIfNull(below);
		return new()
		{
			Border = Border ?? below.Border,
			Guide = Guide ?? below.Guide,
			GaugeFilled = GaugeFilled ?? below.GaugeFilled,
			GaugeEmpty = GaugeEmpty ?? below.GaugeEmpty,
			GaugeOpen = GaugeOpen ?? below.GaugeOpen,
			GaugeClose = GaugeClose ?? below.GaugeClose,
			Bullet = Bullet ?? below.Bullet,
			FieldSeparator = FieldSeparator ?? below.FieldSeparator,
			HeaderRule = HeaderRule ?? below.HeaderRule,
			BorderColor = BorderColor ?? below.BorderColor,
			TitleColor = TitleColor ?? below.TitleColor,
			HeadingColor = HeadingColor ?? below.HeadingColor,
			LabelColor = LabelColor ?? below.LabelColor,
			SeparatorColor = SeparatorColor ?? below.SeparatorColor,
			BulletColor = BulletColor ?? below.BulletColor,
			GuideColor = GuideColor ?? below.GuideColor,
			HeaderRuleColor = HeaderRuleColor ?? below.HeaderRuleColor,
			GaugeFilledColor = GaugeFilledColor ?? below.GaugeFilledColor,
			GaugeEmptyColor = GaugeEmptyColor ?? below.GaugeEmptyColor,
			StripeColor = StripeColor ?? below.StripeColor,
		};
	}

	/// <summary>The theme's piece, or the default's.</summary>
	internal MarkupText Piece(Func<LayoutTheme, MarkupText?> piece) => piece(this) ?? piece(Defaults)!;
}
