using System.Collections.Immutable;

namespace MarkupString.Layout;

/// <summary>
/// A list, each item after its marker — a bullet, a dash, a number — and wrapping under itself, not
/// under the marker.
/// </summary>
/// <param name="Items">The items.</param>
public sealed record Bullets(ImmutableArray<Block> Items) : Block
{
	/// <summary>What kind of marker.</summary>
	public BulletStyle Style { get; init; } = BulletStyle.Bullet;

	/// <summary>The marker for <see cref="BulletStyle.Custom"/>.</summary>
	public MarkupText? Marker { get; init; }

	/// <summary>The first number of a numbered list.</summary>
	public int Start { get; init; } = 1;

	/// <summary>Whether the markers count: numbers, letters or numerals.</summary>
	public bool Ordered => Style is BulletStyle.Number or BulletStyle.Alpha or BulletStyle.Roman;

	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines)
	{
		if (Items.IsDefaultOrEmpty) return;
		var markers = Enumerable.Range(0, Items.Length).Select(i => context.Paint(theme => theme.BulletColor, MarkerAt(i, context))).ToArray();
		var markerWidth = markers.Max(marker => marker.DisplayWidth);
		var gutter = markerWidth + (markerWidth > 0 ? 1 : 2);
		var hang = BlockText.Blank(gutter);

		for (var i = 0; i < Items.Length; i++)
		{
			var drawn = context.Lines(Items[i], width - gutter);
			if (drawn.Count == 0) drawn.Add(MarkupText.Empty);
			var marker = markers[i].Pad(MarkupText.Space, markerWidth, Ordered ? PadType.Left : PadType.Right, TruncationType.Truncate);
			for (var row = 0; row < drawn.Count; row++)
			{
				var lead = row == 0 ? MarkupText.Concat([marker, BlockText.Blank(gutter - markerWidth)]) : hang;
				lines.Add(BlockText.Fit(MarkupText.Concat([lead, drawn[row]]), width));
			}
		}
	}

	private MarkupText MarkerAt(int index, LayoutContext context)
	{
		var number = Start + index;
		return Style switch
		{
			BulletStyle.Bullet => context.Glyph(context.Theme.Piece(theme => theme.Bullet), "*"),
			BulletStyle.Dash => MarkupText.Plain("-"),
			BulletStyle.Star => MarkupText.Plain("*"),
			BulletStyle.Number => MarkupText.Plain(number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "."),
			BulletStyle.Alpha => MarkupText.Plain(Letters(number) + "."),
			BulletStyle.Roman => MarkupText.Plain(Roman(number) + "."),
			BulletStyle.Custom when Marker is { } marker => context.Glyph(marker, "*"),
			_ => MarkupText.Empty,
		};
	}

	/// <summary><c>a</c> to <c>z</c>, then <c>aa</c>, <c>ab</c>, ... as a spreadsheet names columns.</summary>
	internal static string Letters(int number)
	{
		if (number < 1) return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
		var letters = string.Empty;
		for (; number > 0; number = (number - 1) / 26) letters = (char)('a' + (number - 1) % 26) + letters;
		return letters;
	}

	/// <summary>Lower-case Roman numerals, for 1 to 3999; other numbers as digits.</summary>
	internal static string Roman(int number)
	{
		if (number is < 1 or > 3999) return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
		(int Value, string Numeral)[] table =
			[(1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"), (50, "l"), (40, "xl"), (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i")];
		var numeral = new System.Text.StringBuilder();
		foreach (var (value, text) in table)
			for (; number >= value; number -= value) numeral.Append(text);
		return numeral.ToString();
	}
}

/// <summary>The markers <see cref="Bullets"/> can use.</summary>
public enum BulletStyle
{
	/// <summary>The theme's bullet, <c>•</c> unless it says otherwise; <c>*</c> in ASCII.</summary>
	Bullet,

	/// <summary><c>-</c>.</summary>
	Dash,

	/// <summary><c>*</c>.</summary>
	Star,

	/// <summary><c>1.</c>, <c>2.</c>, ...</summary>
	Number,

	/// <summary><c>a.</c>, <c>b.</c>, ...</summary>
	Alpha,

	/// <summary><c>i.</c>, <c>ii.</c>, ...</summary>
	Roman,

	/// <summary>No marker; the items indented.</summary>
	None,

	/// <summary><see cref="Bullets.Marker"/>.</summary>
	Custom,
}

/// <summary>
/// Short items in as many columns as fit, every column as wide as the widest item, filled down each
/// column first — the way <c>ls</c> lists files, and a name-only who list wants.
/// </summary>
/// <param name="Items">The items.</param>
public sealed record Grid(ImmutableArray<MarkupText> Items) : Block
{
	/// <summary>Cells between two columns.</summary>
	public int Gap { get; init; } = 2;

	/// <summary>Fill along each row first instead.</summary>
	public bool Across { get; init; }

	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines)
	{
		if (Items.IsDefaultOrEmpty) return;
		var gap = Math.Max(0, Gap);
		var cell = Math.Min(width, Math.Max(1, Items.Max(item => item.DisplayWidth)));
		var columns = Math.Max(1, (width + gap) / (cell + gap));
		var rows = (Items.Length + columns - 1) / columns;
		columns = (Items.Length + rows - 1) / rows;
		var spacer = BlockText.Blank(gap);
		var parts = new List<MarkupText>(columns * 2);
		for (var row = 0; row < rows; row++)
		{
			parts.Clear();
			for (var column = 0; column < columns; column++)
			{
				var index = Across ? row * columns + column : column * rows + row;
				if (index >= Items.Length) continue;
				if (column > 0) parts.Add(spacer);
				parts.Add(BlockText.Fit(Items[index], cell));
			}
			lines.Add(BlockText.Fit(MarkupText.Concat(parts.ToArray().AsSpan()), width));
		}
	}

	/// <inheritdoc/>
	public override void DrawLinear(LayoutContext context, int width, IList<MarkupText> lines)
	{
		if (!Items.IsDefault) lines.AddRange(Items);
	}
}
