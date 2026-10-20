// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.SpaceTrack;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

/// <summary>
/// Reads Space-Track's <c>cdm_public</c> JSON.
/// </summary>
/// <remarks>
/// <para>
/// The response is an array of flat objects, every value a string, as the rest of Space-Track's
/// classes serve them: <c>CDM_ID</c>, <c>CREATED</c>, <c>TCA</c>, <c>MIN_RNG</c> in metres,
/// <c>PC</c>, <c>EMERGENCY_REPORTABLE</c> as <c>Y</c> or <c>N</c>, and <c>SAT_1_ID</c>,
/// <c>SAT_1_NAME</c>, <c>SAT_2_ID</c>, <c>SAT_2_NAME</c> for the two objects. Times carry no zone
/// and are UTC. A number written as a JSON number is read too, so a source that types its values
/// properly is not refused.
/// </para>
/// <para>
/// Anything else — an object rather than an array, a record without a time of closest approach,
/// a miss distance or either catalogue number, or a value that does not parse — raises
/// <see cref="JsonException"/>, so a body that is not a list of messages is never cached in place
/// of one that is. A missing or empty <c>PC</c> is a message without a published probability, not
/// a malformed one.
/// </para>
/// </remarks>
public static class ConjunctionDataMessageJson
{
	private static readonly string[] TimeFormats =
	[
		"yyyy-MM-ddTHH:mm:ss.FFFFFFF",
		"yyyy-MM-dd HH:mm:ss.FFFFFFF",
		"yyyy-MM-ddTHH:mm:ss",
		"yyyy-MM-dd HH:mm:ss",
	];

	/// <summary>Reads every message in a response.</summary>
	/// <param name="json">The response body.</param>
	/// <returns>The messages, in the order the response lists them.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
	/// <exception cref="JsonException"><paramref name="json"/> is not a list of conjunction data messages.</exception>
	public static IReadOnlyList<ConjunctionDataMessage> Read(string json)
	{
		Ensure.NotNull(json);

		using JsonDocument document = JsonDocument.Parse(json);

		if (document.RootElement.ValueKind != JsonValueKind.Array)
		{
			throw new JsonException("A cdm_public response is a JSON array.");
		}

		List<ConjunctionDataMessage> messages = [];

		foreach (JsonElement record in document.RootElement.EnumerateArray())
		{
			if (record.ValueKind != JsonValueKind.Object)
			{
				throw new JsonException("A cdm_public record is a JSON object.");
			}

			messages.Add(ReadOne(record));
		}

		return messages;
	}

	private static ConjunctionDataMessage ReadOne(JsonElement record)
	{
		string? probability = Text(record, "PC");

		return new ConjunctionDataMessage(
			Id: Required(record, "CDM_ID"),
			Created: Time(record, "CREATED"),
			TimeOfClosestApproach: Time(record, "TCA"),
			MissDistanceKm: Number(record, "MIN_RNG") / 1000.0,
			CollisionProbability: string.IsNullOrWhiteSpace(probability) ? null : Parse(probability, "PC"),
			EmergencyReportable: string.Equals(Text(record, "EMERGENCY_REPORTABLE"), "Y", StringComparison.OrdinalIgnoreCase),
			Primary: CatalogueNumber(record, "SAT_1_ID"),
			PrimaryName: Text(record, "SAT_1_NAME"),
			Secondary: CatalogueNumber(record, "SAT_2_ID"),
			SecondaryName: Text(record, "SAT_2_NAME"));
	}

	private static string? Text(JsonElement record, string name)
	{
		if (!record.TryGetProperty(name, out JsonElement value))
		{
			return null;
		}

		return value.ValueKind switch
		{
			JsonValueKind.String => value.GetString(),
			JsonValueKind.Number => value.GetRawText(),
			JsonValueKind.Null => null,
			_ => throw new JsonException(FormattableString.Invariant($"{name} is neither a string nor a number.")),
		};
	}

	private static string Required(JsonElement record, string name)
	{
		string? text = Text(record, name);

		return string.IsNullOrWhiteSpace(text)
			? throw new JsonException(FormattableString.Invariant($"A cdm_public record has no {name}."))
			: text;
	}

	private static double Number(JsonElement record, string name) => Parse(Required(record, name), name);

	private static double Parse(string text, string name) =>
		double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value)
			? value
			: throw new JsonException(FormattableString.Invariant($"{name} is not a number: '{text}'."));

	private static int CatalogueNumber(JsonElement record, string name)
	{
		string text = Required(record, name);

		return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
			? value
			: throw new JsonException(FormattableString.Invariant($"{name} is not a catalogue number: '{text}'."));
	}

	private static DateTimeOffset Time(JsonElement record, string name)
	{
		string text = Required(record, name);

		return DateTime.TryParseExact(
			text,
			TimeFormats,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
			out DateTime parsed)
			? new DateTimeOffset(parsed, TimeSpan.Zero)
			: throw new JsonException(FormattableString.Invariant($"{name} is not a time: '{text}'."));
	}
}
