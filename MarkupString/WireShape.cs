using System.Text.Json;
namespace MarkupString;

/// <summary>
/// Compares a markup object as read with the same object written back, to find what the write
/// would leave out.
/// </summary>
/// <remarks>
/// A codec may write a value in a different form from the one it read — a legacy colour array as
/// a palette index, <c>"bo":0</c> as <c>"bo":1</c> — so scalar values are not compared, and an array
/// may come back as a scalar. What may not change is the structure: every property read must be
/// written, an object must stay an object, an array that stays an array must keep its length, and
/// the discriminators (<c>"k"</c> for a markup, <c>"t"</c> for a block or a text) must keep their value.
/// </remarks>
internal static class WireShape
{
	/// <summary>
	/// Where <paramref name="written"/> leaves out part of <paramref name="read"/>, as a path from the
	/// object's root, or null when it keeps all of it. <paramref name="ignore"/> names one top-level
	/// property that is allowed to go.
	/// </summary>
	public static string? Lost(JsonElement read, JsonElement written, string? ignore) => Lost(read, written, ignore, "");

	private static string? Lost(JsonElement read, JsonElement written, string? ignore, string path)
	{
		switch (read.ValueKind)
		{
			case JsonValueKind.Object:
				if (written.ValueKind != JsonValueKind.Object) return $"{Where(path)} is no longer an object";
				foreach (var property in read.EnumerateObject())
				{
					if (property.Name == ignore) continue;
					var at = $"{path}/{property.Name}";
					if (!written.TryGetProperty(property.Name, out var value)) return $"{at} is dropped";
					if (property.Name is "k" or "t" && property.Value.ValueKind == JsonValueKind.String
							&& (value.ValueKind != JsonValueKind.String || value.GetString() != property.Value.GetString()))
						return $"{at} changes from {property.Value.GetRawText()} to {value.GetRawText()}";
					if (Lost(property.Value, value, ignore: null, at) is { } lost) return lost;
				}
				return null;

			case JsonValueKind.Array:
				if (written.ValueKind == JsonValueKind.Object) return $"{Where(path)} is no longer an array";
				if (written.ValueKind != JsonValueKind.Array) return null;
				var count = read.GetArrayLength();
				if (written.GetArrayLength() != count) return $"{Where(path)} has {count} entries and would be written with {written.GetArrayLength()}";
				var index = 0;
				using (var left = read.EnumerateArray())
				using (var right = written.EnumerateArray())
					while (left.MoveNext() && right.MoveNext())
					{
						if (Lost(left.Current, right.Current, ignore: null, $"{path}/{index}") is { } lost) return lost;
						index++;
					}
				return null;

			default:
				return null;
		}
	}

	private static string Where(string path) => path.Length == 0 ? "the object" : path;
}
