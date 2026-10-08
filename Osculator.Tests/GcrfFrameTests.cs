// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Numerics;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Numerics.Precise;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// TEME to GCRF through IAU 1976 precession and IAU 1980 nutation, against the worked example of
/// Vallado, Crawford, Hujsak and Kelso, <em>Revisiting Spacetrack Report #3</em>,
/// AIAA 2006-6753 Rev 2.
/// </summary>
/// <remarks>
/// <para>
/// The example takes one TEME state to true of date, mean of date and J2000. Each step is asserted
/// on its own, so a failure says which of the three rotations is wrong rather than only that the
/// end result is. The J2000 vector is also the GCRF of Vallado's <em>Fundamentals</em> Example
/// 3-15, the same satellite reached from the ITRF, which is what makes it a reference for the
/// whole chain rather than for one routine.
/// </para>
/// <para>
/// Every expected vector was reproduced independently, before this code existed, with ERFA's
/// <c>eraPmat76</c>, <c>eraNut80</c> and <c>eraObl80</c>: to 6e-8 km on position and to the
/// last printed digit on velocity. That evaluation also settled the two choices the paper leaves
/// implicit. Its numbers include the IERS celestial pole offsets, and the equation of the
/// equinoxes with its 1994 kinematic terms; drop the offsets and the J2000 vector misses by
/// 9e-4 km, drop the kinematic terms and it misses by 7e-5 km: a thousand and a hundred times the
/// tolerance.
/// </para>
/// </remarks>
[TestClass]
public sealed class GcrfFrameTests
{
	private static readonly DoubleStorageMath Math = DoubleStorageMath.Instance;

	// AIAA 2006-6753 Rev 2, Appendix C: the example satellite in TEME.
	private static readonly TemeState<double> ValladoTeme = new(
		5094.18016210, 6127.64465950, 6380.34453270,
		-4.746131487, 0.785818041, 5.531931288);

	// The IERS celestial pole offsets for 2004 April 6, relative to IAU 1976/1980.
	private static readonly CelestialPoleOffsets ValladoOffsets = new(-0.052195, -0.003875);

	/// <summary>
	/// 2004 April 6, 07:51:28.386009 UTC, on the TT scale: TAI − UTC was 32 s, and TT is TAI plus
	/// 32.184 s. Kept as two parts so the fraction is not rounded against the day.
	/// </summary>
	private static readonly JulianDate ValladoTerrestrialTime = new(
		2453101.5, ((7 * 3600) + (51 * 60) + 28.386009 + 32.0 + 32.184) / 86400.0);

	[TestMethod]
	public void TemeToTrueOfDateMatchesTheValladoExample()
	{
		PrecessionNutationAngles angles = PrecessionNutation.At(ValladoTerrestrialTime, ValladoOffsets, EquationOfEquinoxes.Iau1994);
		(double x, double y, double z) = GcrfFrame<double>.TemeToTrueOfDate(angles, Math).Apply(ValladoTeme.X, ValladoTeme.Y, ValladoTeme.Z, Math);

		// A rotation about the pole, so z must come through bit for bit.
		AssertVector("TOD position", (5094.51620300, 6127.36527840, 6380.34453270), (x, y, z), 1e-7);
		Assert.AreEqual(ValladoTeme.Z, z, "The equation of the equinoxes turns about z and must not touch it.");
	}

	[TestMethod]
	public void TrueOfDateToMeanOfDateMatchesTheValladoExample()
	{
		PrecessionNutationAngles angles = PrecessionNutation.At(ValladoTerrestrialTime, ValladoOffsets, EquationOfEquinoxes.Iau1994);

		// From the paper's own TOD vector, so the nutation step is judged on its own.
		(double x, double y, double z) = GcrfFrame<double>.TrueOfDateToMeanOfDate(angles, Math).Apply(5094.51620300, 6127.36527840, 6380.34453270, Math);

		AssertVector("MOD position", (5094.02837450, 6127.87081640, 6380.24851640), (x, y, z), 1e-7);
	}

