using System.Collections.Immutable;
using System.Text.Json;

namespace MarkupString.Mxp;

/// <summary>
/// Reads and writes <see cref="MxpElement"/> in the serializer's envelope, under kind <c>"mxp"</c>.
/// Keys are <c>e</c> (the element name), <c>w</c> (whether it wraps content, written only when it does)
/// and <c>a</c> (the arguments, each an array of name — null for a positional one — and value).
/// </summary>
public sealed class MxpElementCodec : IMarkupCodec
{
	/// <inheritdoc/>
	public string Kind => "mxp";

	/// <inheritdoc/>
	public Type MarkupType => typeof(MxpElement);

	/// <inheritdoc/>
	public void Write(Utf8JsonWriter writer, IMarkup markup)
	{
		ArgumentNullException.ThrowIfNull(writer);
		ArgumentNullException.ThrowIfNull(markup);

		var element = (MxpElement)markup;
		writer.WriteString("e", element.Name);
		if (element.WrapsContent) writer.WriteBoolean("w", true);

		if (element.Arguments.Length == 0) return;

		writer.WriteStartArray("a");
		foreach (var argument in element.Arguments)
		{
			writer.WriteStartArray();
			if (argument.Name is null) writer.WriteNullValue();
			else writer.WriteStringValue(argument.Name);
			writer.WriteStringValue(argument.Value);
			writer.WriteEndArray();
		}

		writer.WriteEndArray();
	}

	/// <inheritdoc/>
	public IMarkup Read(JsonElement element)
	{
		var name = element.TryGetProperty("e", out var e) && e.ValueKind == JsonValueKind.String
			? e.GetString() ?? string.Empty
			: string.Empty;

		var wraps = element.TryGetProperty("w", out var w) && w.ValueKind == JsonValueKind.True;

		var arguments = ImmutableArray.CreateBuilder<MxpArgument>();
		if (element.TryGetProperty("a", out var a) && a.ValueKind == JsonValueKind.Array)
		{
			foreach (var pair in a.EnumerateArray())
			{
				if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() != 2) continue;

				var argumentName = pair[0].ValueKind == JsonValueKind.String ? pair[0].GetString() : null;
				var value = pair[1].ValueKind == JsonValueKind.String ? pair[1].GetString() ?? string.Empty : string.Empty;
				arguments.Add(new MxpArgument(argumentName, value));
			}
		}

		return new MxpElement(name, [.. arguments], wraps);
	}
}
