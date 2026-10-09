using System.Collections.Immutable;

namespace MarkupString.Layout;

/// <summary>
/// Labelled values — <c>Sex: Male</c>, <c>Species: Human</c> — each label beside its value and the
/// values lined up in one column, a value that wraps keeping to that column.
/// </summary>
/// <param name="Items">The labels and values, in order.</param>
/// <remarks>
/// Too narrow for a label beside its value, each label goes on a line of its own with its value
/// indented under it. With <see cref="Columns"/> above one the fields are dealt into columns side by
/// side, down each column first, and the columns stack when they do not fit.
/// </remarks>
public sealed record Fields(ImmutableArray<Field> Items) : Block
{
	/// <inheritdoc/>
	public override Block MapText(Func<MarkupText, MarkupText> map) =>
		this with { Items = Items.IsDefault ? Items : Items.Select(item => item with { Label = map(item.Label), Value = item.Value.MapText(map) }).ToImmutableArray() };

	/// <summary>The narrowest a value column may be before each label goes over its value instead.</summary>
	private const int MinValue = 10;

	/// <summary>Where a label sits in the label column: <see cref="Alignment.Left"/> or <see cref="Alignment.Right"/>.</summary>
	public Alignment LabelAlignment { get; init; } = Alignment.Left;

	/// <summary>Drawn after each label; unset, the theme's (<c>": "</c>).</summary>
	public MarkupText? Separator { get; init; }

	/// <summary>A pattern filling the label column after a short label — <c>.</c> draws <c>Name......: </c> — or none.</summary>
	public MarkupText? Leader { get; init; }

	/// <summary>How many columns the fields are dealt into.</summary>
	public int Columns { get; init; } = 1;

	/// <summary>Cells between two columns.</summary>
	public int Gap { get; init; } = 3;

	/// <summary>
	/// Whether every second field is laid on the theme's <see cref="LayoutTheme.StripeColor"/>, across the
	/// label and the value. Dealt into columns, each column is striped on its own.
	/// </summary>
	public bool Striped { get; init; }

	/// <summary>The separator drawn under <paramref name="context"/>.</summary>
	internal MarkupText SeparatorIn(LayoutContext context) => context.Glyph(Separator ?? context.Theme.Piece(theme => theme.FieldSeparator), ": ");

	/// <summary>The fewest cells it needs to keep each label beside its value.</summary>
	public override BlockMeasure Measure(LayoutContext context, int width)
	{
		ArgumentNullException.ThrowIfNull(context);
		var natural = base.Measure(context, width).Natural;
		var labels = Items.IsDefaultOrEmpty ? 0 : Items.Max(field => field.Label.DisplayWidth);
		return new BlockMeasure(labels + SeparatorIn(context).DisplayWidth + MinValue, natural);
	}

	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines)
	{
		if (Items.IsDefaultOrEmpty) return;
		if (Columns > 1 && Items.Length > 1)
		{
			// Dealt down each column first, the way a reader expects to go on reading.
			var per = (Items.Length + Math.Min(Columns, Items.Length) - 1) / Math.Min(Columns, Items.Length);
			var columns = Items.Chunk(per)
				.Select(chunk => this with { Items = [.. chunk], Columns = 1 })
				.Select(column => (Block)new Sized(column) { Min = column.Measure(context, width).Min })
				.ToImmutableArray();
			context.Draw(new Flex(columns) { Gap = Math.Max(0, Gap) }, width, lines);
			return;
		}

		var separator = context.Paint(theme => theme.SeparatorColor, SeparatorIn(context));
		var labelWidth = Math.Min(Items.Max(field => field.Label.DisplayWidth), Math.Max(1, width / 2));
		var valueWidth = width - labelWidth - separator.DisplayWidth;
		if (valueWidth < Math.Min(MinValue, width))
		{
			// Too narrow to sit side by side: each label on its own line, its value indented under it.
			var indent = BlockText.Blank(Math.Min(2, width - 1));
			for (var index = 0; index < Items.Length; index++)
			{
				var field = Items[index];
				var first = lines.Count;
				if (field.Label.Length > 0)
					lines.AddRange(MarkupText.Concat([context.Paint(theme => theme.LabelColor, field.Label), separator.Trim(TrimType.TrimEnd)]).FormatColumn(BlockText.Column(width, Alignment.Left)));
				foreach (var line in context.Lines(field.Value, width - indent.DisplayWidth)) lines.Add(Striped ? BlockText.Fit(MarkupText.Concat([indent, line]), width) : MarkupText.Concat([indent, line]));
				if (Striped) context.Stripe(lines, first, index);
			}
			return;
		}

		// Without a leader the separator rides on the label, "Sex:" then the gap to the value; with
		// one, the leader fills from the label to the separator, "Sex.....: ".
		var alignment = LabelAlignment == Alignment.Right ? Alignment.Right : Alignment.Left;
		var head = separator.Trim(TrimType.TrimEnd);
		var leader = Leader is { Length: > 0 } pattern ? pattern : null;
		var labelColumn = labelWidth + separator.DisplayWidth;
		var blankLabel = BlockText.Blank(labelColumn);
		for (var index = 0; index < Items.Length; index++)
		{
			var field = Items[index];
			var first = lines.Count;
			var label = new List<MarkupText>();
			if (field.Label.Length > 0 && leader is not null)
			{
				var rows = context.Paint(theme => theme.LabelColor, field.Label).FormatColumn(BlockText.Column(labelWidth, alignment) with { Fill = context.Paint(theme => theme.SeparatorColor, leader) });
				for (var i = 0; i < rows.Length; i++)
					label.Add(MarkupText.Concat([BlockText.Fit(rows[i], labelWidth), i == 0 ? separator : BlockText.Blank(separator.DisplayWidth)]));
			}
			else if (field.Label.Length > 0)
			{
				var rows = MarkupText.Concat([context.Paint(theme => theme.LabelColor, field.Label), head]).FormatColumn(BlockText.Column(labelWidth + head.DisplayWidth, alignment));
				foreach (var row in rows) label.Add(BlockText.Fit(row, labelColumn));
			}

			var drawn = context.Lines(field.Value, valueWidth);
			var height = Math.Max(Math.Max(label.Count, drawn.Count), 1);
			for (var row = 0; row < height; row++)
			{
				lines.Add(MarkupText.Concat([
					row < label.Count ? label[row] : blankLabel,
					row < drawn.Count ? BlockText.Fit(drawn[row], valueWidth) : BlockText.Blank(valueWidth)]));
			}
			if (Striped) context.Stripe(lines, first, index);
		}
	}

	/// <inheritdoc/>
	public override void DrawLinear(LayoutContext context, int width, IList<MarkupText> lines)
	{
		if (Items.IsDefaultOrEmpty) return;
		var separator = SeparatorIn(context);
		foreach (var field in Items)
		{
			var value = context.Lines(field.Value, width);
			var first = value.Count > 0 ? value[0] : MarkupText.Empty;
			lines.Add(field.Label.Length == 0 ? first : MarkupText.Concat([field.Label, separator, first]));
			for (var i = 1; i < value.Count; i++) lines.Add(value[i]);
		}
	}
}

/// <summary>One label and its value in <see cref="Fields"/>.</summary>
/// <param name="Label">The label; empty to carry on the value above.</param>
/// <param name="Value">The value.</param>
public sealed record Field(MarkupText Label, Block Value);
