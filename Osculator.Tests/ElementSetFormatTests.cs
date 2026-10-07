// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System.Collections.Generic;
using System.Linq;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Residuals;
using ktsu.Osculator.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers recording which format an element set was read from, and the data term reading its
/// quantization steps from that record.
/// </summary>
[TestClass]
public sealed class ElementSetFormatTests
{
	// The ISS pair TleParserTests carries: one element set, as CelesTrak serves it in both formats.
	private const string IssLine1 = "1 25544U 98067A   26258.88499338  .00006292  00000+0  12172-3 0  9999";
	private const string IssLine2 = "2 25544  51.6310 211.2092 0004923 144.3133 215.8185 15.49128922585812";

	private const string IssOmm = """
		{"OBJECT_NAME":"ISS (ZARYA)","OBJECT_ID":"1998-067A","EPOCH":"2026-09-15T21:14:23.428032",
		"MEAN_MOTION":15.49128922,"ECCENTRICITY":0.00049233,"INCLINATION":51.6310,
		"RA_OF_ASC_NODE":211.2092,"ARG_OF_PERICENTER":144.3133,"MEAN_ANOMALY":215.8185,
		"NORAD_CAT_ID":25544,"ELEMENT_SET_NO":999,"REV_AT_EPOCH":58581,
		"BSTAR":0.00012172288,"MEAN_MOTION_DOT":6.292e-05,"MEAN_MOTION_DDOT":0}
		""";

	[TestMethod]
	public void TheTextParserRecordsTle()
	{
		Assert.AreEqual(ElementSetFormat.Tle, TleParser.Parse(IssLine1, IssLine2).Format);
	}

	[TestMethod]
	public void TheJsonReaderRecordsOmm()
	{
		Assert.AreEqual(ElementSetFormat.Omm, OmmJson.Read(IssOmm)[0].Format);
		Assert.AreEqual(ElementSetFormat.Omm, OmmJson.Read($"[{IssOmm}]")[0].Format);
	}

	[TestMethod]
	public void AnElementSetBuiltByHandAssumesTheCoarserFormat()
	{
		// A forgotten format should overstate the data term, not understate it.
		ElementSet byHand = TleParser.Parse(IssLine1, IssLine2);
		ElementSet fresh = new()
		{
			ObjectName = byHand.ObjectName,
			ObjectId = byHand.ObjectId,
			NoradCatalogId = byHand.NoradCatalogId,
			Epoch = byHand.Epoch,
			EpochJulianDate = byHand.EpochJulianDate,
			MeanMotion = byHand.MeanMotion,
			Eccentricity = byHand.Eccentricity,
			Inclination = byHand.Inclination,
			RightAscensionOfAscendingNode = byHand.RightAscensionOfAscendingNode,
			ArgumentOfPericenter = byHand.ArgumentOfPericenter,
			MeanAnomaly = byHand.MeanAnomaly,
			BStar = byHand.BStar,
		};

		Assert.AreEqual(ElementSetFormat.Tle, fresh.Format);
	}

	[TestMethod]
	public void TheIssPairMeasuresTheSameEverywhereButEccentricityAndDrag()
	{
		// The two parses are the same element set at two precisions, so their data terms should
		// agree field for field except where the formats write different digits. Each is measured
		// against its own steps, which is the point: the OMM set is known more finely and says so.
		Dictionary<string, DataTerm.Contribution> fromText = Measure(TleParser.Parse(IssLine1, IssLine2));
		Dictionary<string, DataTerm.Contribution> fromJson = Measure(OmmJson.Read(IssOmm)[0]);

		Assert.AreEqual(1e-8, fromText[nameof(ElementSet.BStar)].StepSize, 1e-20);
		Assert.AreEqual(1e-11, fromJson[nameof(ElementSet.BStar)].StepSize, 1e-23);
		Assert.AreEqual(1e-7, fromText[nameof(ElementSet.Eccentricity)].StepSize);
		Assert.AreEqual(1e-8, fromJson[nameof(ElementSet.Eccentricity)].StepSize);

		Assert.IsLessThan(fromText[nameof(ElementSet.BStar)].PositionKilometers / 100.0, fromJson[nameof(ElementSet.BStar)].PositionKilometers);
		Assert.IsLessThan(fromText[nameof(ElementSet.Eccentricity)].PositionKilometers / 5.0, fromJson[nameof(ElementSet.Eccentricity)].PositionKilometers);

		foreach (string field in new[] { nameof(ElementSet.Epoch), nameof(ElementSet.MeanMotion), nameof(ElementSet.Inclination), nameof(ElementSet.MeanAnomaly) })
		{
			Assert.AreEqual(fromText[field].StepSize, fromJson[field].StepSize, $"{field}'s step should not depend on the format.");
		}
	}

	private static Dictionary<string, DataTerm.Contribution> Measure(ElementSet elements) =>
		DataTerm.Measure(elements, 1440.0, DoubleStorageMath.Instance).ToDictionary(c => c.Field);
}
