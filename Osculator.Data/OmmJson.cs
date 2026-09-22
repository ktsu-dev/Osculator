// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Time;

/// <summary>
/// Reads element sets from the Orbit Mean-Elements Message JSON that CelesTrak and Space-Track
/// distribute.
/// </summary>
/// <remarks>
/// The JSON form carries <em>more</em> precision than the fixed-column two-line element format for
/// some fields: it is generated from the originating values rather than by re-reading the text, so
/// eccentricity gains a decimal and the drag term gains three significant digits. Mean motion and
/// its first derivative are identical in both. Anything computing the data error term therefore has
/// to know which representation it read. See <see cref="ElementFieldQuantization"/>.
/// </remarks>
public static class OmmJson
{
	private static readonly JsonSerializerOptions Options = new()
	{
		PropertyNameCaseInsensitive = false,
		NumberHandling = JsonNumberHandling.AllowReadingFromString,
	};

	/// <summary>
	/// Reads every element set in an OMM JSON document.
	/// </summary>
	/// <param name="json">The document text, which may be a single object or an array of them.</param>
	/// <returns>The element sets, in document order.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
	/// <exception cref="JsonException"><paramref name="json"/> is not valid OMM JSON.</exception>
	public static IReadOnlyList<ElementSet> Read(string json)
	{
		Ensure.NotNull(json);

		string trimmed = json.TrimStart();
		OmmRecord[] records = trimmed.StartsWith('[')
			? JsonSerializer.Deserialize<OmmRecord[]>(json, Options) ?? []
			: [JsonSerializer.Deserialize<OmmRecord>(json, Options) ?? throw new JsonException("Document contained no element set.")];

		List<ElementSet> result = new(records.Length);

		foreach (OmmRecord record in records)
		{
			result.Add(ToElementSet(record));
		}

		return result;
	}

	private static ElementSet ToElementSet(OmmRecord record)
	{
		DateTime epoch = EpochOf(record);

		return new()
		{
			ObjectName = record.ObjectName ?? string.Empty,
			ObjectId = record.ObjectId ?? string.Empty,
			NoradCatalogId = record.NoradCatalogId,
			Epoch = epoch,
			EpochJulianDate = JulianDate.FromUtc(epoch),
			MeanMotion = record.MeanMotion,
			Eccentricity = record.Eccentricity,
			Inclination = record.Inclination,
			RightAscensionOfAscendingNode = record.RaOfAscNode,
			ArgumentOfPericenter = record.ArgOfPericenter,
			MeanAnomaly = record.MeanAnomaly,
			BStar = record.BStar,
			MeanMotionDot = record.MeanMotionDot,
			MeanMotionDdot = record.MeanMotionDdot,
			RevolutionAtEpoch = record.RevAtEpoch,
			ElementSetNumber = record.ElementSetNo,
		};
	}

	/// <summary>
	/// Reads an OMM record's epoch, which the standard writes as an ISO-8601 instant.
	/// </summary>
	/// <param name="record">The record.</param>
	/// <returns>The epoch, in UTC.</returns>
	/// <exception cref="JsonException">The record carried no <c>EPOCH</c>.</exception>
	private static DateTime EpochOf(OmmRecord record) => DateTime.ParseExact(
		record.Epoch ?? throw new JsonException("Element set carried no EPOCH."),
		["yyyy-MM-ddTHH:mm:ss.ffffff", "yyyy-MM-ddTHH:mm:ss.ffffffZ", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ"],
		CultureInfo.InvariantCulture,
		DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

	/// <summary>
	/// The wire shape. Internal so its members need no public documentation, and so the mapping to
	/// <see cref="ElementSet"/> stays the only way in.
	/// </summary>
	internal sealed class OmmRecord
	{
		[JsonPropertyName("OBJECT_NAME")] public string? ObjectName { get; set; }
		[JsonPropertyName("OBJECT_ID")] public string? ObjectId { get; set; }
		[JsonPropertyName("EPOCH")] public string? Epoch { get; set; }
		[JsonPropertyName("MEAN_MOTION")] public double MeanMotion { get; set; }
		[JsonPropertyName("ECCENTRICITY")] public double Eccentricity { get; set; }
		[JsonPropertyName("INCLINATION")] public double Inclination { get; set; }
		[JsonPropertyName("RA_OF_ASC_NODE")] public double RaOfAscNode { get; set; }
		[JsonPropertyName("ARG_OF_PERICENTER")] public double ArgOfPericenter { get; set; }
		[JsonPropertyName("MEAN_ANOMALY")] public double MeanAnomaly { get; set; }
		[JsonPropertyName("NORAD_CAT_ID")] public int NoradCatalogId { get; set; }
		[JsonPropertyName("BSTAR")] public double BStar { get; set; }
		[JsonPropertyName("MEAN_MOTION_DOT")] public double MeanMotionDot { get; set; }
		[JsonPropertyName("MEAN_MOTION_DDOT")] public double MeanMotionDdot { get; set; }
		[JsonPropertyName("REV_AT_EPOCH")] public int RevAtEpoch { get; set; }
		[JsonPropertyName("ELEMENT_SET_NO")] public int ElementSetNo { get; set; }
	}
}
