// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the fixed-column two-line element format, and what it costs against the OMM JSON.
/// </summary>
[TestClass]
public sealed class TleParserTests
{
	/// <summary>
	/// One real ISS element set as CelesTrak serves it in the two-line format.
	/// </summary>
	private const string IssLine1 = "1 25544U 98067A   26258.88499338  .00006292  00000+0  12172-3 0  9999";

	/// <summary>The second line of the same element set.</summary>
	private const string IssLine2 = "2 25544  51.6310 211.2092 0004923 144.3133 215.8185 15.49128922585812";

	/// <summary>
	/// The same element set, at the same epoch, as CelesTrak serves it in OMM JSON.
	/// </summary>
	/// <remarks>
	/// Kept beside the text form deliberately: the pair is the evidence for
	/// <see cref="TheJsonFormCarriesMorePrecisionThanTheTextForm"/>, and a claim about two formats
	/// needs both of them committed or it cannot be checked.
	/// </remarks>
	private const string IssOmm = """
		{"OBJECT_NAME":"ISS (ZARYA)","OBJECT_ID":"1998-067A","EPOCH":"2026-09-15T21:14:23.428032",
		"MEAN_MOTION":15.49128922,"ECCENTRICITY":0.00049233,"INCLINATION":51.6310,
		"RA_OF_ASC_NODE":211.2092,"ARG_OF_PERICENTER":144.3133,"MEAN_ANOMALY":215.8185,
		"NORAD_CAT_ID":25544,"ELEMENT_SET_NO":999,"REV_AT_EPOCH":58581,
		"BSTAR":0.00012172288,"MEAN_MOTION_DOT":6.292e-05,"MEAN_MOTION_DDOT":0}
		""";

	[TestMethod]
	public void Parse_ReadsEveryFixedColumnField()
	{
		ElementSet iss = TleParser.Parse(IssLine1, IssLine2, "ISS (ZARYA)");

		Assert.AreEqual("ISS (ZARYA)", iss.ObjectName);
		Assert.AreEqual("98067A", iss.ObjectId);
		Assert.AreEqual(25544, iss.NoradCatalogId);
		Assert.AreEqual(51.6310, iss.Inclination);
		Assert.AreEqual(211.2092, iss.RightAscensionOfAscendingNode);
		Assert.AreEqual(144.3133, iss.ArgumentOfPericenter);
		Assert.AreEqual(215.8185, iss.MeanAnomaly);
		Assert.AreEqual(15.49128922, iss.MeanMotion);
		Assert.AreEqual(58581, iss.RevolutionAtEpoch);
		// The line ends "0  9999": ephemeris type 0, element number 999 right-aligned in columns
		// 65-68, and 9 as the checksum. The OMM for the same set agrees: ELEMENT_SET_NO is 999.
		Assert.AreEqual(999, iss.ElementSetNumber);
	}

	[TestMethod]
	public void Parse_ExpandsTheAssumedDecimalPointAndExponent()
	{
		// " 12172-3" means 0.12172 x 10^-3, not 12172 and not -3.
		ElementSet iss = TleParser.Parse(IssLine1, IssLine2);

		Assert.AreEqual(0.00012172, iss.BStar, 1e-12);
		Assert.AreEqual(0.0, iss.MeanMotionDdot);
	}

	[TestMethod]
	public void Parse_ReadsTheEpochAsUtcFromTwoDigitYearAndFractionalDay()
	{
		ElementSet iss = TleParser.Parse(IssLine1, IssLine2);

		Assert.AreEqual(DateTimeKind.Utc, iss.Epoch.Kind);
		Assert.AreEqual(2026, iss.Epoch.Year);
		Assert.AreEqual(258, iss.Epoch.DayOfYear);
	}

	[TestMethod]
	public void Parse_TreatsAnEccentricityFieldAsHavingAnAssumedLeadingDecimalPoint()
	{
		ElementSet iss = TleParser.Parse(IssLine1, IssLine2);

		Assert.AreEqual(0.0004923, iss.Eccentricity, 1e-12);
	}