	[TestMethod]
	public void MeanOfDateToJ2000MatchesTheValladoExample()
	{
		PrecessionNutationAngles angles = PrecessionNutation.At(ValladoTerrestrialTime, ValladoOffsets, EquationOfEquinoxes.Iau1994);
		(double x, double y, double z) = GcrfFrame<double>.MeanOfDateToGcrf(angles, Math).Apply(5094.02837450, 6127.87081640, 6380.24851640, Math);

		AssertVector("J2000 position", (5102.50895790, 6123.01140070, 6378.13692820), (x, y, z), 1e-7);
	}

	[TestMethod]
	public void TemeToGcrfMatchesTheValladoExampleEndToEnd()
	{
		GcrfState<double> gcrf = GcrfFrame<double>.FromTeme(ValladoTeme, ValladoTerrestrialTime, ValladoOffsets, Math);

		AssertVector("GCRF position", (5102.50895790, 6123.01140070, 6378.13692820), (gcrf.X, gcrf.Y, gcrf.Z), 1e-7);

		// Nine decimals printed. The last one of z is 5.5337557277 to ten, which the paper and this
		// code round to opposite sides, so the bound is half a unit wider than the last digit.
		AssertVector("GCRF velocity", (-4.743220157, 0.790536497, 5.533755727), (gcrf.VelocityX, gcrf.VelocityY, gcrf.VelocityZ), 1.5e-9);
	}

	[TestMethod]
	[DataRow(2444239.5, -3.789501118318688e-05, -4.262465467771138e-05, DisplayName = "1980")]
	[DataRow(2451545.0, -6.748832172101004e-05, -2.8017362348243993e-05, DisplayName = "J2000")]
	[DataRow(2453101.5, -5.949329432884831e-05, 3.547798544658484e-05, DisplayName = "2004")]
	[DataRow(2461320.5, 4.1632929474479626e-05, 4.0176917375353636e-05, DisplayName = "2026")]
	[DataRow(2469807.5, 7.35027788557133e-05, -2.5890686978049402e-05, DisplayName = "2050")]
	public void NutationMatchesErfaAcrossACentury(double day, double expectedDeltaPsi, double expectedDeltaEpsilon)
	{
		// ERFA's eraNut80 at a quarter past each day, in radians. A transcription slip in one of the
		// 106 terms moves these by its own amplitude, 1e-10 rad for the smallest, so a bound of
		// 1e-14 rad catches any of them on the dates where that term is not near a zero.
		PrecessionNutationAngles angles = PrecessionNutation.At(new JulianDate(day, 0.25), CelestialPoleOffsets.Ignored, EquationOfEquinoxes.Geometric);

		Assert.AreEqual(expectedDeltaPsi, angles.DeltaPsi, 1e-14, "Δψ");
		Assert.AreEqual(expectedDeltaEpsilon, angles.DeltaEpsilon, 1e-14, "Δε");
	}

	[TestMethod]
	public void TheKinematicTermsAreTheWholeDifferenceBetweenTheTwoEquations()
	{
		GcrfState<double> full = GcrfFrame<double>.FromTeme(ValladoTeme, ValladoTerrestrialTime, ValladoOffsets, EquationOfEquinoxes.Iau1994, Math);
		GcrfState<double> geometric = GcrfFrame<double>.FromTeme(ValladoTeme, ValladoTerrestrialTime, ValladoOffsets, EquationOfEquinoxes.Geometric, Math);

		// The kinematic terms rotate about the true pole by 0.00264" sin Ω + 0.000063" sin 2Ω, so
		// they move the state by that angle times its distance from the pole.
		double t = (ValladoTerrestrialTime.Day - 2451545.0 + ValladoTerrestrialTime.DayFraction) / 36525.0;
		double omega = (125.04452222 + (((((0.008 * t) + 7.455) * t) - 6962890.5390) * t / 3600.0)) * System.Math.PI / 180.0;
		double kinematic = ((0.00264 * System.Math.Sin(omega)) + (0.000063 * System.Math.Sin(2.0 * omega))) * System.Math.PI / 648000.0;
		double offAxis = System.Math.Sqrt((ValladoTeme.X * ValladoTeme.X) + (ValladoTeme.Y * ValladoTeme.Y));

		double separation = Distance(full, geometric);
		Console.WriteLine($"Kinematic terms: {kinematic * 648000.0 / System.Math.PI * 1e3:F3} mas, moving the state {separation * 1e5:F2} cm");

		Assert.AreEqual(kinematic * offAxis, separation, 1e-9, "The two equations differ by exactly the kinematic rotation.");

		// And dropping them is what misses the paper: far outside the 1e-7 km it is matched to.
		Assert.IsGreaterThan(5e-5, System.Math.Abs(geometric.X - 5102.50895790), "The geometric equation alone should not reproduce the paper.");
	}

