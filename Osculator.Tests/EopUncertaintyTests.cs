// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Residuals;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Data.Iers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers carrying the IERS's stated uncertainties through to a contribution in metres (#75).
/// </summary>
[TestClass]
public sealed class EopUncertaintyTests
{
	private static readonly DoubleStorageMath Math = DoubleStorageMath.Instance;

	/// <summary>
	/// A LEO position, about 410 km up, out of the equatorial plane so both pole terms have
	/// something to move.
	/// </summary>
	private static readonly TemeState<double> Leo = new(4000.0, 3000.0, 4590.0, -4.1, 5.9, 0.9);

	private const string Header =
		"MJD;Year;Month;Day;Type;x_pole;sigma_x_pole;y_pole;sigma_y_pole;x_rate;sigma_x_rate;y_rate;sigma_y_rate;Type;UT1-UTC;sigma_UT1-UTC;LOD;sigma_LOD;Type;dPsi;sigma_dPsi;dEpsilon;sigma_dEpsilon;dX;sigma_dX;dY;sigma_dY;Type;bulB/x_pole;bulB/y_pole;Type;bulB/UT-UTC;Type;bulB/dPsi;bulB/dEpsilon;bulB/dX;bulB/dY\n";

	/// <summary>
	/// One real final row from the sample, then two forecast rows a year later. The year-out rows
	/// are constructed, not copied: the UT1 − UTC sigma is the 2.5e-2 s a year-out IERS forecast
	/// states, which <see cref="EarthOrientation"/> documents, and the pole sigmas are illustrative.
	/// </summary>
	private const string YearOutCsv = Header +
		"61296;2026;09;13;final;0.194510;0.000090;0.331655;0.000091;;;;;final;-0.0050574;0.0000262;1.1743;0.0190;prediction;;;;;;;;;;;;;;;;;;\n" +
		"61661;2027;09;13;prediction;0.150000;0.020000;0.380000;0.020000;;;;;prediction;-0.0400000;0.0250000;;;prediction;;;;;;;;;;;;;;;;;;\n" +
		"61662;2027;09;14;prediction;0.150000;0.020000;0.380000;0.020000;;;;;prediction;-0.0400000;0.0250000;;;prediction;;;;;;;;;;;;;;;;;;";

	/// <summary>Two rows with values and no sigmas, which must still parse.</summary>
	private const string NoSigmaCsv = Header +
		"61296;2026;09;13;final;0.194510;;0.331655;;;;;;final;-0.0050574;;;;prediction;;;;;;;;;;;;;;;;;;\n" +
		"61297;2026;09;14;final;0.192933;;0.330554;;;;;;final;-0.0061769;;;;prediction;;;;;;;;;;;;;;;;;;";

	private static JulianDate At(double modifiedJulianDate) =>
		new(2400000.5 + System.Math.Floor(modifiedJulianDate), modifiedJulianDate - System.Math.Floor(modifiedJulianDate));

	[TestMethod]
	public void TheSigmaColumnsAreReadOffTheRow()
	{
		EarthOrientation orientation = EarthOrientationTable.Parse(IersSample.Csv).At(At(61296.0));

		Assert.AreEqual(0.000090, orientation.PoleXSigmaArcseconds, 1e-12);
		Assert.AreEqual(0.000091, orientation.PoleYSigmaArcseconds, 1e-12);
		Assert.AreEqual(0.0000262, orientation.Ut1MinusUtcSigmaSeconds, 1e-15);
	}

	[TestMethod]
	public void BetweenRowsTheLargerNeighbourSigmaIsReported()
	{
		// 61298 is final at 2.42e-5 s and 61302 a forecast at 2.041e-4 s. A blend would report
		// about 1.1e-4 s for a value that is partly forecast; the larger neighbour does not
		// understate it. Midway between two finals it is still the larger of the two.
		EarthOrientationTable table = EarthOrientationTable.Parse(IersSample.Csv);

		Assert.AreEqual(0.0002041, table.At(At(61299.0)).Ut1MinusUtcSigmaSeconds, 1e-15);
		Assert.AreEqual(0.000891, table.At(At(61299.0)).PoleXSigmaArcseconds, 1e-12);
		Assert.AreEqual(0.0000273, table.At(At(61296.5)).Ut1MinusUtcSigmaSeconds, 1e-15);

		// On a row exactly, the row's own sigma: nothing has been blended.
		Assert.AreEqual(0.0000242, table.At(At(61298.0)).Ut1MinusUtcSigmaSeconds, 1e-15);
	}

	[TestMethod]
	public void ARowWithValuesButNoSigmasStillParsesAndReportsTheSigmasAsUnknown()
	{
		EarthOrientationTable table = EarthOrientationTable.Parse(NoSigmaCsv);
		EarthOrientation orientation = table.At(At(61296.5));

		Assert.AreEqual(2, table.Count);
		Assert.AreEqual(-0.0050574, table.At(At(61296.0)).Ut1MinusUtcSeconds, 1e-12);
		Assert.IsTrue(double.IsNaN(orientation.Ut1MinusUtcSigmaSeconds), "A missing sigma is not a sigma of zero.");
		Assert.IsTrue(double.IsNaN(orientation.PoleXSigmaArcseconds));
		Assert.IsTrue(double.IsNaN(orientation.PoleYSigmaArcseconds));
	}

