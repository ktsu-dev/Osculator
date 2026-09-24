// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Data.Iers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers reading the IERS series and applying polar motion.
/// </summary>
[TestClass]
public sealed class EarthOrientationTests
{
	private static readonly DoubleStorageMath Math = DoubleStorageMath.Instance;

	/// <summary>MJD 61296, one of the sample's final rows.</summary>
	private static JulianDate At(double modifiedJulianDate) =>
		new(2400000.5 + System.Math.Floor(modifiedJulianDate), modifiedJulianDate - System.Math.Floor(modifiedJulianDate));

	[TestMethod]
	public void RowsWithNoValuesAreDroppedRatherThanReadAsZero()
	{
		// The defect the parser exists to prevent. The file ends with rows carrying a date and
		// nothing else, and a zero UT1 − UTC is a legal-looking value that silently costs 415 m.
		EarthOrientationTable table = EarthOrientationTable.Parse(IersSample.Csv);

		Assert.AreEqual(5, table.Count, "Only the rows with values should survive.");
		Assert.AreEqual(61303.0, table.LastModifiedJulianDate,
			"The last usable row is the last prediction, not the last date in the file.");
	}

	[TestMethod]
	public void AnInstantPastTheDataIsRefusedRatherThanExtrapolated()
	{
		EarthOrientationTable table = EarthOrientationTable.Parse(IersSample.Csv);

		// 61722 is a real row in the file — it just has no values in it.
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => table.At(At(61722.0)));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => table.At(At(61000.0)));
	}

	[TestMethod]
	public void AValueOnARowIsReadStraightOff()
	{
		EarthOrientationTable table = EarthOrientationTable.Parse(IersSample.Csv);
		EarthOrientation orientation = table.At(At(61296.0));

		Assert.AreEqual(0.194510, orientation.PoleXArcseconds, 1e-9);
		Assert.AreEqual(0.331655, orientation.PoleYArcseconds, 1e-9);
		Assert.AreEqual(-0.0050574, orientation.Ut1MinusUtcSeconds, 1e-12);
		Assert.IsFalse(orientation.IsPrediction);
	}

	[TestMethod]
	public void BetweenRowsItInterpolates()
	{
		EarthOrientationTable table = EarthOrientationTable.Parse(IersSample.Csv);
		EarthOrientation midday = table.At(At(61296.5));

		Assert.AreEqual((0.194510 + 0.192933) / 2.0, midday.PoleXArcseconds, 1e-9);
		Assert.AreEqual((-0.0050574 + -0.0061769) / 2.0, midday.Ut1MinusUtcSeconds, 1e-12);
	}

	[TestMethod]
	public void BlendingAMeasurementWithAForecastGivesAForecast()
	{
		// 61298 is final and 61302 is a prediction, so anything between them is partly forecast.
		// Reporting that as measured would be the sort of quiet overclaim this flag exists to stop.
		EarthOrientationTable table = EarthOrientationTable.Parse(IersSample.Csv);

		Assert.IsFalse(table.At(At(61298.0)).IsPrediction);
		Assert.IsTrue(table.At(At(61299.0)).IsPrediction);
		Assert.IsTrue(table.At(At(61302.0)).IsPrediction);
	}

	[TestMethod]
	public void SomethingThatIsNotTheIersFileIsRefusedByName()
	{
		FormatException failure = Assert.ThrowsExactly<FormatException>(
			() => EarthOrientationTable.Parse("date,x,y\n2026-09-13,1,2\n"));

		Assert.Contains("MJD;", failure.Message);
	}

	[TestMethod]
	public void PolarMotionPutsTheRotationAxisWhereTheIersSaysItIs()
	{
		// The test that pins the sign convention, and it is the published definition rather than
		// this code's own arithmetic: the pole coordinates ARE the position of the Celestial
		// Intermediate Pole in the ITRS, xp towards Greenwich and yp towards 90° west. So the PEF
		// z axis — which is that pole — has to come out at ITRS (xp, −yp, 1).
		//
		// IERS Technical Note 36 eq 5.3 gives r_TIRS = W r_ITRS with W = R3(−s′) R2(xp) R1(yp),
		// so this direction is r_ITRS = R1(−yp) R2(−xp) r_TIRS, with s′ dropped at a
		// microarcsecond. Getting either sign backwards fails here.
		const double poleX = 0.190054;
		const double poleY = 0.329163;
		EarthOrientation orientation = new(poleX, poleY, 0.0, IsPrediction: false);

		PefState<double> alongTheAxis = new(0.0, 0.0, 1.0, 0.0, 0.0, 0.0);
		ItrfState<double> inItrf = EarthFixedFrame<double>.PefToItrf(alongTheAxis, orientation, Math);

		Assert.AreEqual(Radians(poleX), inItrf.X, 1e-12, "xp towards Greenwich.");
		Assert.AreEqual(-Radians(poleY), inItrf.Y, 1e-12, "yp towards 90° west, so negative y.");
		// Not exactly 1: the exact z is cos(xp)·cos(yp), which is short of 1 by (xp² + yp²)/2,
		// about 1.7e-12. That residual is the second-order term the first-order definition drops,
		// and it is 11 microns at the Earth's radius.
		Assert.AreEqual(1.0, inItrf.Z, 1e-11);
	}

	[TestMethod]
	public void PolarMotionMovesASurfacePointAboutTwelveMetres()
	{
		// The magnitude claim made in the docs, measured. It is the pole offset times the radius,
		// and it lands between the 9 m this layer originally estimated and nothing at all.
		EarthOrientation orientation = new(0.190054, 0.329163, 0.0, IsPrediction: false);
		PefState<double> onTheEquator = new(6378.137, 0.0, 0.0, 0.0, 0.0, 0.0);

		ItrfState<double> shifted = EarthFixedFrame<double>.PefToItrf(onTheEquator, orientation, Math);
		double metres = System.Math.Sqrt(
			System.Math.Pow(shifted.X - onTheEquator.X, 2)
			+ System.Math.Pow(shifted.Y - onTheEquator.Y, 2)
			+ System.Math.Pow(shifted.Z - onTheEquator.Z, 2)) * 1000.0;

		Assert.AreEqual(5.88, metres, 0.5,
			$"A point on the equator at the Greenwich meridian moved {metres:F2} m.");
	}

	[TestMethod]
	public void TheItrfRoundTripReturnsWhatWentIn()
	{
		EarthOrientation orientation = new(0.190054, 0.329163, -0.0086337, IsPrediction: false);
		PefState<double> original = new(4821.7, -3112.4, 5908.2, 3.114, 6.402, -1.877);

		PefState<double> back = EarthFixedFrame<double>.ItrfToPef(
			EarthFixedFrame<double>.PefToItrf(original, orientation, Math), orientation, Math);

		Assert.AreEqual(original.X, back.X, 1e-9);
		Assert.AreEqual(original.Y, back.Y, 1e-9);
		Assert.AreEqual(original.Z, back.Z, 1e-9);
		Assert.AreEqual(original.VelocityX, back.VelocityX, 1e-12);
		Assert.AreEqual(original.VelocityY, back.VelocityY, 1e-12);
		Assert.AreEqual(original.VelocityZ, back.VelocityZ, 1e-12);
	}

	[TestMethod]
	public void IgnoringTheOrientationChangesNothing()
	{
		PefState<double> original = new(4821.7, -3112.4, 5908.2, 3.114, 6.402, -1.877);
		ItrfState<double> itrf = EarthFixedFrame<double>.PefToItrf(original, EarthOrientation.Ignored, Math);

		Assert.AreEqual(original.X, itrf.X, 1e-12);
		Assert.AreEqual(original.Y, itrf.Y, 1e-12);
		Assert.AreEqual(original.Z, itrf.Z, 1e-12);
	}

	private static double Radians(double arcseconds) => arcseconds * System.Math.PI / (180.0 * 3600.0);
}
