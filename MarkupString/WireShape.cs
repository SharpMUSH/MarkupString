using System.Text.Json;
namespace MarkupString;

/// <summary>
/// Compares a markup object as read with the same object written back, to find what the write
/// would leave out.
/// </summary>
/// <remarks>
/// A codec may write a value in a different form from the one it read — <c>"bo":0</c> as
/// <c>"bo":1</c>, a legacy <c>"#rrggbbaa"</c> as <c>"#rrggbb"</c> — so scalar values are not compared,
/// only their JSON kind. What may not change is the structure: every property read must be written,
/// a value keeps its kind (an object stays an object, an array keeps its length, a number stays a
/// number), and the discriminators (<c>"k"</c> for a markup, <c>"t"</c> for a block or a text) keep
/// their value. The one exception is the ANSI codec's legacy colour arrays (<c>"f":[31]</c>), which
/// it writes back as the palette index or hex colour they mean; a colour it cannot read is not
/// written at all, which counts as dropped.
/// </remarks>
internal static class WireShape
{
	/// <summary>The ANSI properties whose legacy SGR arrays are written back as a single colour.</summary>
	private static readonly string[] AnsiColorProperties = ["f", "g", "fs"];

	/// <summary>
	/// Where <paramref name="written"/> leaves out part of <paramref name="read"/>, as a path from the
	/// object's root, or null when it keeps all of it. <paramref name="ignore"/> names one top-level
	/// property that is allowed to go; <paramref name="kind"/> is the layer's kind.
	/// </summary>
	public static string? Lost(JsonElement read, JsonElement written, string? ignore, string kind)
	{
		if (read.ValueKind != JsonValueKind.Object || written.ValueKind != JsonValueKind.Object) return Lost(read, written, "");
		foreach (var property in read.EnumerateObject())
		{
			if (property.Name == ignore) continue;
			if (kind == "ansi" && property.Value.ValueKind == JsonValueKind.Array && Array.IndexOf(AnsiColorProperties, property.Name) >= 0
					&& written.TryGetProperty(property.Name, out var colour) && colour.ValueKind is JsonValueKind.Number or JsonValueKind.String)
				continue;
			if (Property(property, written, "") is { } lost) return lost;
		}
		return null;
	}

	private static string? Property(JsonProperty property, JsonElement written, string path)
	{
		var at = $"{path}/{property.Name}";
		if (!written.TryGetProperty(property.Name, out var value)) return $"{at} is dropped";
		if (property.Name is "k" or "t" && property.Value.ValueKind == JsonValueKind.String
				&& (value.ValueKind != JsonValueKind.String || value.GetString() != property.Value.GetString()))
			return $"{at} changes from {property.Value.GetRawText()} to {value.GetRawText()}";
		return Lost(property.Value, value, at);
	}

	private static string? Lost(JsonElement read, JsonElement written, string path)
	{
		if (Kind(read.ValueKind) != Kind(written.ValueKind))
			return $"{Where(path)} changes from {read.GetRawText()} to {written.GetRawText()}";

		switch (read.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (var property in read.EnumerateObject())
					if (Property(property, written, path) is { } lost) return lost;
				return null;

			case JsonValueKind.Array:
				var count = read.GetArrayLength();
				if (written.GetArrayLength() != count) return $"{Where(path)} has {count} entries and would be written with {written.GetArrayLength()}";
				var index = 0;
				using (var left = read.EnumerateArray())
				using (var right = written.EnumerateArray())
					while (left.MoveNext() && right.MoveNext())
					{
						if (Lost(left.Current, right.Current, $"{path}/{index}") is { } lost) return lost;
						index++;
					}
				return null;

			default:
				return null;
		}
	}

	/// <summary>A value's kind, with <see langword="true"/> and <see langword="false"/> as one.</summary>
	private static JsonValueKind Kind(JsonValueKind kind) => kind == JsonValueKind.False ? JsonValueKind.True : kind;

	private static string Where(string path) => path.Length == 0 ? "the object" : path;
}
