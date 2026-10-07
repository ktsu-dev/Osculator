// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers reading the OMM JSON form CelesTrak distributes.
/// </summary>
[TestClass]
public sealed class OmmJsonTests
{
	/// <summary>
	/// One element set for the International Space Station, exactly as returned by
	/// <c>celestrak.org/NORAD/elements/gp.php?CATNR=25544&amp;FORMAT=json</c>. Kept verbatim so the
	/// test exercises the real wire shape rather than a tidied version of it.
	/// </summary>
	private const string IssResponse = """
		[{"OBJECT_NAME":"ISS (ZARYA)","OBJECT_ID":"1998-067A","EPOCH":"2026-09-15T08:51:14.158368",
		"MEAN_MOTION":15.49122235,"ECCENTRICITY":0.00049264,"INCLINATION":51.6311,
		"RA_OF_ASC_NODE":213.7631,"ARG_OF_PERICENTER":142.7188,"MEAN_ANOMALY":217.4143,
		"EPHEMERIS_TYPE":0,"CLASSIFICATION_TYPE":"U","NORAD_CAT_ID":25544,"ELEMENT_SET_NO":999,
		"REV_AT_EPOCH":58573,"BSTAR":0.00011249608,"MEAN_MOTION_DOT":5.779e-5,"MEAN_MOTION_DDOT":0}]
		""";

	[TestMethod]
	public void Read_ParsesEveryFieldOfARealElementSet()
	{
		IReadOnlyList<ElementSet> sets = OmmJson.Read(IssResponse);

		Assert.HasCount(1, sets);
		ElementSet iss = sets[0];

		Assert.AreEqual("ISS (ZARYA)", iss.ObjectName);
		Assert.AreEqual("1998-067A", iss.ObjectId);
		Assert.AreEqual(25544, iss.NoradCatalogId);
		Assert.AreEqual(15.49122235, iss.MeanMotion);
		Assert.AreEqual(0.00049264, iss.Eccentricity);
		Assert.AreEqual(51.6311, iss.Inclination);
		Assert.AreEqual(213.7631, iss.RightAscensionOfAscendingNode);
		Assert.AreEqual(142.7188, iss.ArgumentOfPericenter);
		Assert.AreEqual(217.4143, iss.MeanAnomaly);
		Assert.AreEqual(0.00011249608, iss.BStar);
		Assert.AreEqual(5.779e-5, iss.MeanMotionDot);
		Assert.AreEqual(58573, iss.RevolutionAtEpoch);
	}

	[TestMethod]
	public void Read_TreatsAnEpochWithNoOffsetAsUtc()
	{
		ElementSet iss = OmmJson.Read(IssResponse)[0];

		Assert.AreEqual(DateTimeKind.Utc, iss.Epoch.Kind);
		Assert.AreEqual(new DateTime(2026, 9, 15, 8, 51, 14, DateTimeKind.Utc), iss.Epoch.AddTicks(-iss.Epoch.Ticks % TimeSpan.TicksPerSecond));
	}

	[TestMethod]
	public void Read_AcceptsABareObjectAsWellAsAnArray()
	{
		string single = IssResponse.Trim().TrimStart('[').TrimEnd(']');

		IReadOnlyList<ElementSet> sets = OmmJson.Read(single);

		Assert.HasCount(1, sets);
		Assert.AreEqual(25544, sets[0].NoradCatalogId);
	}

	[TestMethod]
	[DataRow("MEAN_MOTION")]
	[DataRow("ECCENTRICITY")]
	[DataRow("INCLINATION")]
	[DataRow("RA_OF_ASC_NODE")]
	[DataRow("ARG_OF_PERICENTER")]
	[DataRow("MEAN_ANOMALY")]
	[DataRow("BSTAR")]
	[DataRow("NORAD_CAT_ID")]
	[DataRow("EPOCH")]
	public void Read_RejectsARecordMissingAFieldTheOrbitNeeds(string field)
	{
		// An absent property used to deserialize as zero: a mean motion of zero, or an object filed
		// under catalogue number 0. That passed the CelesTrak client's parse-before-cache check.
		string missing = Without(IssResponse, field);

		Assert.DoesNotContain($"\"{field}\"", missing, StringComparison.Ordinal);
		Assert.ThrowsExactly<JsonException>(() => OmmJson.Read(missing));
	}

