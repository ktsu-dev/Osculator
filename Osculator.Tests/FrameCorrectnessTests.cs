// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Gate 3, and the two precision defects found on the way to it: the sidereal angle's Julian-date
/// staircase, and the geodetic solve that could not converge at high working precision.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FrameTests"/> checks the transform against physics. This checks it against a
/// published number, which is what catches what physics cannot: a unit slip in the pole
/// coordinates, GMST in place of GAST, or the polar-motion rotations applied in the wrong order
/// all leave a geostationary satellite where it should be.
/// </para>
/// <para>
/// The reference is the worked example in Appendix C of Vallado, Crawford, Hujsak and Kelso,
/// <em>Revisiting Spacetrack Report #3</em>, AIAA 2006-6753, Revision 2 — earlier revisions of
/// the paper print different numbers. The PEF and ITRF steps are asserted separately, so a failure
/// says whether it is the Earth's rotation or polar motion that is wrong.
/// </para>
/// </remarks>
[TestClass]
public sealed class FrameCorrectnessTests
{
	private static readonly DoubleStorageMath Math = DoubleStorageMath.Instance;

	// AIAA 2006-6753 Rev 2, Appendix C. TEME state of the example satellite at its epoch, and the
	// Earth orientation published for that day.
	private static readonly TemeState<double> ValladoTeme = new(
		5094.18016210, 6127.64465950, 6380.34453270,
		-4.746131487, 0.785818041, 5.531931288);

	private static readonly JulianDate ValladoEpoch = JulianDate.FromCalendar(2004, 4, 6, 7, 51, 28.386009);

	private static readonly EarthOrientation ValladoOrientation = new(
		PoleXArcseconds: -0.140682,
		PoleYArcseconds: 0.333309,
		Ut1MinusUtcSeconds: -0.4399619,
		IsPrediction: false);

	private static readonly (double X, double Y, double Z) ValladoPefPosition = (-1033.4750313, 7901.3055856, 6380.3445327);

	private static readonly (double X, double Y, double Z) ValladoPefVelocity = (-3.225632747, -2.872442511, 5.531931288);

	/// <summary>
	/// The UT1 instant the paper's vectors were computed at, formed the way its reference program
	/// forms it: one double, <c>jday(...) + dut1 / 86400</c>. That is not the instant the paper
	/// names; see <see cref="ThePapersOwnJulianDateIsRoundedByFifteenMicroseconds"/>.
	/// </summary>
	private static readonly JulianDate ValladoReferenceInstant = new(
		(367.0 * 2004) - System.Math.Floor(7.0 * (2004 + System.Math.Floor((4 + 9.0) / 12.0)) * 0.25)
			+ System.Math.Floor(275.0 * 4 / 9.0) + 6 + 1721013.5
			+ (((((28.386009 / 60.0) + 51.0) / 60.0) + 7.0) / 24.0)
			+ (-0.4399619 / 86400.0),
		0.0);

	[TestMethod]
	public void TemeToPefMatchesTheValladoExample()
	{
		PefState<double> pef = EarthFixedFrame<double>.ToPef(ValladoTeme, ValladoReferenceInstant, EarthOrientation.Ignored, Math);

		// The paper prints seven decimals of a kilometre, so 1e-7 km is its last digit.
		AssertVector("PEF position", ValladoPefPosition, (pef.X, pef.Y, pef.Z), 1e-7);

		// Nine decimals of a km/s are printed. The paper's Earth rotation rate is reduced by the
		// day's excess length of day, 1.5563 ms, which this transform does not carry; that is
		// 1.8e-8 of omega x r, about 1e-8 km/s here, so the bound is 2e-8 rather than 1e-9.
		AssertVector("PEF velocity", ValladoPefVelocity, (pef.VelocityX, pef.VelocityY, pef.VelocityZ), 2e-8);
	}

	[TestMethod]
	public void ThePapersOwnJulianDateIsRoundedByFifteenMicroseconds()
	{
		// The reference program forms UT1 as one double, so its instant sits 14.7 µs from the one
		// the paper names: issue #51's staircase, inside the published reference. Taken from the
		// exact two-part instant instead, the PEF vector lands 8.5 mm away, and
		// lands there because of exactly that rotation, which is what this asserts. A transform
		// that agreed with the paper to the last digit from the exact instant would be wrong.
		PefState<double> exact = EarthFixedFrame<double>.ToPef(ValladoTeme, ValladoEpoch, ValladoOrientation, Math);
		PefState<double> paper = EarthFixedFrame<double>.ToPef(ValladoTeme, ValladoReferenceInstant, EarthOrientation.Ignored, Math);

		double roundingSeconds = (ValladoReferenceInstant.Day - ValladoEpoch.Day - ValladoEpoch.DayFraction
			- (ValladoOrientation.Ut1MinusUtcSeconds / 86400.0)) * 86400.0;
		double offAxis = System.Math.Sqrt((exact.X * exact.X) + (exact.Y * exact.Y));
		double predictedKm = offAxis * EarthFixedFrame<double>.RotationRateRadiansPerSecond * System.Math.Abs(roundingSeconds);
		double separationKm = System.Math.Sqrt(
			((exact.X - paper.X) * (exact.X - paper.X)) + ((exact.Y - paper.Y) * (exact.Y - paper.Y)));

		Console.WriteLine($"Reference instant rounded by {roundingSeconds * 1e6:F2} µs: predicted {predictedKm * 1e6:F1} mm, measured {separationKm * 1e6:F1} mm");
		Assert.AreEqual(14.69e-6, roundingSeconds, 0.05e-6, "How far the paper's single-double UT1 sits from the instant it names.");
		Assert.AreEqual(predictedKm, separationKm, 2e-8, "The whole gap from the exact instant is that rotation.");
	}

