// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Data.Sp3;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// The SP3-c parser, against two real files: an IGS final GPS orbit and an ILRS LAGEOS-1 orbit.
/// </summary>
/// <remarks>
/// The two were chosen because they differ in every way the format allows. The IGS file is a
/// position-only orbit on GPS time with satellite clocks, one of which is the bad-value marker
/// throughout; the LAGEOS-1 file is a position-and-velocity orbit on UTC with no clock field at all,
/// which is what every ILRS orbit the M4 gate will read looks like. See <c>Data/Sp3/ATTRIBUTION.md</c>.
/// </remarks>
[TestClass]
public sealed class Sp3ParserTests
{
	private static readonly string[] IgsSatellites = ["G01", "G02", "G05"];

	internal static string Igs => File.ReadAllText(Path.Join(VerificationSet.DataDirectory, "Sp3", "igs16295-excerpt.sp3"));

	internal static string Lageos => File.ReadAllText(Path.Join(VerificationSet.DataDirectory, "Sp3", "lageos1-excerpt.sp3"));

	[TestMethod]
	public void TheIgsHeaderIsReadField_ByField()
	{
		Sp3File file = Sp3Parser.Parse(Igs);

		Assert.AreEqual('c', file.Version);
		Assert.IsFalse(file.HasVelocities);
		Assert.AreEqual("IGS05", file.CoordinateSystem);
		Assert.AreEqual("HLM", file.OrbitType);
		Assert.AreEqual("IGS", file.Agency);
		Assert.AreEqual("GPS", file.TimeSystem);
		Assert.AreEqual(900.0, file.EpochIntervalSeconds);
		CollectionAssert.AreEqual(IgsSatellites, (System.Collections.ICollection)file.Satellites);
		Assert.HasCount(96, file.Epochs);
		Assert.AreEqual(JulianDate.FromCalendar(2011, 4, 1, 0, 0, 0.0), file.Start);
		Assert.AreEqual(file.Start, file.Epochs[0]);
	}

	[TestMethod]
	public void PositionsAndClocksAreReadInTheFilesUnits()
	{
		Sp3File file = Sp3Parser.Parse(Igs);
		IReadOnlyList<Sp3Record> g02 = file.RecordsOf("G02");

		Assert.HasCount(96, g02);
		Assert.AreEqual(-11706.796694, g02[0].X);
		Assert.AreEqual(13773.960913, g02[0].Y);
		Assert.AreEqual(19334.631024, g02[0].Z);
		Assert.AreEqual(328.187255, g02[0].ClockMicroseconds);
		Assert.IsNull(g02[0].Velocity);

		// The last epoch is 23:45, ninety-five intervals in, and computed from the calendar fields it
		// is an exact whole number of seconds rather than a difference of two Julian dates.
		Assert.AreEqual(95 * 900.0, g02[^1].SecondsSinceStart);

		// A GPS orbit's radius is about 26,560 km. A column cut one character off would not be.
		foreach (Sp3Record r in g02)
		{
			double radius = Math.Sqrt((r.X * r.X) + (r.Y * r.Y) + (r.Z * r.Z));
			Assert.IsTrue(radius is > 26000.0 and < 27000.0, $"radius {radius} km");
		}
	}

	[TestMethod]
	public void TheBadClockMarkerIsNoClock_NotAClockOfAMillionMicroseconds()
	{
		Sp3File file = Sp3Parser.Parse(Igs);
		IReadOnlyList<Sp3Record> g01 = file.RecordsOf("G01");

		// G01 has an orbit and no clock throughout this file; both halves of that are the point.
		Assert.HasCount(96, g01);

		foreach (Sp3Record r in g01)
		{
			Assert.IsNull(r.ClockMicroseconds);
		}
	}