	[TestMethod]
	public void IgnoringThePoleOffsetsLandsOnFk5RatherThanTheGcrf()
	{
		GcrfState<double> gcrf = GcrfFrame<double>.FromTeme(ValladoTeme, ValladoTerrestrialTime, ValladoOffsets, Math);
		GcrfState<double> fk5 = GcrfFrame<double>.FromTeme(ValladoTeme, ValladoTerrestrialTime, CelestialPoleOffsets.Ignored, Math);

		double separation = Distance(gcrf, fk5);
		Console.WriteLine($"FK5 J2000 vs GCRF for the example: {separation * 1e5:F1} cm");

		// 52 mas in longitude and 4 mas in obliquity, at 10,200 km: 91 cm, measured
		// rather than projected, and asserted only to sit in that decade.
		Assert.IsGreaterThan(0.1e-3, separation);
		Assert.IsLessThan(1.0e-3, separation);
	}

	[TestMethod]
	public void TemeIsNotJ2000AndHowFarApartTheyAreDependsOnTheEpoch()
	{
		// The spec says treating TEME as J2000 costs "100 m to several km depending on epoch".
		// Measured, it is that only within a few years of 2000: precession turns TEME away from
		// J2000 at 50" a year, so by now the gap is tens of kilometres at LEO and growing. Same
		// state, same offsets, four epochs.
		(int Year, double Day)[] epochs = [(1980, 2444239.5), (2000, 2451544.5), (2004, 2453101.5), (2026, 2461320.5)];
		double[] separations = new double[epochs.Length];
		for (int i = 0; i < epochs.Length; i++)
		{
			JulianDate tt = new(epochs[i].Day, 0.5);
			GcrfState<double> gcrf = GcrfFrame<double>.FromTeme(ValladoTeme, tt, CelestialPoleOffsets.Ignored, Math);
			separations[i] = System.Math.Sqrt(
				((gcrf.X - ValladoTeme.X) * (gcrf.X - ValladoTeme.X))
				+ ((gcrf.Y - ValladoTeme.Y) * (gcrf.Y - ValladoTeme.Y))
				+ ((gcrf.Z - ValladoTeme.Z) * (gcrf.Z - ValladoTeme.Z)));
			Console.WriteLine($"{epochs[i].Year}: TEME vs J2000 {separations[i]:F3} km");
		}

		// Near J2000 itself only nutation separates them, which is hundreds of metres.
		Assert.IsLessThan(1.0, separations[1], "At J2000 the frames differ by nutation alone.");

		// Today it is precession, and it is not kilometres but tens of them.
		Assert.IsGreaterThan(50.0, separations[3], "In 2026 the frames are tens of kilometres apart at this radius.");
	}

	[TestMethod]
	public void RoundTripReturnsTheStateInEveryStorageType()
	{
		(double doublePosition, double doubleVelocity) = RoundTrip(DoubleStorageMath.Instance);
		(double floatPosition, double floatVelocity) = RoundTrip(FloatStorageMath.Instance);
		(double decimalPosition, double decimalVelocity) = RoundTrip(DecimalStorageMath.Instance);
		(double precisePosition, double preciseVelocity) = RoundTrip(PreciseStorageMath.Instance);

		Console.WriteLine($"Round trip, position / velocity: double {doublePosition:E2} km / {doubleVelocity:E2} km/s, float {floatPosition:E2} / {floatVelocity:E2}, decimal {decimalPosition:E2} / {decimalVelocity:E2}, PreciseNumber {precisePosition:E2} / {preciseVelocity:E2}");

		// About ten units in the last place of a 10,000 km component, in each type's own digits.
		Assert.IsLessThan(1e-11, doublePosition);
		Assert.IsLessThan(1e-14, doubleVelocity);
		Assert.IsLessThan(1e-2, floatPosition);
		Assert.IsLessThan(1e-5, floatVelocity);
		Assert.IsLessThan(1e-20, decimalPosition);
		Assert.IsLessThan(1e-20, decimalVelocity);
		Assert.IsLessThan(1e-20, precisePosition);
		Assert.IsLessThan(1e-20, preciseVelocity);
	}

