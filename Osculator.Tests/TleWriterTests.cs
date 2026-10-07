// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers writing element sets back out in the two-line format, against the published verification
/// file and a real CelesTrak element set.
/// </summary>
[TestClass]
public sealed class TleWriterTests
{
	/// <summary>A real ISS element set as CelesTrak serves it, the same one <c>TleParserTests</c> reads.</summary>
	private const string IssLine1 = "1 25544U 98067A   26258.88499338  .00006292  00000+0  12172-3 0  9999";

	/// <summary>The second line of the same element set.</summary>
	private const string IssLine2 = "2 25544  51.6310 211.2092 0004923 144.3133 215.8185 15.49128922585812";

	/// <summary>The same element set, at the same epoch, as CelesTrak serves it in OMM JSON.</summary>
	private const string IssOmm = """
		{"OBJECT_NAME":"ISS (ZARYA)","OBJECT_ID":"1998-067A","EPOCH":"2026-09-15T21:14:23.428032",
		"MEAN_MOTION":15.49128922,"ECCENTRICITY":0.00049233,"INCLINATION":51.6310,
		"RA_OF_ASC_NODE":211.2092,"ARG_OF_PERICENTER":144.3133,"MEAN_ANOMALY":215.8185,
		"NORAD_CAT_ID":25544,"ELEMENT_SET_NO":999,"REV_AT_EPOCH":58581,
		"BSTAR":0.00012172288,"MEAN_MOTION_DOT":6.292e-05,"MEAN_MOTION_DDOT":0}
		""";

	/// <summary>
	/// Every column at which the written verification file differs from the published one, as
	/// <c>catalogue/line/column</c> with the column one-based as the format documents number them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Each one is a spelling <see cref="ElementSet"/> does not record, so no writer could reproduce
	/// it from the element set; every other column of the file's 33 cases is reproduced byte for byte.
	/// </para>
	/// <list type="bullet">
	/// <item>Column 63 of 11801: the ephemeris type is blank there, and written as 0.</item>
	/// <item>Column 60 of 09998 and both 20413 cases: a zero drag term is spelled <c>00000+0</c> in
	/// those lines and <c>00000-0</c> in the rest of the file, so a writer can match one or the other.
	/// Moving that sign moves the checksum too, so column 69 follows.</item>
	/// <item>Column 69 of the five lines whose published checksum digit is stale, all in the three
	/// 333xx cases, which the file's own comments say were edited by hand from real sets.</item>
	/// </list>
	/// </remarks>
	private static readonly string[] KnownDifferences =
	[
		"09998/1/60", "09998/1/69",
		"11801/1/63",
		"20413/1/60", "20413/1/69",
		"33333/1/69", "33333/2/69",
		"33334/1/69",
		"33335/1/69", "33335/2/69",
		"20413/1/60", "20413/1/69",
	];

	[TestMethod]
	public void TheVerificationFile_IsWrittenBackByteForByte_ExceptWhereAnElementSetCannotSayHow()
	{
		List<string> differences = [];

		foreach ((string line1, string line2) in VerificationLinePairs())
		{
			ElementSet elements = TleParser.Parse(line1, line2, checksum: TleChecksum.Ignore);
			TleLines written = TleWriter.Write(elements, TleWriterOptions.Vallado);

			differences.AddRange(Differences(elements.NoradCatalogId, 1, line1, written.Line1));
			differences.AddRange(Differences(elements.NoradCatalogId, 2, line2, written.Line2));
		}

		CollectionAssert.AreEqual(KnownDifferences, differences, string.Join(", ", differences));
	}

	[TestMethod]
	public void EveryStaleChecksumInTheVerificationFile_IsOneTheFileStatesWrongly()
	{
		// The other half of the column-69 exemptions above: each is a line whose own digit fails
		// against its own contents, which the writer did not decide. Every line it writes passes.
		foreach ((string line1, string line2) in VerificationLinePairs())
		{
			ElementSet elements = TleParser.Parse(line1, line2, checksum: TleChecksum.Ignore);
			TleLines written = TleWriter.Write(elements, TleWriterOptions.Vallado);

			Assert.AreEqual(TleWriter.Checksum(written.Line1), written.Line1[68] - '0');
			Assert.AreEqual(TleWriter.Checksum(written.Line2), written.Line2[68] - '0');

			foreach ((string published, string ours, int number) in new[] { (line1, written.Line1, 1), (line2, written.Line2, 2) })
			{
				string key = $"{elements.NoradCatalogId:00000}/{number}/69";

				if (KnownDifferences.Contains(key) && !KnownDifferences.Any(d => d.StartsWith($"{elements.NoradCatalogId:00000}/{number}/", StringComparison.Ordinal) && d != key))
				{
					Assert.AreNotEqual(TleWriter.Checksum(published), published[68] - '0', $"{key} is exempted as stale but states its checksum correctly.");
					Assert.AreEqual(published[..68], ours[..68]);
				}
			}
		}
	}

	[TestMethod]
	public void EveryVerificationCase_ParsesBackToTheSameElementSet()
	{
		foreach ((string line1, string line2) in VerificationLinePairs())
		{
			ElementSet original = TleParser.Parse(line1, line2, checksum: TleChecksum.Ignore);
			TleLines written = TleWriter.Write(original);

			// Verified this time: whatever the published digits say, ours have to be right.
			ElementSet reread = TleParser.Parse(written.Line1, written.Line2);

			Assert.AreEqual(original, reread, $"Catalogue number {original.NoradCatalogId}.");
		}
	}