	[TestMethod]
	public void PefToItrfMatchesTheValladoExample()
	{
		// Starting from the paper's own PEF vector rather than this code's, so the polar-motion
		// step is judged on its own and an error in the rotation cannot leak into it.
		PefState<double> pef = new(
			ValladoPefPosition.X, ValladoPefPosition.Y, ValladoPefPosition.Z,
			ValladoPefVelocity.X, ValladoPefVelocity.Y, ValladoPefVelocity.Z);

		ItrfState<double> itrf = EarthFixedFrame<double>.PefToItrf(pef, ValladoOrientation, Math);

		AssertVector("ITRF position", (-1033.4793830, 7901.2952754, 6380.3565958), (itrf.X, itrf.Y, itrf.Z), 1e-7);
		AssertVector("ITRF velocity", (-3.225636520, -2.872451450, 5.531924446), (itrf.VelocityX, itrf.VelocityY, itrf.VelocityZ), 2e-9);
	}

	[TestMethod]
	public void TemeToItrfMatchesTheValladoExampleEndToEnd()
	{
		// The pole coordinates are passed, UT1 - UTC is not: it is already inside the reference
		// instant, which is UT1 formed the way the paper's program forms it.
		EarthOrientation poleOnly = ValladoOrientation with { Ut1MinusUtcSeconds = 0.0 };
		ItrfState<double> itrf = EarthFixedFrame<double>.ToItrf(ValladoTeme, ValladoReferenceInstant, poleOnly, Math);

		AssertVector("ITRF position", (-1033.4793830, 7901.2952754, 6380.3565958), (itrf.X, itrf.Y, itrf.Z), 1e-7);
		AssertVector("ITRF velocity", (-3.225636520, -2.872451450, 5.531924446), (itrf.VelocityX, itrf.VelocityY, itrf.VelocityZ), 2e-8);
	}

	[TestMethod]
	public void SiderealAngleRisesAtEveryMicrosecond()
	{
		// One 40 µs tread of a single-double Julian date near 2.46e6, sampled five times over. The
		// old code returned 6 distinct angles across these 201 instants.
		double previous = double.NegativeInfinity;

		for (int k = 0; k <= 200; k++)
		{
			JulianDate instant = new(2461312.5, 0.3 + (k * 1e-6 / 86400.0));
			double angle = EarthFixedFrame<double>.SiderealAngle(instant, 0.0, Math);

			Assert.IsGreaterThan(previous, angle, $"GMST did not rise between {k - 1} µs and {k} µs.");
			previous = angle;
		}
	}

	[TestMethod]
	public void SiderealAngleAgreesWithAFiftyDigitTwoPartEvaluation()
	{
		double worst = 0.0;

		foreach (JulianDate instant in SiderealInstants())
		{
			double angle = EarthFixedFrame<double>.SiderealAngle(instant, 0.0, Math);
			double reference = PreciseSiderealAngle(instant);

			double difference = System.Math.Abs(angle - reference);
			difference = System.Math.Min(difference, (2.0 * System.Math.PI) - difference);
			worst = System.Math.Max(worst, difference);
		}

		Console.WriteLine($"Worst GMST difference from a 50-digit two-part evaluation: {worst:E3} rad");
		Assert.IsLessThan(1e-12, worst, $"GMST is {worst:E3} rad from the two-part polynomial.");
	}

	[TestMethod]
	[DataRow(30)]
	[DataRow(80)]
	[DataRow(100)]
	[DataRow(300)]
	public void GeodeticConvergesAtHighWorkingPrecision(int digits)
	{
		// The case from the issue: 45° geocentric, 700 km up. At 80 digits the fixed-point solve
		// needed 32 steps against a ceiling of 30 and threw.
		PreciseStorageMath precise = new(digits);
		double radius = 7078.137;
		double component = radius * System.Math.Sqrt(0.5);

		GeodeticPosition<double> expected = Geodetic<double>.FromEarthFixed(
			new ItrfState<double>(component, 0.0, component, 0.0, 0.0, 0.0), Math);
		GeodeticPosition<PreciseNumber> actual = Geodetic<PreciseNumber>.FromEarthFixed(
			new ItrfState<PreciseNumber>(P(component), PreciseNumber.Zero, P(component), PreciseNumber.Zero, PreciseNumber.Zero, PreciseNumber.Zero),
			precise);

		Assert.AreEqual(expected.LatitudeRadians, actual.LatitudeRadians.To<double>(), 1e-15, "latitude");
		Assert.AreEqual(expected.AltitudeKilometers, actual.AltitudeKilometers.To<double>(), 1e-10, "altitude");
	}

