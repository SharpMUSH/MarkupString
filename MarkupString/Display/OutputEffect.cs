using MarkupString.Decoding;

namespace MarkupString.Display;

/// <summary>Reads <see cref="OutputEffect"/>s out of text.</summary>
public static class OutputEffects
{
	/// <summary>
	/// The effects in <paramref name="text"/>, in the order they stand: one for each point (once per carrier),
	/// and a <see cref="VariableEvent"/> for each stretch of text a <see cref="VariableMarkup"/> covers.
	/// </summary>
	/// <param name="text">The line.</param>
	/// <param name="capabilities">
	/// What the output can do; sounds are left out when it has no <see cref="OutputCapabilities.Sound"/>.
	/// Null keeps everything.
	/// </param>
	public static IReadOnlyList<OutputEffect> Of(MarkupText text, OutputCapabilities? capabilities = null)
	{
		ArgumentNullException.ThrowIfNull(text);

		var effects = new List<OutputEffect>();
		var source = text.Text;
		VariableMarkup? variable = null;
		var variableStart = 0;
		var variableEnd = 0;

		foreach (var run in text.Runs)
		{
			VariableMarkup? here = null;
			IPointMarkup? point = null;
			foreach (var markup in run.Markups)
			{
				if (markup is IPointMarkup p) point ??= p;
				else if (markup is VariableMarkup v) here ??= v;
			}

			// A variable's value is the whole stretch it covers, however many runs that is.
			if (variable is not null && (!Equals(here, variable) || run.Start != variableEnd))
			{
				effects.Add(Variable(variable, source, variableStart, variableEnd));
				variable = null;
			}

			if (here is not null)
			{
				if (variable is null)
				{
					variable = here;
					variableStart = run.Start;
				}
				variableEnd = run.End;
			}

			if (point is not null && (capabilities is null || SpanRenderer.Shows(point, capabilities)))
			{
				var effect = OutputEffect.Of(point);
				for (var count = Count(source.AsSpan(run.Start, run.Length), point.Carrier); count > 0; count--)
				{
					effects.Add(effect);
				}
			}
		}

		if (variable is not null) effects.Add(Variable(variable, source, variableStart, variableEnd));
		return effects;
	}

	private static VariableEvent Variable(VariableMarkup variable, string source, int start, int end) =>
		new(variable.Name, source[start..end].Replace(MarkupText.PointCarrier, string.Empty, StringComparison.Ordinal));

	private static int Count(ReadOnlySpan<char> text, string carrier)
	{
		var count = 0;
		for (var at = text.IndexOf(carrier, StringComparison.Ordinal); at >= 0; at = text.IndexOf(carrier, StringComparison.Ordinal))
		{
			count++;
			text = text[(at + carrier.Length)..];
		}

		return Math.Max(count, 1);
	}
}