	[TestMethod]
	public void TheLageosFileCarriesVelocities_InKilometresPerSecond_AndNoClock()
	{
		Sp3File file = Sp3Parser.Parse(Lageos);

		Assert.IsTrue(file.HasVelocities);
		Assert.AreEqual("UTC", file.TimeSystem);
		Assert.AreEqual("ITRF14", file.CoordinateSystem);
		Assert.AreEqual("JCET", file.Agency);
		Assert.AreEqual(120.0, file.EpochIntervalSeconds);

		IReadOnlyList<Sp3Record> l51 = file.RecordsOf("L51");
		Assert.HasCount(31, l51);

		Sp3Record first = l51[0];
		Assert.AreEqual(-995.887942, first.X);
		Assert.IsNull(first.ClockMicroseconds);
		Assert.IsNotNull(first.Velocity);

		// 28940.073413 decimetres per second is 2.8940073413 km/s.
		Assert.AreEqual(2.8940073413, first.Velocity.Value.X, 1e-12);

		// LAGEOS-1 is in a near-circular orbit at about 12,270 km radius, moving at about 5.7 km/s.
		// A velocity read in the file's own decimetres would be ten thousand times that.
		foreach (Sp3Record r in l51)
		{
			Sp3Velocity v = r.Velocity!.Value;
			double radius = Math.Sqrt((r.X * r.X) + (r.Y * r.Y) + (r.Z * r.Z));
			double speed = Math.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));
			Assert.IsTrue(radius is > 12000.0 and < 12500.0, $"radius {radius} km");
			Assert.IsTrue(speed is > 5.0 and < 6.5, $"speed {speed} km/s");
		}
	}

	[TestMethod]
	public void AnotherVersionIsRefusedByName()
	{
		FormatException e = Assert.ThrowsExactly<FormatException>(() => Sp3Parser.Parse("#d" + Igs[2..]));
		StringAssert.Contains(e.Message, "'d'");
		StringAssert.Contains(e.Message, "SP3-c");
	}

	[TestMethod]
	public void TextThatIsNotSp3IsRefused() =>
		Assert.ThrowsExactly<FormatException>(() => Sp3Parser.Parse("MJD;Year;Month\n1;2;3\n"));

	[TestMethod]
	public void AnUnknownPositionVelocityFlagIsRefused() =>
		Assert.ThrowsExactly<FormatException>(() => Sp3Parser.Parse("#cX" + Igs[3..]));

	[TestMethod]
	public void ATruncatedFileIsRefused_NotHalfRead()
	{
		// Cut the file at its last epoch line, the way a dropped connection would.
		string cut = Igs[..Igs.LastIndexOf("\n*", StringComparison.Ordinal)];

		FormatException e = Assert.ThrowsExactly<FormatException>(() => Sp3Parser.Parse(cut));
		StringAssert.Contains(e.Message, "96");
		StringAssert.Contains(e.Message, "95");
	}

	[TestMethod]
	public void ARecordForAnUndeclaredSatelliteIsRefused()
	{
		int at = Igs.IndexOf("\nPG05", StringComparison.Ordinal);
		string altered = Igs[..at] + "\nPG07" + Igs[(at + 5)..];

		FormatException e = Assert.ThrowsExactly<FormatException>(() => Sp3Parser.Parse(altered));
		StringAssert.Contains(e.Message, "G07");
	}

	[TestMethod]
	public void AnEpochThatIsNotACalendarInstantIsRefused()
	{
		// Some producers have written the day after 31 December as day 0 of January. That is what
		// the original of the LAGEOS excerpt goes on to do, and it is why the excerpt stops at
		// midnight.
		string altered = Lageos.Replace("* 2021 12 31  0  0", "* 2022  1  0  0  0", StringComparison.Ordinal);

		FormatException e = Assert.ThrowsExactly<FormatException>(() => Sp3Parser.Parse(altered));
		StringAssert.Contains(e.Message, "not a calendar instant");
	}

	[TestMethod]
	public void EpochsOutOfOrderAreRefused()
	{
		string altered = Lageos.Replace("* 2021 12 30 23  2", "* 2021 12 30 22 58", StringComparison.Ordinal);

		Assert.ThrowsExactly<FormatException>(() => Sp3Parser.Parse(altered));
	}

	[TestMethod]
	public void AMissingPositionIsLeftOut_NotPlacedAtTheCentreOfTheEarth()
	{
		int at = Igs.IndexOf("\nPG05", StringComparison.Ordinal);
		int end = Igs.IndexOf('\n', at + 1);
		string missing = "\nPG05      0.000000      0.000000      0.000000 999999.999999";
		string altered = Igs[..at] + missing + Igs[end..];

		Sp3File file = Sp3Parser.Parse(altered);

		Assert.HasCount(95, file.RecordsOf("G05"));
		Assert.HasCount(96, file.Epochs);
	}

	[TestMethod]
	public void AskingForAnUndeclaredSatelliteThrows() =>
		Assert.ThrowsExactly<ArgumentException>(() => Sp3Parser.Parse(Igs).RecordsOf("G07"));
}
