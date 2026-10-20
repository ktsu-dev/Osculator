// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Numerics;
using ktsu.Osculator.Core.Forces;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Data.Forces;
using ktsu.Osculator.Data.Horizons;
using ktsu.Osculator.Numerics.Precise;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Horizons' ICRF Sun and Moon rotated into TEME, the frame an SGP4 state and so a Cowell
/// integration started from one are in.
/// </summary>
/// <remarks>
/// <para>
/// The expected rotation is not read back from <see cref="GcrfFrame{T}"/>: it is the IAU 1976
/// general precession in longitude, <c>p_A = 5029.0966″ t + 1.11113″ t²</c> (Lieske et al. 1977),
/// which is how far the mean equinox has moved along the ecliptic since J2000. A body's ecliptic
/// longitude measured from the equinox of date is larger than its J2000 longitude by exactly that,
/// to within nutation, which tilts TEME's true equator by Δε and so moves the computed longitude by
/// at most about ten arcseconds. For the committed table that is 1307″, 0.363°.
/// </para>
/// </remarks>
[TestClass]
public sealed class ThirdBodyTemeTests
{
	private const double ArcsecondsToRadians = Math.PI / (180.0 * 3600.0);

	[TestMethod]
	public void TheMoonInTeme_IsTheIcrfMoonPrecessedSinceJ2000()
	{
		HorizonsEphemeris table = HorizonsVectorTable.Parse(HorizonsSample.MoonJson);
		JulianDate epoch = table.Vectors[0].Epoch;
		TabulatedEphemeris<double> teme = HorizonsBodyEphemeris.CreateInTeme(table, epoch, HorizonsTimeScale.Tdb, DoubleStorageMath.Instance, points: 4);

		foreach (HorizonsStateVector row in table.Vectors)
		{
			BodyPosition<double> p = teme.PositionAt(SecondsSince(epoch, row.Epoch));
			double t = (row.Epoch.Day - 2451545.0 + row.Epoch.DayFraction) / 36525.0;
			double precession = ((5029.0966 * t) + (1.11113 * t * t)) * ArcsecondsToRadians;

			double rawRadius = Math.Sqrt((row.X * row.X) + (row.Y * row.Y) + (row.Z * row.Z));
			double temeRadius = Math.Sqrt((p.X * p.X) + (p.Y * p.Y) + (p.Z * p.Z));
			Assert.AreEqual(rawRadius, temeRadius, rawRadius * 1e-14, "a rotation keeps the distance");

			double angle = Math.Acos(((row.X * p.X) + (row.Y * p.Y) + (row.Z * p.Z)) / (rawRadius * temeRadius));
			double degrees = angle * 180.0 / Math.PI;
			Console.WriteLine($"{row.CalendarDate}: rotated {degrees:F4}°, p_A {precession * 180.0 / Math.PI:F4}°");
			Assert.IsTrue(degrees is > 0.3 and < 0.4, $"about a third of a degree in 2026; read {degrees:F4}°");

			// The sign and the size together: the longitude of date runs ahead of J2000's by p_A.
			double meanObliquityJ2000 = 84381.448 * ArcsecondsToRadians;
			double meanObliquityOfDate = (84381.448 - (46.8150 * t)) * ArcsecondsToRadians;
			double shift = EclipticLongitude(p.X, p.Y, p.Z, meanObliquityOfDate) - EclipticLongitude(row.X, row.Y, row.Z, meanObliquityJ2000);
			Assert.AreEqual(precession, shift, 20.0 * ArcsecondsToRadians, $"longitude shift {shift / ArcsecondsToRadians:F1}″ against p_A {precession / ArcsecondsToRadians:F1}″");
		}
	}

	[TestMethod]
	public void ARotatedRow_RoundTripsThroughTheGcrf()
	{
		HorizonsEphemeris table = HorizonsVectorTable.Parse(HorizonsSample.MoonJson);
		JulianDate epoch = table.Vectors[0].Epoch;

		RoundTrip(table, epoch, DoubleStorageMath.Instance, 1e-15);
		RoundTrip(table, epoch, new PreciseStorageMath(30), 1e-27);
	}

