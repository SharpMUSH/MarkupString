namespace MarkupString;

/// <summary>
/// Names the value the text it wraps is, so a client can keep it — MXP's variables, which a client can
/// show in a status bar or a gauge. <see cref="MarkupText.Variable"/> builds one.
/// </summary>
/// <remarks>
/// MXP writes <c>&lt;VAR&gt;</c> around the text, and HTML marks it for the page. Every client shows
/// the text itself.
/// </remarks>
/// <param name="Name">The variable's name.</param>
public sealed record VariableMarkup(string Name) : IMarkup;

/// <summary>
/// A gauge showing one variable against another — hit points against their maximum. It wraps the
/// text a client with no gauges shows instead. <see cref="MarkupText.Gauge"/> builds one.
/// </summary>
/// <remarks>
/// MXP writes <c>&lt;GAUGE&gt;</c>, which follows the two variables as they change; HTML marks the text
/// for the page to draw. Every other format writes the text.
/// </remarks>
/// <param name="Variable">The variable the gauge shows.</param>
/// <param name="Maximum">The variable holding its maximum.</param>
/// <param name="Caption">The gauge's label.</param>
/// <param name="Color">Its colour, as a name or <c>#rrggbb</c>, or the client's own when null.</param>
public sealed record GaugeMarkup(string Variable, string Maximum, string? Caption = null, string? Color = null) : IMarkup;

/// <summary>
/// A variable shown in the client's status bar, optionally against a maximum. It wraps the text a
/// client with no status bar shows instead. <see cref="MarkupText.Status"/> builds one.
/// </summary>
/// <remarks>MXP writes <c>&lt;STAT&gt;</c>; HTML marks the text for the page. Every other format writes the text.</remarks>
/// <param name="Variable">The variable shown.</param>
/// <param name="Maximum">The variable holding its maximum, if it has one.</param>
/// <param name="Caption">The label shown with it.</param>
public sealed record StatusMarkup(string Variable, string? Maximum = null, string? Caption = null) : IMarkup;