	[TestMethod]
	public void ARealCelesTrakElementSet_IsWrittenBackByteForByte()
	{
		TleLines written = TleWriter.Write(TleParser.Parse(IssLine1, IssLine2));

		Assert.AreEqual(IssLine1, written.Line1);
		Assert.AreEqual(IssLine2, written.Line2);
	}

	[TestMethod]
	public void TheOmmFormOfAnElementSet_WritesTheTwoLinesCelesTrakServesForIt()
	{
		// The JSON carries finer eccentricity and drag than the columns hold, and its epoch is an
		// instant rather than a day of year. Writing it as two lines has to round each of those to
		// the very digits the service printed when it wrote the same set in the same format.
		TleLines written = TleWriter.Write(OmmJson.Read(IssOmm)[0]);

		Assert.AreEqual(IssLine1, written.Line1);
		Assert.AreEqual(IssLine2, written.Line2);
	}

	[TestMethod]
	public void CatalogueNumbersAbove99999_AreWrittenInAlpha5()
	{
		Assert.AreEqual("00005", TleWriter.CatalogNumber(5));
		Assert.AreEqual("99999", TleWriter.CatalogNumber(99999));
		Assert.AreEqual("A0000", TleWriter.CatalogNumber(100000));
		Assert.AreEqual("E8493", TleWriter.CatalogNumber(148493));
		Assert.AreEqual("J0000", TleWriter.CatalogNumber(180000));
		Assert.AreEqual("P0000", TleWriter.CatalogNumber(230000));
		Assert.AreEqual("Z9999", TleWriter.CatalogNumber(339999));
	}

	[TestMethod]
	public void ValuesTheColumnsCannotHold_AreRefusedRatherThanWritten()
	{
		ElementSet iss = TleParser.Parse(IssLine1, IssLine2);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TleWriter.Write(iss with { NoradCatalogId = 340000 }));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TleWriter.Write(iss with { Eccentricity = 1.0 }));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TleWriter.Write(iss with { Eccentricity = 0.99999999 }));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TleWriter.Write(iss with { MeanMotion = 100.0 }));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TleWriter.Write(iss with { MeanMotionDot = 1.0 }));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TleWriter.Write(iss with { BStar = 1e10 }));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TleWriter.Write(iss with { Inclination = -1.0 }));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TleWriter.Write(iss with { RevolutionAtEpoch = 100000 }));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TleWriter.Write(iss with { ElementSetNumber = 10000 }));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TleWriter.Write(iss with { ObjectId = "1998-067ABC" }));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TleWriter.Write(iss with { Epoch = new DateTime(2057, 1, 1, 0, 0, 0, DateTimeKind.Utc) }));
	}

	[TestMethod]
	public void AssumedDecimalFields_WriteTheirExponentEitherWay()
	{
		ElementSet iss = TleParser.Parse(IssLine1, IssLine2) with { BStar = 0.13519 };

		Assert.AreEqual(" 13519+0", TleWriter.Write(iss, TleWriterOptions.CelesTrak).Line1[53..61]);
		Assert.AreEqual(" 13519-0", TleWriter.Write(iss, TleWriterOptions.Vallado).Line1[53..61]);
		Assert.AreEqual("-10000-3", TleWriter.Write(iss with { BStar = -1e-4 }).Line1[53..61]);
		Assert.AreEqual(" 10000-4", TleWriter.Write(iss with { BStar = 1e-5 }).Line1[53..61]);

		// Five digits of 0.999996 round up to a sixth. The mantissa has to renormalize to 0.10000
		// and carry into the exponent, or the field is nine columns wide and shifts the line.
		Assert.AreEqual(" 10000-2", TleWriter.Write(iss with { BStar = 0.999996e-3 }).Line1[53..61]);
	}

	/// <summary>Reads the verification file's line pairs as published, comment lines skipped.</summary>
	/// <returns>Each case's two lines, cut at column 69 to drop the start, stop and step that follow.</returns>
	private static IEnumerable<(string Line1, string Line2)> VerificationLinePairs()
	{
		string? pending = null;

		foreach (string line in File.ReadAllLines(Path.Join(VerificationSet.DataDirectory, "SGP4-VER.TLE")))
		{
			if (line.StartsWith("1 ", StringComparison.Ordinal))
			{
				pending = line[..69];
			}
			else if (line.StartsWith("2 ", StringComparison.Ordinal) && pending is not null)
			{
				yield return (pending, line[..69]);
				pending = null;
			}
		}
	}

	/// <summary>Lists the columns at which two lines differ.</summary>
	/// <param name="catalog">The catalogue number, for the key.</param>
	/// <param name="number">The line number, for the key.</param>
	/// <param name="published">The published line.</param>
	/// <param name="written">The written line.</param>
	/// <returns>One <c>catalogue/line/column</c> key per differing column, the column one-based.</returns>
	private static IEnumerable<string> Differences(int catalog, int number, string published, string written)
	{
		Assert.AreEqual(published.Length, written.Length, $"{catalog} line {number}: {written}");

		for (int i = 0; i < published.Length; i++)
		{
			if (published[i] != written[i])
			{
				yield return $"{catalog:00000}/{number}/{i + 1}";
			}
		}
	}
}