	[TestMethod]
	public void AnUnknownSigmaIsRefusedRatherThanMeasuredAsZero()
	{
		EarthOrientation orientation = EarthOrientationTable.Parse(NoSigmaCsv).At(At(61296.0));

		ArgumentException failure = Assert.ThrowsExactly<ArgumentException>(
			() => EarthOrientationTerm.Measure(Leo, At(61296.0), orientation, Math));

		Assert.Contains("UT1 - UTC", failure.Message);
	}

	[TestMethod]
	public void IgnoringTheOrientationReportsZeroSigmaAndZeroContribution()
	{
		Assert.AreEqual(0.0, EarthOrientation.Ignored.PoleXSigmaArcseconds);
		Assert.AreEqual(0.0, EarthOrientation.Ignored.PoleYSigmaArcseconds);
		Assert.AreEqual(0.0, EarthOrientation.Ignored.Ut1MinusUtcSigmaSeconds);

		IReadOnlyList<EarthOrientationTerm.Contribution> contributions =
			EarthOrientationTerm.Measure(Leo, At(61296.0), EarthOrientation.Ignored, Math);

		Assert.AreEqual(0.0, EarthOrientationTerm.CombineInQuadrature(contributions));
	}

	[TestMethod]
	public void AFinalRowIsWorthAboutACentimetreAtLeo()
	{
		EarthOrientation orientation = EarthOrientationTable.Parse(IersSample.Csv).At(At(61296.0));
		IReadOnlyList<EarthOrientationTerm.Contribution> contributions =
			EarthOrientationTerm.Measure(Leo, At(61296.0), orientation, Math);
		double metres = EarthOrientationTerm.CombineInQuadrature(contributions) * 1000.0;

		Console.WriteLine($"Final row at LEO: {metres:E3} m combined.");

		foreach (EarthOrientationTerm.Contribution c in contributions)
		{
			Console.WriteLine($"  {c.Term,-12} sigma {c.Sigma:E3}  {c.PositionKilometers * 1000.0:E3} m");
		}

		Assert.IsGreaterThan(0.005, metres, $"Expected of order a centimetre, got {metres:E3} m.");
		Assert.IsLessThan(0.05, metres, $"Expected of order a centimetre, got {metres:E3} m.");
		Assert.AreEqual("Ut1MinusUtc", contributions[0].Term,
			"At a final row UT1 - UTC should lead: 2.6e-5 s of rotation is a centimetre, 9e-5 arcseconds of pole is three millimetres.");
	}

	[TestMethod]
	public void TheUt1TermIsTheRotationRateTimesTheDistanceFromTheAxis()
	{
		// Physics rather than this code's own arithmetic: a sigma of UT1 − UTC turns the Earth by
		// the sidereal rate times that sigma, which moves a point by that angle times its distance
		// from the rotation axis. The pole sigmas are zero here, so nothing else contributes.
		//
		// A whole second rather than a realistic sigma, because the sidereal angle is still
		// evaluated from one double Julian date (#51), which resolves about 40 µs; a 25 ms sigma
		// would carry that as 1e-3 of relative error, a one-second one as 4e-5.
		const double sigma = 1.0;
		EarthOrientation orientation = new(0.0, 0.0, 0.0, IsPrediction: true, 0.0, 0.0, sigma);
		IReadOnlyList<EarthOrientationTerm.Contribution> contributions =
			EarthOrientationTerm.Measure(Leo, At(61296.0), orientation, Math);

		double expected = 7.2921158553e-5 * sigma * System.Math.Sqrt((Leo.X * Leo.X) + (Leo.Y * Leo.Y));

		Assert.AreEqual(expected, contributions[0].PositionKilometers, expected * 1e-4);
	}

	[TestMethod]
	public void AForecastAYearOutIsWorthStrictlyMore()
	{
		EarthOrientationTable table = EarthOrientationTable.Parse(YearOutCsv);

		double final = EarthOrientationTerm.CombineInQuadrature(
			EarthOrientationTerm.Measure(Leo, At(61296.0), table.At(At(61296.0)), Math));
		double yearOut = EarthOrientationTerm.CombineInQuadrature(
			EarthOrientationTerm.Measure(Leo, At(61662.0), table.At(At(61662.0)), Math));

		Console.WriteLine($"Final {final * 1000.0:E3} m, a year out {yearOut * 1000.0:E3} m.");

		Assert.IsGreaterThan(final, yearOut, $"A year-out forecast ({yearOut} km) should exceed a final row ({final} km).");
		Assert.IsGreaterThan(5.0, yearOut * 1000.0, $"2.5e-2 s of UT1 - UTC is metres at LEO, got {yearOut * 1000.0:F2} m.");
	}

	[TestMethod]
	public void DroppingTheUt1TermChangesTheResult()
	{
		EarthOrientation orientation = EarthOrientationTable.Parse(IersSample.Csv).At(At(61296.0));
		EarthOrientation withoutUt1 = orientation with { Ut1MinusUtcSigmaSeconds = 0.0 };

		double all = EarthOrientationTerm.CombineInQuadrature(
			EarthOrientationTerm.Measure(Leo, At(61296.0), orientation, Math));
		double poleOnly = EarthOrientationTerm.CombineInQuadrature(
			EarthOrientationTerm.Measure(Leo, At(61296.0), withoutUt1, Math));

		Assert.IsLessThan(all * 0.5, poleOnly,
			$"UT1 - UTC is the larger share at a final row; without it {poleOnly} km against {all} km.");
		Assert.IsGreaterThan(0.0, poleOnly, "The pole terms still contribute.");
	}
}
