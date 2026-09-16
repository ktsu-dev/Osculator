// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
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
}