	[TestMethod]
	public void EveryStorageType_RotatesTheSameMoon()
	{
		HorizonsEphemeris table = HorizonsVectorTable.Parse(HorizonsSample.MoonJson);
		JulianDate epoch = table.Vectors[0].Epoch;
		double[][] reference = Rotated(table, epoch, new PreciseStorageMath(30));

		Compare("double", reference, Rotated(table, epoch, DoubleStorageMath.Instance), 1e-14);
		Compare("decimal", reference, Rotated(table, epoch, DecimalStorageMath.Instance), 1e-14);
		Compare("float", reference, Rotated(table, epoch, FloatStorageMath.Instance), 1e-6);
	}

	[TestMethod]
	public void TheCelestialPoleOffsets_ReachTheRotation()
	{
		HorizonsEphemeris table = HorizonsVectorTable.Parse(HorizonsSample.MoonJson);
		JulianDate epoch = table.Vectors[0].Epoch;
		BodyPosition<double> ignored = HorizonsBodyEphemeris.CreateInTeme(table, epoch, HorizonsTimeScale.Tdb, DoubleStorageMath.Instance, points: 4).PositionAt(0.0);
		BodyPosition<double> offset = HorizonsBodyEphemeris.CreateInTeme(table, epoch, HorizonsTimeScale.Tdb, new CelestialPoleOffsets(-0.1, -0.01), DoubleStorageMath.Instance, points: 4).PositionAt(0.0);

		double moved = Math.Sqrt(Square(offset.X - ignored.X) + Square(offset.Y - ignored.Y) + Square(offset.Z - ignored.Z));
		double radius = Math.Sqrt(Square(ignored.X) + Square(ignored.Y) + Square(ignored.Z));

		// TEME keeps the mean equinox, so Δψ reaches it only by tilting the true equator, by Δψ sin ε
		// at most, about 0.04″ here; Δε adds a hundredth. Ignoring them is a few tens of metres at the Moon.
		double arcseconds = moved / radius / ArcsecondsToRadians;
		Console.WriteLine($"offsets moved the Moon {moved:E3} km, {arcseconds:F4}″");
		Assert.IsTrue(arcseconds is > 0.01 and < 0.11, $"moved {arcseconds:F4}″");
	}

	[TestMethod]
	public void AUtTable_IsRefused()
	{
		HorizonsEphemeris table = HorizonsVectorTable.Parse(HorizonsSample.MoonJson) with { TimeScale = HorizonsTimeScale.Ut };
		Assert.ThrowsExactly<ArgumentException>(() =>
			HorizonsBodyEphemeris.CreateInTeme(table, table.Vectors[0].Epoch, HorizonsTimeScale.Ut, DoubleStorageMath.Instance, points: 4));
	}

	[TestMethod]
	public void ATableInAnotherFrame_IsRefused()
	{
		HorizonsEphemeris table = HorizonsVectorTable.Parse(HorizonsSample.MoonJson) with { ReferenceFrame = "FK4" };
		Assert.ThrowsExactly<ArgumentException>(() =>
			HorizonsBodyEphemeris.CreateInTeme(table, table.Vectors[0].Epoch, HorizonsTimeScale.Tdb, DoubleStorageMath.Instance, points: 4));
	}

	[TestMethod]
	public void AnEpochInAnotherTimeScale_IsRefused()
	{
		HorizonsEphemeris table = HorizonsVectorTable.Parse(HorizonsSample.MoonJson);
		Assert.ThrowsExactly<ArgumentException>(() =>
			HorizonsBodyEphemeris.CreateInTeme(table, table.Vectors[0].Epoch, HorizonsTimeScale.Tt, DoubleStorageMath.Instance, points: 4));
	}