	[TestMethod]
	public void TheJsonFormCarriesMorePrecisionThanTheTextForm()
	{
		// The finding this test exists to pin: the OMM JSON is generated from the originating
		// values rather than by re-reading the text, so the fields the text format compresses come
		// through finer. Anything computing the data error term has to know which it read.
		ElementSet fromText = TleParser.Parse(IssLine1, IssLine2);
		ElementSet fromJson = OmmJson.Read(IssOmm)[0];

		// Identical where the text format has room for the whole value.
		Assert.AreEqual(fromJson.MeanMotion, fromText.MeanMotion);
		Assert.AreEqual(fromJson.Inclination, fromText.Inclination);
		Assert.AreEqual(fromJson.MeanAnomaly, fromText.MeanAnomaly);
		Assert.AreEqual(fromJson.MeanMotionDot, fromText.MeanMotionDot, 1e-15);

		// One digit finer: seven decimals in the text, eight in the JSON.
		Assert.AreNotEqual(fromJson.Eccentricity, fromText.Eccentricity);
		Assert.AreEqual(0.00049233, fromJson.Eccentricity, 1e-12);
		Assert.AreEqual(0.0004923, fromText.Eccentricity, 1e-12);

		// Three digits finer: five significant digits in the text, eight in the JSON.
		Assert.AreNotEqual(fromJson.BStar, fromText.BStar);
		Assert.AreEqual(0.00012172288, fromJson.BStar, 1e-15);
		Assert.AreEqual(0.00012172, fromText.BStar, 1e-15);

		// And the text is a truncation of the JSON rather than a different value, which is what
		// makes the pair safe to treat as the same element set at two precisions.
		Assert.IsLessThan(
			ElementFieldQuantization.Eccentricity,
			System.Math.Abs(fromJson.Eccentricity - fromText.Eccentricity));
	}

	[TestMethod]
	public void Parse_RejectsALineTooShortForTheColumnLayout()
	{
		Assert.ThrowsExactly<FormatException>(() => TleParser.Parse("1 25544U", IssLine2));
	}

	[TestMethod]
	public void Parse_RejectsALineWhoseChecksumDoesNotMatch()
	{
		// One digit of the inclination altered, 51.6310 to 51.6410, and the checksum left alone.
		// Still 69 columns, still every field parseable — the corruption this digit exists to
		// catch, and the reason a length check is not enough.
		string corrupted = IssLine2.Replace("51.6310", "51.6410", StringComparison.Ordinal);

		Assert.AreEqual(IssLine2.Length, corrupted.Length);
		Assert.ThrowsExactly<FormatException>(() => TleParser.Parse(IssLine1, corrupted));
	}

	[TestMethod]
	public void Parse_RejectsTheTwoLinesSwapped()
	{
		// The message is asserted rather than just the exception type, because swapped lines
		// already threw before this check existed — some field of line 2 read at line 1's columns
		// happens not to parse. That is an accident of these particular elements, not a guard: it
		// says nothing about what is wrong, and a swap whose fields all parse would have gone
		// through. Naming column 1 is what makes it a guard.
		FormatException error = Assert.ThrowsExactly<FormatException>(() => TleParser.Parse(IssLine2, IssLine1));

		Assert.Contains("column 1", error.Message, StringComparison.Ordinal);
	}

	[TestMethod]
	public void Parse_AcceptsACorruptedChecksumWhenAskedToIgnoreIt()
	{
		// The escape hatch the constructed verification vectors need. Every field still has to
		// read correctly, so this also pins that ignoring the digit ignores nothing else.
		string corrupted = IssLine2.Replace("51.6310", "51.6410", StringComparison.Ordinal);

		ElementSet elements = TleParser.Parse(IssLine1, corrupted, checksum: TleChecksum.Ignore);

		Assert.AreEqual(51.6410, elements.Inclination);
		Assert.AreEqual(25544, elements.NoradCatalogId);
	}

	[TestMethod]
	public void Parse_ChecksTheLineNumbersEvenWhenIgnoringChecksums()
	{
		FormatException error = Assert.ThrowsExactly<FormatException>(
			() => TleParser.Parse(IssLine2, IssLine1, checksum: TleChecksum.Ignore));

		Assert.Contains("column 1", error.Message, StringComparison.Ordinal);
	}
}