	[TestMethod]
	public void GeodeticConvergesForRandomPositionsAtEightyDigits()
	{
		// Six of forty of these threw before. Any latitude, anywhere from the surface to beyond GEO.
		PreciseStorageMath precise = new(80);
		Random random = new(20261007);

		for (int i = 0; i < 40; i++)
		{
			double latitude = (random.NextDouble() - 0.5) * System.Math.PI;
			double longitude = (random.NextDouble() - 0.5) * 2.0 * System.Math.PI;
			double radius = 6378.137 + (random.NextDouble() * 40000.0);
			double x = radius * System.Math.Cos(latitude) * System.Math.Cos(longitude);
			double y = radius * System.Math.Cos(latitude) * System.Math.Sin(longitude);
			double z = radius * System.Math.Sin(latitude);

			GeodeticPosition<double> expected = Geodetic<double>.FromEarthFixed(new ItrfState<double>(x, y, z, 0.0, 0.0, 0.0), Math);
			GeodeticPosition<PreciseNumber> actual = Geodetic<PreciseNumber>.FromEarthFixed(
				new ItrfState<PreciseNumber>(P(x), P(y), P(z), PreciseNumber.Zero, PreciseNumber.Zero, PreciseNumber.Zero),
				precise);

			Assert.AreEqual(expected.LatitudeRadians, actual.LatitudeRadians.To<double>(), 1e-14, $"latitude, case {i}");
		}
	}

	[TestMethod]
	public void GeodeticStillConvergesInFloat()
	{
		// The narrowest type is where a convergence test on exact equality is most likely to spin,
		// because its last bit is the one that flips.
		Random random = new(7);

		for (int i = 0; i < 20000; i++)
		{
			double latitude = (random.NextDouble() - 0.5) * System.Math.PI;
			double longitude = (random.NextDouble() - 0.5) * 2.0 * System.Math.PI;
			double radius = 6378.137 + (random.NextDouble() * 40000.0);
			float x = (float)(radius * System.Math.Cos(latitude) * System.Math.Cos(longitude));
			float y = (float)(radius * System.Math.Cos(latitude) * System.Math.Sin(longitude));
			float z = (float)(radius * System.Math.Sin(latitude));

			GeodeticPosition<float> position = Geodetic<float>.FromEarthFixed(
				new ItrfState<float>(x, y, z, 0f, 0f, 0f), FloatStorageMath.Instance);
			GeodeticPosition<double> expected = Geodetic<double>.FromEarthFixed(
				new ItrfState<double>(x, y, z, 0.0, 0.0, 0.0), Math);

			Assert.AreEqual(expected.LatitudeRadians, position.LatitudeRadians, 1e-5, $"latitude, case {i}");
		}
	}

	private static IEnumerable<JulianDate> SiderealInstants()
	{
		for (int k = 0; k <= 200; k += 7)
		{
			yield return new JulianDate(2461312.5, 0.3 + (k * 1e-6 / 86400.0));
		}

		yield return ValladoEpoch;
		yield return new JulianDate(2451545.0, 0.0);
		yield return new JulianDate(2433281.5, 0.123456789);
		yield return new JulianDate(2469807.5, 0.987654321);
		yield return new JulianDate(2461312.5, -0.25);
		yield return new JulianDate(2461312.5, 1.75);
	}

	/// <summary>
	/// The 1982 GMST polynomial, every term included, from the sum of the two parts formed exactly.
	/// </summary>
	/// <param name="instant">The instant, taken as UT1.</param>
	/// <returns>The angle in [0, 2π), in radians.</returns>
	private static double PreciseSiderealAngle(JulianDate instant)
	{
		PreciseNumber twoPi = PreciseNumber.Pi * P(2.0);
		PreciseNumber centuries = (P(instant.Day) - P(2451545.0) + P(instant.DayFraction)) / P(36525.0);
		PreciseNumber seconds = (P(-6.2e-6) * centuries * centuries * centuries)
			+ (P(0.093104) * centuries * centuries)
			+ ((P(876600.0 * 3600.0) + P(8640184.812866)) * centuries)
			+ P(67310.54841);

		PreciseNumber radians = seconds * PreciseNumber.Pi / P(180.0 * 240.0);
		radians %= twoPi;

		if (radians < PreciseNumber.Zero)
		{
			radians += twoPi;
		}

		return radians.To<double>();
	}

	private static PreciseNumber P(double value) => value.ToPreciseNumber();

	private static void AssertVector(string what, (double X, double Y, double Z) expected, (double X, double Y, double Z) actual, double tolerance)
	{
		Console.WriteLine(
			$"{what}: {actual.X - expected.X:E2}, {actual.Y - expected.Y:E2}, {actual.Z - expected.Z:E2} from the paper");
		Assert.AreEqual(expected.X, actual.X, tolerance, $"{what} x");
		Assert.AreEqual(expected.Y, actual.Y, tolerance, $"{what} y");
		Assert.AreEqual(expected.Z, actual.Z, tolerance, $"{what} z");
	}
}
