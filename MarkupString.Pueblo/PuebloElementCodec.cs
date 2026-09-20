using System.Collections.Immutable;
using System.Text.Json;

namespace MarkupString.Pueblo;

/// <summary>
/// Reads and writes <see cref="PuebloElement"/> in the serializer's envelope, under kind <c>"pueblo"</c>.
/// Keys are <c>e</c> (the element name), <c>w</c> (whether it wraps content, written only when it does)
/// and <c>a</c> (the attributes, each an array of name and value).
/// </summary>
public sealed class PuebloElementCodec : IMarkupCodec
{
	/// <inheritdoc/>
	public string Kind => "pueblo";

	/// <inheritdoc/>
	public Type MarkupType => typeof(PuebloElement);

	/// <inheritdoc/>
	public void Write(Utf8JsonWriter writer, IMarkup markup)
	{
		ArgumentNullException.ThrowIfNull(writer);
		ArgumentNullException.ThrowIfNull(markup);

		var element = (PuebloElement)markup;
		writer.WriteString("e", element.Name);
		if (element.WrapsContent) writer.WriteBoolean("w", true);

		if (element.Attributes.Length == 0) return;

		writer.WriteStartArray("a");
		foreach (var attribute in element.Attributes)
		{
			writer.WriteStartArray();
			writer.WriteStringValue(attribute.Name);
			writer.WriteStringValue(attribute.Value);
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

		var attributes = ImmutableArray.CreateBuilder<PuebloAttribute>();
		if (element.TryGetProperty("a", out var a) && a.ValueKind == JsonValueKind.Array)
		{
			foreach (var pair in a.EnumerateArray())
			{
				if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() != 2) continue;

				if (pair[0].ValueKind != JsonValueKind.String) continue;

				var attributeName = pair[0].GetString() ?? string.Empty;
				var value = pair[1].ValueKind == JsonValueKind.String ? pair[1].GetString() ?? string.Empty : string.Empty;
				attributes.Add(new PuebloAttribute(attributeName, value));
			}
		}

		return new PuebloElement(name, [.. attributes], wraps);
	}
}
