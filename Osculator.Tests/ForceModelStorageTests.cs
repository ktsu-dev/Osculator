// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Numerics;
using ktsu.Osculator.Core.Forces;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Every force in every storage type, against the same force at thirty digits.
/// </summary>
/// <remarks>
/// <para>
/// Each storage type evaluates the same forces at the same state, built from the same
/// <see langword="double"/> inputs so that only the arithmetic differs. That is the property the
/// round-off measurements need from the force model, and the failure it guards against is the
/// quiet one: a <see langword="decimal"/> that underflows a 1e-15 density to a handful of digits,
/// or a <see langword="float"/> that overflows the unnormalized coefficients a degree-70 field would
/// need, and returns a confident number either way.
/// </para>
/// <para>
/// The bounds are what each type measured with room to spare; the figures are printed. The
/// thirty-digit run is the reference, so its own row is a check of the harness: it has to agree with
/// itself to every digit.
/// </para>
/// </remarks>
[TestClass]
public sealed class ForceModelStorageTests
{
	private static readonly string[] ForceNames = ["harmonics 20×20", "drag", "radiation pressure", "Moon", "Sun"];

	[TestMethod]
	public void EveryForce_RunsInEveryStorageType()
	{
		double[][] reference = Evaluate(new PreciseStorageMath(30));
		double[][] again = Evaluate(new PreciseStorageMath(30));
		Report("PreciseNumber (rerun)", reference, again, 0.0);

		Report("double", reference, Evaluate(DoubleStorageMath.Instance), 1e-13);
		Report("decimal", reference, Evaluate(DecimalStorageMath.Instance), 1e-13);
		Report("float", reference, Evaluate(FloatStorageMath.Instance), 1e-5);
	}

	private static void Report(string type, double[][] reference, double[][] probe, double bound)
	{
		for (int i = 0; i < ForceNames.Length; i++)
		{
			double[] r = reference[i];
			double[] p = probe[i];
			double magnitude = Math.Sqrt((r[0] * r[0]) + (r[1] * r[1]) + (r[2] * r[2]));
			Assert.IsGreaterThan(0.0, magnitude, $"{ForceNames[i]} evaluated to nothing");
			double error = Math.Sqrt(((p[0] - r[0]) * (p[0] - r[0])) + ((p[1] - r[1]) * (p[1] - r[1])) + ((p[2] - r[2]) * (p[2] - r[2]))) / magnitude;
			Console.WriteLine($"{type,-22} {ForceNames[i],-20} relative {error:E2}");
			Assert.IsLessThanOrEqualTo(bound, error, $"{type}, {ForceNames[i]}");
		}
	}

	private static double[][] Evaluate<T>(IStorageMath<T> math)
		where T : struct, INumber<T>
	{
		// An ISS-like state at 400 km, an hour into a table that runs for two days.
		CartesianState<T> state = new(C<T>(-1234.5), C<T>(5678.9), C<T>(3210.1), C<T>(-6.1), C<T>(-2.2), C<T>(3.5));
		T t = C<T>(3600.0 * 7.5);

		GravityField field = Egm96.Load(20);
		EarthRotation<T> rotation = new(C<T>(1.2345), EarthRotation<T>.At(new Core.Time.JulianDate(2461041.5, 0.0), 0.0, math).RateRadiansPerSecond);
		TabulatedEphemeris<T> moon = Circular<T>(384400.0, 27.321661, 0.3, math);
		TabulatedEphemeris<T> sun = Circular<T>(149597870.7, 365.256363, 0.4, math);

		IForceModel<T>[] forces =
		[
			new SphericalHarmonicGravity<T>(field, 20, 20, rotation, math),
			new AtmosphericDrag<T>(new ExponentialAtmosphere<T>(math), C<T>(0.01), rotation.RateRadiansPerSecond, math),
			new SolarRadiationPressure<T>(sun, C<T>(0.02), math),
			new ThirdBodyGravity<T>(ThirdBodyGravity<T>.MoonGravitationalParameter, moon, math),
			new ThirdBodyGravity<T>(ThirdBodyGravity<T>.SunGravitationalParameter, sun, math),
		];

		double[][] results = new double[forces.Length][];
		for (int i = 0; i < forces.Length; i++)
		{
			CartesianAcceleration<T> a = forces[i].Acceleration(t, state);
			results[i] = [double.CreateChecked(a.X), double.CreateChecked(a.Y), double.CreateChecked(a.Z)];
		}

		return results;
	}

	private static TabulatedEphemeris<T> Circular<T>(double radius, double periodDays, double tilt, IStorageMath<T> math)
		where T : struct, INumber<T>
	{
		double rate = 2.0 * Math.PI / (periodDays * 86400.0);
		List<T> times = [];
		List<BodyPosition<T>> samples = [];
		for (int hour = 0; hour <= 48; hour++)
		{
			double t = hour * 3600.0;
			double x = radius * Math.Cos(rate * t);
			double y = radius * Math.Sin(rate * t);
			times.Add(C<T>(t));
			samples.Add(new(C<T>(x), C<T>(y * Math.Cos(tilt)), C<T>(y * Math.Sin(tilt))));
		}

		return new TabulatedEphemeris<T>(times, samples, math);
	}

	private static T C<T>(double value)
		where T : struct, INumber<T> =>
		typeof(T) == typeof(PreciseNumber) ? (T)(object)value.ToPreciseNumber() : T.CreateChecked(value);
}