	[TestMethod]
	public void EveryStorageTypeAgreesWithDouble()
	{
		GcrfState<double> reference = GcrfFrame<double>.FromTeme(ValladoTeme, ValladoTerrestrialTime, ValladoOffsets, Math);

		double floatGap = GapFromDouble(FloatStorageMath.Instance, reference);
		double decimalGap = GapFromDouble(DecimalStorageMath.Instance, reference);
		double preciseGap = GapFromDouble(PreciseStorageMath.Instance, reference);
		Console.WriteLine($"Position vs double: float {floatGap:E2} km, decimal {decimalGap:E2} km, PreciseNumber {preciseGap:E2} km");

		// The angles are evaluated in double for every type, so the wider types agree with double
		// to double's own rounding of the matrix and no further.
		Assert.IsLessThan(1e-2, floatGap);
		Assert.IsLessThan(1e-11, decimalGap);
		Assert.IsLessThan(1e-11, preciseGap);
	}

	private static (double Position, double Velocity) RoundTrip<T>(IStorageMath<T> math)
		where T : struct, INumber<T>
	{
		TemeState<T> teme = new(
			T.CreateChecked(ValladoTeme.X), T.CreateChecked(ValladoTeme.Y), T.CreateChecked(ValladoTeme.Z),
			T.CreateChecked(ValladoTeme.VelocityX), T.CreateChecked(ValladoTeme.VelocityY), T.CreateChecked(ValladoTeme.VelocityZ));

		GcrfState<T> gcrf = GcrfFrame<T>.FromTeme(teme, ValladoTerrestrialTime, ValladoOffsets, math);
		TemeState<T> back = GcrfFrame<T>.ToTeme(gcrf, ValladoTerrestrialTime, ValladoOffsets, math);

		double position = Norm(back.X - teme.X, back.Y - teme.Y, back.Z - teme.Z);
		double velocity = Norm(back.VelocityX - teme.VelocityX, back.VelocityY - teme.VelocityY, back.VelocityZ - teme.VelocityZ);
		return (position, velocity);
	}

	private static double GapFromDouble<T>(IStorageMath<T> math, GcrfState<double> reference)
		where T : struct, INumber<T>
	{
		TemeState<T> teme = new(
			T.CreateChecked(ValladoTeme.X), T.CreateChecked(ValladoTeme.Y), T.CreateChecked(ValladoTeme.Z),
			T.CreateChecked(ValladoTeme.VelocityX), T.CreateChecked(ValladoTeme.VelocityY), T.CreateChecked(ValladoTeme.VelocityZ));
		GcrfState<T> gcrf = GcrfFrame<T>.FromTeme(teme, ValladoTerrestrialTime, ValladoOffsets, math);

		return System.Math.Sqrt(
			Square(double.CreateChecked(gcrf.X) - reference.X)
			+ Square(double.CreateChecked(gcrf.Y) - reference.Y)
			+ Square(double.CreateChecked(gcrf.Z) - reference.Z));
	}

	private static double Norm<T>(T x, T y, T z)
		where T : struct, INumber<T> =>
		System.Math.Sqrt(double.CreateChecked((x * x) + (y * y) + (z * z)));

	private static double Square(double value) => value * value;

	private static double Distance(GcrfState<double> a, GcrfState<double> b) =>
		System.Math.Sqrt(Square(a.X - b.X) + Square(a.Y - b.Y) + Square(a.Z - b.Z));

	private static void AssertVector(string what, (double X, double Y, double Z) expected, (double X, double Y, double Z) actual, double tolerance)
	{
		Console.WriteLine(
			$"{what}: {actual.X - expected.X:E2}, {actual.Y - expected.Y:E2}, {actual.Z - expected.Z:E2} from the paper");
		Assert.AreEqual(expected.X, actual.X, tolerance, $"{what} x");
		Assert.AreEqual(expected.Y, actual.Y, tolerance, $"{what} y");
		Assert.AreEqual(expected.Z, actual.Z, tolerance, $"{what} z");
	}
}