	private static void RoundTrip<T>(HorizonsEphemeris table, JulianDate epoch, IStorageMath<T> math, double relative)
		where T : struct, INumber<T>
	{
		TabulatedEphemeris<T> teme = HorizonsBodyEphemeris.CreateInTeme(table, epoch, HorizonsTimeScale.Tdb, math, points: 4);
		foreach (HorizonsStateVector row in table.Vectors)
		{
			BodyPosition<T> p = teme.PositionAt(T.CreateChecked(SecondsSince(epoch, row.Epoch)));
			TemeState<T> start = new(p.X, p.Y, p.Z, T.Zero, T.Zero, T.Zero);
			GcrfState<T> gcrf = GcrfFrame<T>.FromTeme(start, row.Epoch, CelestialPoleOffsets.Ignored, math);
			TemeState<T> back = GcrfFrame<T>.ToTeme(gcrf, row.Epoch, CelestialPoleOffsets.Ignored, math);

			double radius = Math.Sqrt(Square(row.X) + Square(row.Y) + Square(row.Z));
			double tripError = Math.Sqrt(Square(Diff(back.X, start.X)) + Square(Diff(back.Y, start.Y)) + Square(Diff(back.Z, start.Z))) / radius;
			double icrfError = Math.Sqrt(Square(double.CreateChecked(gcrf.X) - row.X) + Square(double.CreateChecked(gcrf.Y) - row.Y) + Square(double.CreateChecked(gcrf.Z) - row.Z)) / radius;
			Console.WriteLine($"{typeof(T).Name,-14} {row.CalendarDate}: round trip {tripError:E2}, back to the row {icrfError:E2}");
			Assert.IsLessThanOrEqualTo(relative, tripError, $"{typeof(T).Name}: TEME → GCRF → TEME");

			// The way back lands on the Horizons row itself, to the row's double digits.
			Assert.IsLessThanOrEqualTo(1e-15, icrfError, $"{typeof(T).Name}: TEME → GCRF recovers the ICRF row");
		}
	}

	private static double[][] Rotated<T>(HorizonsEphemeris table, JulianDate epoch, IStorageMath<T> math)
		where T : struct, INumber<T>
	{
		TabulatedEphemeris<T> teme = HorizonsBodyEphemeris.CreateInTeme(table, epoch, HorizonsTimeScale.Tdb, math, points: 4);
		List<double[]> rows = [];
		foreach (HorizonsStateVector row in table.Vectors)
		{
			BodyPosition<T> p = teme.PositionAt(T.CreateChecked(SecondsSince(epoch, row.Epoch)));
			rows.Add([double.CreateChecked(p.X), double.CreateChecked(p.Y), double.CreateChecked(p.Z)]);
		}

		return [.. rows];
	}

	private static void Compare(string type, double[][] reference, double[][] probe, double bound)
	{
		for (int i = 0; i < reference.Length; i++)
		{
			double[] r = reference[i];
			double[] p = probe[i];
			double error = Math.Sqrt(Square(p[0] - r[0]) + Square(p[1] - r[1]) + Square(p[2] - r[2])) / Math.Sqrt(Square(r[0]) + Square(r[1]) + Square(r[2]));
			Console.WriteLine($"{type,-8} row {i}: relative {error:E2}");
			Assert.IsLessThanOrEqualTo(bound, error, $"{type}, row {i}");
		}
	}

	private static double EclipticLongitude(double x, double y, double z, double obliquity)
	{
		double yEcliptic = (y * Math.Cos(obliquity)) + (z * Math.Sin(obliquity));
		return Math.Atan2(yEcliptic, x);
	}

	private static double SecondsSince(JulianDate epoch, JulianDate instant) =>
		(instant.Day - epoch.Day + (instant.DayFraction - epoch.DayFraction)) * 86400.0;

	private static double Diff<T>(T a, T b)
		where T : struct, INumber<T> => double.CreateChecked(a - b);

	private static double Square(double v) => v * v;
}
