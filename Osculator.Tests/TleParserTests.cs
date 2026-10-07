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

	/// <summary>
	/// <see cref="IssLine2"/> with its catalogue number changed to Hubble's, 20580, and its
	/// checksum digit recomputed, so it passes every check a line can pass on its own.
	/// </summary>
	private const string OtherObjectLine2 = "2 20580  51.6310 211.2092 0004923 144.3133 215.8185 15.49128922585817";

	[TestMethod]
	public void Parse_RejectsLinesFromDifferentObjects()
	{
		// Both lines are well-formed and checksum-valid; only the pairing is wrong. Accepting it
		// would label the second object's orbit with the first object's catalogue number.
		FormatException error = Assert.ThrowsExactly<FormatException>(() => TleParser.Parse(IssLine1, OtherObjectLine2));

		Assert.Contains("25544", error.Message, StringComparison.Ordinal);
		Assert.Contains("20580", error.Message, StringComparison.Ordinal);
	}

	[TestMethod]
	public void Parse_ChecksTheCatalogueNumbersEvenWhenIgnoringChecksums()
	{
		// A structural check, not a checksum, so the escape hatch for constructed vectors does not
		// switch it off.
		FormatException error = Assert.ThrowsExactly<FormatException>(
			() => TleParser.Parse(IssLine1, OtherObjectLine2, checksum: TleChecksum.Ignore));

		Assert.Contains("catalogue number", error.Message, StringComparison.Ordinal);
	}

	[TestMethod]
	[DataRow("A0001", 100001)]
	[DataRow("H9999", 179999)]
	[DataRow("J0000", 180000)]
	[DataRow("N0000", 220000)]
	[DataRow("P0000", 230000)]
	[DataRow("Z9999", 339999)]
	public void Parse_DecodesAnAlpha5CatalogueNumber(string field, int expected)
	{
		// Catalogue numbers past 99999 do not fit five digits, so the leading digit becomes a
		// letter. The checksum is recomputed and verified: a letter counts as nothing in it.
		ElementSet elements = TleParser.Parse(WithCatalogNumber(IssLine1, field), WithCatalogNumber(IssLine2, field));

		Assert.AreEqual(expected, elements.NoradCatalogId);
	}

	[TestMethod]
	public void Parse_LeavesANumericCatalogueNumberAsItWas()
	{
		ElementSet elements = TleParser.Parse(WithCatalogNumber(IssLine1, "99999"), WithCatalogNumber(IssLine2, "99999"));

		Assert.AreEqual(99999, elements.NoradCatalogId);
	}

	[TestMethod]
	[DataRow("I0001")]
	[DataRow("O0001")]
	public void Parse_RejectsTheTwoLettersAlpha5Skips(string field)
	{
		FormatException error = Assert.ThrowsExactly<FormatException>(
			() => TleParser.Parse(WithCatalogNumber(IssLine1, field), WithCatalogNumber(IssLine2, field)));

		Assert.Contains("Alpha-5", error.Message, StringComparison.Ordinal);
	}

	[TestMethod]
	public void Parse_RejectsALowercaseAlpha5Letter()
	{
		Assert.ThrowsExactly<FormatException>(
			() => TleParser.Parse(WithCatalogNumber(IssLine1, "a0001"), WithCatalogNumber(IssLine2, "a0001")));
	}

	[TestMethod]
	public void Parse_ComparesAlpha5CatalogueNumbersByValue()
	{
		// A0001 and A0002 differ only by a digit, and both lines are checksum-valid, so only the
		// pairing check stands between them and one object's orbit under the other's number.
		FormatException error = Assert.ThrowsExactly<FormatException>(
			() => TleParser.Parse(WithCatalogNumber(IssLine1, "A0001"), WithCatalogNumber(IssLine2, "A0002")));

		Assert.Contains("100001", error.Message, StringComparison.Ordinal);
		Assert.Contains("100002", error.Message, StringComparison.Ordinal);
	}

	[TestMethod]
	[DataRow(" 01234-4", "  1234-4", 1.234e-6)]
	[DataRow(" 00234-4", "   234-4", 2.34e-7)]
	[DataRow("-01234-4", "- 1234-4", -1.234e-6)]
	public void Parse_ReadsASpacePaddedDragTermAsTheZeroPaddedOne(string zeroPadded, string spacePadded, double expected)
	{
		// The mantissa has an assumed point before its first column, so leading spaces are leading
		// zeros. Scaling by the digits left after trimming read "  1234-4" as ten times " 01234-4",
		// and the checksum cannot tell the two apart.
		ElementSet zeros = TleParser.Parse(WithBStar(IssLine1, zeroPadded), IssLine2);
		ElementSet spaces = TleParser.Parse(WithBStar(IssLine1, spacePadded), IssLine2);

		Assert.AreEqual(expected, zeros.BStar, System.Math.Abs(expected) * 1e-12);
		Assert.AreEqual(zeros.BStar, spaces.BStar);
	}

	[TestMethod]
	public void Parse_ReadsASpacePaddedSecondDerivativeAsTheZeroPaddedOne()
	{
		ElementSet zeros = TleParser.Parse(WithMeanMotionDdot(IssLine1, " 01234-4"), IssLine2);
		ElementSet spaces = TleParser.Parse(WithMeanMotionDdot(IssLine1, "  1234-4"), IssLine2);

		Assert.AreEqual(1.234e-6, zeros.MeanMotionDdot, 1e-18);
		Assert.AreEqual(zeros.MeanMotionDdot, spaces.MeanMotionDdot);
	}

	/// <summary>The zero-based column the second derivative of mean motion starts at on line 1.</summary>
	private const int MeanMotionDdotColumn = 44;

	/// <summary>The zero-based column the drag term starts at on line 1.</summary>
	private const int BStarColumn = 53;

	/// <summary>Replaces line 1's drag term and recomputes its checksum.</summary>
	/// <param name="line">The line.</param>
	/// <param name="field">The eight-column field.</param>
	/// <returns>The edited line.</returns>
	private static string WithBStar(string line, string field) => WithField(line, BStarColumn, field);

	/// <summary>Replaces line 1's second derivative of mean motion and recomputes its checksum.</summary>
	/// <param name="line">The line.</param>
	/// <param name="field">The eight-column field.</param>
	/// <returns>The edited line.</returns>
	private static string WithMeanMotionDdot(string line, string field) => WithField(line, MeanMotionDdotColumn, field);

	/// <summary>Replaces a line's catalogue number and recomputes its checksum.</summary>
	/// <param name="line">The line.</param>
	/// <param name="catalogNumber">The five-column catalogue number.</param>
	/// <returns>The edited line.</returns>
	private static string WithCatalogNumber(string line, string catalogNumber) => WithField(line, 2, catalogNumber);

	/// <summary>Overwrites a fixed-column field and recomputes the line's checksum digit.</summary>
	/// <param name="line">The line.</param>
	/// <param name="start">The zero-based column the field starts at.</param>
	/// <param name="text">The field, exactly as wide as the columns it replaces.</param>
	/// <returns>The edited line, still checksum-valid.</returns>
	private static string WithField(string line, int start, string text)
	{
		string edited = string.Concat(line.AsSpan(0, start), text, line.AsSpan(start + text.Length));
		int sum = 0;

		foreach (char c in edited.AsSpan(0, 68))
		{
			sum += c switch
			{
				>= '0' and <= '9' => c - '0',
				'-' => 1,
				_ => 0,
			};
		}

		return string.Concat(edited.AsSpan(0, 68), (sum % 10).ToString(System.Globalization.CultureInfo.InvariantCulture));
	}
}
