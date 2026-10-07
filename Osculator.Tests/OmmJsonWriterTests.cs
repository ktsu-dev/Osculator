// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers writing element sets as OMM JSON, against a real CelesTrak response.
/// </summary>
[TestClass]
public sealed class OmmJsonWriterTests
{
	/// <summary>
	/// The ISS response <c>OmmJsonTests</c> reads, as CelesTrak returned it. The service sends it on
	/// one line; the line breaks here are only the source file's wrapping, and are removed before it
	/// is compared.
	/// </summary>
	private const string IssResponse = """
		[{"OBJECT_NAME":"ISS (ZARYA)","OBJECT_ID":"1998-067A","EPOCH":"2026-09-15T08:51:14.158368",
		"MEAN_MOTION":15.49122235,"ECCENTRICITY":0.00049264,"INCLINATION":51.6311,
		"RA_OF_ASC_NODE":213.7631,"ARG_OF_PERICENTER":142.7188,"MEAN_ANOMALY":217.4143,
		"EPHEMERIS_TYPE":0,"CLASSIFICATION_TYPE":"U","NORAD_CAT_ID":25544,"ELEMENT_SET_NO":999,
		"REV_AT_EPOCH":58573,"BSTAR":0.00011249608,"MEAN_MOTION_DOT":5.779e-5,"MEAN_MOTION_DDOT":0}]
		""";

	[TestMethod]
	public void ARealCelesTrakResponse_IsWrittenBackByteForByte()
	{
		string served = IssResponse.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);

		string written = OmmJsonWriter.Write(OmmJson.Read(served));

		Assert.AreEqual(served, written);
	}

	[TestMethod]
	public void AnElementSetReadFromOmm_ReadsBackEqual()
	{
		ElementSet original = OmmJson.Read(IssResponse)[0];

		IReadOnlyList<ElementSet> reread = OmmJson.Read(OmmJsonWriter.Write([original]));

		Assert.HasCount(1, reread);
		Assert.AreEqual(original, reread[0]);
	}

	[TestMethod]
	public void EveryVerificationCase_ReadsBackEqual_ToTheMicrosecondOfItsEpoch()
	{
		// An element set read from two lines has its epoch at the tick a day-of-year landed on,
		// finer than the microsecond OMM writes. Eight decimals of a day is 864 microseconds, so
		// the epoch is still exact to the precision it was given in; everything else is exact.
		List<ElementSet> originals = [];

		foreach (VerificationSet.Case verification in VerificationSet.ReadCases())
		{
			originals.Add(verification.Elements);
		}

		IReadOnlyList<ElementSet> reread = OmmJson.Read(OmmJsonWriter.Write(originals));

		Assert.HasCount(originals.Count, reread);

		for (int i = 0; i < originals.Count; i++)
		{
			ElementSet original = originals[i];
			ElementSet copy = reread[i];

			Assert.IsLessThanOrEqualTo(5L, Math.Abs((copy.Epoch - original.Epoch).Ticks), $"Catalogue number {original.NoradCatalogId}.");
			Assert.AreEqual(original with { Epoch = copy.Epoch, EpochJulianDate = copy.EpochJulianDate }, copy, $"Catalogue number {original.NoradCatalogId}.");
		}
	}

	[TestMethod]
	public void Numbers_AreSpelledTheWayTheServiceSpellsThem()
	{
		Assert.AreEqual("0", OmmJsonWriter.FormatNumber(0.0));
		Assert.AreEqual("0", OmmJsonWriter.FormatNumber(-0.0));
		Assert.AreEqual("58573", OmmJsonWriter.FormatNumber(58573.0));
		Assert.AreEqual("15.49122235", OmmJsonWriter.FormatNumber(15.49122235));
		Assert.AreEqual("51.6311", OmmJsonWriter.FormatNumber(51.6311));
		Assert.AreEqual("0.0001", OmmJsonWriter.FormatNumber(0.0001));
		Assert.AreEqual("0.00011249608", OmmJsonWriter.FormatNumber(0.00011249608));
		Assert.AreEqual("5.779e-5", OmmJsonWriter.FormatNumber(5.779e-5));
		Assert.AreEqual("1.0e-5", OmmJsonWriter.FormatNumber(1e-5));
		Assert.AreEqual("-8.4e-7", OmmJsonWriter.FormatNumber(-8.4e-7));
		Assert.AreEqual("10000000000000000", OmmJsonWriter.FormatNumber(1e16));
		Assert.AreEqual("1.0e+17", OmmJsonWriter.FormatNumber(1e17));
		Assert.AreEqual("1.0e+20", OmmJsonWriter.FormatNumber(1e20));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => OmmJsonWriter.FormatNumber(double.NaN));
	}

	[TestMethod]
	public void ObjectNames_AreNotEscapedBeyondWhatJsonRequires()
	{
		// Real names carry '+', '/' and parentheses. The default encoder escapes '+' as +,
		// which is valid JSON and reads back the same, but is not what the service sends.
		ElementSet rocketBody = OmmJson.Read(IssResponse)[0] with { ObjectName = "ARIANE 44L+ R/B \"X\"" };

		string written = OmmJsonWriter.Write([rocketBody]);

		Assert.Contains("\"OBJECT_NAME\":\"ARIANE 44L+ R/B \\\"X\\\"\"", written, StringComparison.Ordinal);
		Assert.AreEqual(rocketBody.ObjectName, OmmJson.Read(written)[0].ObjectName);
	}
}
