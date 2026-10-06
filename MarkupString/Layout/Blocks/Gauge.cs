namespace MarkupString.Layout;

/// <summary>
/// A bar showing <paramref name="Value"/> against <paramref name="Maximum"/> — hit points, a quest's
/// progress — filling the width it is given, with its label before and the figures after.
/// </summary>
/// <param name="Value">How much there is.</param>
/// <param name="Maximum">How much there could be; the bar is full at this.</param>
public sealed record Gauge(double Value, double Maximum) : Block
{
	/// <summary>What it measures, or none.</summary>
	public MarkupText? Label { get; init; }

	/// <summary>The filled part, repeated; unset, the theme's (<c>█</c>).</summary>
	public MarkupText? Filled { get; init; }

	/// <summary>The empty part, repeated; unset, the theme's (<c>░</c>).</summary>
	public MarkupText? Empty { get; init; }

	/// <summary>Before the bar; unset, the theme's (<c>[</c>).</summary>
	public MarkupText? Open { get; init; }

	/// <summary>After the bar; unset, the theme's (<c>]</c>).</summary>
	public MarkupText? Close { get; init; }

	/// <summary>The figures written after the bar.</summary>
	public GaugeShow Show { get; init; } = GaugeShow.Percent;

	/// <summary>The bar's own width in cells, or zero to fill what the label and figures leave.</summary>
	public int BarWidth { get; init; }

	/// <summary>Colours the filled part is shaded with, or <see langword="null"/> to draw it as <see cref="Filled"/> is.</summary>
	public ColorGradient? Gradient { get; init; }

	/// <summary>How <see cref="Gradient"/> shades the bar.</summary>
	public GaugeShade Shade { get; init; } = GaugeShade.Cells;

	/// <summary>How full the bar is, from 0 to 1.</summary>
	public double Ratio => Maximum > 0 && double.IsFinite(Value) ? Math.Clamp(Value / Maximum, 0, 1) : 0;

	private string Figures => Show switch
	{
		GaugeShow.Value => $"{BlockText.Figure(Value)}/{BlockText.Figure(Maximum)}",
		GaugeShow.Percent => BlockText.Figure(Math.Round((Maximum > 0 && double.IsFinite(Value) ? Value / Maximum : 0) * 100)) + "%",
		_ => string.Empty,
	};

	/// <inheritdoc/>
	public override void Draw(LayoutContext context, int width, IList<MarkupText> lines)
	{
		var theme = context.Theme;
		var open = context.Glyph(Open ?? theme.Piece(t => t.GaugeOpen), "[");
		var close = context.Glyph(Close ?? theme.Piece(t => t.GaugeClose), "]");
		var filled = context.Glyph(Filled ?? theme.Piece(t => t.GaugeFilled), "#");
		var empty = context.Glyph(Empty ?? theme.Piece(t => t.GaugeEmpty), "-");
		var label = Label is { Length: > 0 } text ? MarkupText.Concat([text, MarkupText.Space]) : MarkupText.Empty;
		var figures = Figures;
		var after = figures.Length > 0 ? MarkupText.Plain(" " + figures) : MarkupText.Empty;

		var room = width - label.DisplayWidth - open.DisplayWidth - close.DisplayWidth - after.DisplayWidth;
		var bar = BarWidth > 0 ? Math.Min(BarWidth, Math.Max(1, room)) : Math.Max(1, room);
		var full = (int)Math.Round(bar * Ratio);
		var fill = BlockText.Run(filled, full);
		if (Gradient is { IsEmpty: false } gradient)
			fill = Shade == GaugeShade.Value ? gradient.Paint(fill, Ratio) : gradient.ShadeLines([fill], GradientFlow.Across, bar)[0];
		lines.Add(BlockText.Fit(MarkupText.Concat([label, open, fill, BlockText.Run(empty, bar - full), close, after]), width));
	}

	/// <inheritdoc/>
	public override void DrawLinear(LayoutContext context, int width, IList<MarkupText> lines)
	{
		var spoken = $"{BlockText.Figure(Value)} of {BlockText.Figure(Maximum)}" + (Show == GaugeShow.Percent ? $" ({Figures})" : string.Empty);
		lines.Add(Label is { Length: > 0 } named ? MarkupText.Concat([named, MarkupText.Plain(": " + spoken)]) : MarkupText.Plain(spoken));
	}
}

/// <summary>How a <see cref="Gauge.Gradient"/> shades a gauge.</summary>
public enum GaugeShade
{
	/// <summary>Each cell takes the colour at its place along the whole bar, so a fuller bar reaches further along the gradient.</summary>
	Cells,

	/// <summary>The whole filled part takes the colour at the value's place: red when nearly empty, green when full.</summary>
	Value,
}

/// <summary>The figures a <see cref="Gauge"/> writes after its bar.</summary>
public enum GaugeShow
{
	/// <summary><c>50%</c>.</summary>
	Percent,

	/// <summary><c>6/12</c>.</summary>
	Value,

	/// <summary>Nothing.</summary>
	None,
}
