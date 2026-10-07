// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Globalization;
using System.Text.Json;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Data.Horizons;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers reading a vector table out of a <c>horizons.api</c> response.
/// </summary>
[TestClass]
public sealed class HorizonsVectorTableTests
{
	private const double SecondsPerHour = 3600;

	[TestMethod]
	public void TheHeaderAndEveryRowAreRead()
	{
		HorizonsEphemeris ephemeris = HorizonsVectorTable.Parse(HorizonsSample.MoonJson);

		Assert.AreEqual("Moon (301)", ephemeris.TargetName);
		Assert.AreEqual("Earth (399)", ephemeris.CenterName);
		Assert.AreEqual(HorizonsTimeScale.Tdb, ephemeris.TimeScale);
		Assert.AreEqual("ICRF", ephemeris.ReferenceFrame);
		Assert.HasCount(4, ephemeris.Vectors);

		HorizonsStateVector first = ephemeris.Vectors[0];

		Assert.AreEqual("A.D. 2026-Jan-01 00:00:00.0000", first.CalendarDate);
		Assert.AreEqual(1.743623490759919E+05, first.X);
		Assert.AreEqual(3.144044798926128E+05, first.Y);
		Assert.AreEqual(1.360549677452374E+05, first.Z);
		Assert.AreEqual(-9.118453147111976E-01, first.VelocityX);
		Assert.AreEqual(4.259300889027561E-01, first.VelocityY);
		Assert.AreEqual(1.843164083641041E-01, first.VelocityZ);
	}

	[TestMethod]
	public void PositionsAndVelocitiesAreNotSwapped()
	{
		// Every column has the same type, so a reader that took them in the wrong order would still
		// produce numbers. Over an hour the position has to move by the velocity times 3600 seconds.
		HorizonsEphemeris ephemeris = HorizonsVectorTable.Parse(HorizonsSample.MoonJson);
		HorizonsStateVector a = ephemeris.Vectors[0];
		HorizonsStateVector b = ephemeris.Vectors[1];

		double[] moved = [b.X - a.X, b.Y - a.Y, b.Z - a.Z];
		double[] predicted =
		[
			Mean(a.VelocityX, b.VelocityX) * SecondsPerHour,
			Mean(a.VelocityY, b.VelocityY) * SecondsPerHour,
			Mean(a.VelocityZ, b.VelocityZ) * SecondsPerHour,
		];

		for (int axis = 0; axis < 3; axis++)
		{
			Assert.AreEqual(predicted[axis], moved[axis], 1.0, $"Axis {axis} moved {moved[axis]} km against {predicted[axis]} predicted.");
		}
	}

	[TestMethod]
	public void TheEpochIsSplitWithoutLosingTheFraction()
	{
		JulianDate epoch = HorizonsVectorTable.Parse(HorizonsSample.MoonJson).Vectors[1].Epoch;

		Assert.AreEqual(2461041.5, epoch.Day);
		Assert.AreEqual((double)0.041666667m, epoch.DayFraction);

		// Reading the whole number into one double first would round it to a 48-microsecond grid,
		// which is the thing the split exists to avoid.
		double oneDouble = double.Parse(HorizonsSample.SecondRowJulianDate, CultureInfo.InvariantCulture);

		Assert.AreNotEqual(oneDouble - 2461041.5, epoch.DayFraction);
	}

	[TestMethod]
	public void AnEpochBeforeNoonBelongsToThePreviousJulianDay()
	{
		JulianDate epoch = HorizonsVectorTable.SplitJulianDate("2461041.25");

		Assert.AreEqual(2461040.5, epoch.Day);
		Assert.AreEqual(0.75, epoch.DayFraction);
	}

	[TestMethod]
	public void TheTimeScaleComesFromTheColumnHeader()
	{
		string utColumns = HorizonsSample.TdbColumns.Replace("JDTDB", " JDUT", StringComparison.Ordinal)
			.Replace("(TDB)", "(UT) ", StringComparison.Ordinal);

		HorizonsEphemeris ephemeris = HorizonsVectorTable.Parse(HorizonsSample.Wrap(HorizonsSample.Report(columns: utColumns)));

		Assert.AreEqual(HorizonsTimeScale.Ut, ephemeris.TimeScale);
	}

	[TestMethod]
	public void AnErrorMemberIsHorizonsRefusing()
	{
		string body = """{"signature":{"source":"NASA/JPL Horizons API","version":"1.2"},"error":"Cannot interpret date. Type \"?!\" for help."}""";

		HorizonsException refusal = Assert.ThrowsExactly<HorizonsException>(() => HorizonsVectorTable.Parse(body));

		Assert.Contains("Cannot interpret date", refusal.Message);
	}

	[TestMethod]
	public void AReportWithNoTableIsHorizonsAnsweringSomethingElse()
	{
		// What an ambiguous target gets: a list of candidates and no $$SOE.
		string report = """
			API VERSION: 1.2
			API SOURCE: NASA/JPL Horizons API

			*******************************************************************************
			 Multiple major-bodies match string "MOON*"

			  ID#      Name                               Designation  IAU/aliases/other
			  -------  ---------------------------------- -----------  -------------------
			      301  Moon                                            Luna
			      401  Phobos                                          MI
			""";

		HorizonsException refusal = Assert.ThrowsExactly<HorizonsException>(
			() => HorizonsVectorTable.Parse(HorizonsSample.Wrap(report)));

		Assert.Contains("Multiple major-bodies match", refusal.Message);
	}

	[TestMethod]
	public void ATableThatNeverEndsWasCutShort() =>
		Assert.ThrowsExactly<FormatException>(
			() => HorizonsVectorTable.Parse(HorizonsSample.Wrap(HorizonsSample.Report(endMarker: string.Empty))));

	[TestMethod]
	public void ATableInOtherUnitsIsRefusedRatherThanReadAsKilometres() =>
		Assert.ThrowsExactly<FormatException>(
			() => HorizonsVectorTable.Parse(HorizonsSample.Wrap(HorizonsSample.Report(units: "AU-D"))));

	[TestMethod]
	public void AnEmptyTableIsNotATable() =>
		Assert.ThrowsExactly<FormatException>(
			() => HorizonsVectorTable.Parse(HorizonsSample.Wrap(HorizonsSample.Report(rows: string.Empty))));

	[TestMethod]
	public void ARowWithAnUnreadableNumberIsRefused() =>
		Assert.ThrowsExactly<FormatException>(
			() => HorizonsVectorTable.Parse(HorizonsSample.Wrap(HorizonsSample.Report(
				rows: HorizonsSample.Rows.Replace("1.743623490759919E+05", "n.a.", StringComparison.Ordinal)))));

	[TestMethod]
	public void AnHtmlPageIsNotJson() =>
		Assert.Throws<JsonException>(() => HorizonsVectorTable.Parse("<html>Service unavailable</html>"));

	private static double Mean(double first, double second) => (first + second) / 2;
}