	[TestMethod]
	[DataRow("MEAN_MOTION_DDOT")]
	[DataRow("REV_AT_EPOCH")]
	[DataRow("ELEMENT_SET_NO")]
	[DataRow("MEAN_MOTION_DOT")]
	public void Read_AcceptsARecordMissingAFieldTheStandardLeavesOptional(string field)
	{
		ElementSet iss = OmmJson.Read(Without(IssResponse, field))[0];

		Assert.AreEqual(25544, iss.NoradCatalogId);
	}

	[TestMethod]
	public void Read_RejectsTheMinimalRecordTheIssueReported()
	{
		Assert.ThrowsExactly<JsonException>(() => OmmJson.Read("""[{"OBJECT_NAME":"X","EPOCH":"2026-09-15T08:51:14.158368","NORAD_CAT_ID":25544}]"""));
	}

	[TestMethod]
	[DataRow("2026-09-15T08:51:14.158")]
	[DataRow("2026-09-15T08:51:14.158000")]
	[DataRow("2026-09-15T08:51:14.1580000")]
	[DataRow("2026-09-15T08:51:14.158Z")]
	[DataRow("2026-09-15T08:51:14.158000+00:00")]
	[DataRow("2026-09-15T09:51:14.158+01:00")]
	[DataRow("2026-09-15T03:51:14.158-05:00")]
	public void Read_AcceptsAnyIsoSpellingOfTheSameEpoch(string epoch)
	{
		ElementSet iss = OmmJson.Read(WithEpoch(IssResponse, epoch))[0];

		Assert.AreEqual(DateTimeKind.Utc, iss.Epoch.Kind);
		Assert.AreEqual(new DateTime(2026, 9, 15, 8, 51, 14, 158, DateTimeKind.Utc), iss.Epoch);
	}

	[TestMethod]
	public void Read_KeepsSevenFractionalDigitsOfTheEpoch()
	{
		ElementSet iss = OmmJson.Read(WithEpoch(IssResponse, "2026-09-15T08:51:14.1583681"))[0];

		Assert.AreEqual(new DateTime(2026, 9, 15, 8, 51, 14, DateTimeKind.Utc).AddTicks(1583681), iss.Epoch);
	}

	[TestMethod]
	[DataRow("2026-09-15")]
	[DataRow("15/09/2026 08:51:14")]
	[DataRow("2026-09-15T08:51:14.")]
	[DataRow("2026-09-15T08:51:14.158368123")]
	[DataRow("not a date")]
	public void Read_RejectsAMalformedEpochAsJson(string epoch)
	{
		// JsonException, not FormatException: it is the one failure Read documents, and the one the
		// CelesTrak client's stale-copy fallback catches.
		JsonException error = Assert.ThrowsExactly<JsonException>(() => OmmJson.Read(WithEpoch(IssResponse, epoch)));

		Assert.Contains("EPOCH", error.Message, StringComparison.Ordinal);
	}

	/// <summary>Removes one property from the fixture.</summary>
	/// <param name="json">The fixture.</param>
	/// <param name="field">The property name.</param>
	/// <returns>The fixture without it.</returns>
	private static string Without(string json, string field)
	{
		JsonArray array = JsonNode.Parse(json)!.AsArray();
		array[0]!.AsObject().Remove(field);
		return array.ToJsonString();
	}

	/// <summary>Replaces the fixture's epoch.</summary>
	/// <param name="json">The fixture.</param>
	/// <param name="epoch">The new <c>EPOCH</c> text.</param>
	/// <returns>The fixture with it.</returns>
	private static string WithEpoch(string json, string epoch)
	{
		JsonArray array = JsonNode.Parse(json)!.AsArray();
		array[0]!["EPOCH"] = epoch;
		return array.ToJsonString();
	}
}
