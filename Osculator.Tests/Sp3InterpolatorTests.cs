// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Numerics;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Data.Sp3;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Gate 4: SP3 interpolation, validated by holding epochs out of real orbits and interpolating them
/// back.
/// </summary>
/// <remarks>
/// <para>
/// A held-out epoch is the honest test of an interpolator because the right answer is known
/// exactly — it is in the file — and the interpolator is never shown it. Dropping one epoch also
/// doubles the gap the polynomial has to bridge at that point, so the error measured here is an
/// upper bound on the error between two epochs of the complete file, not a sample of it.
/// </para>
/// <para>
/// The bounds come from what this suite measured, quoted beside each one, and they separate the
/// middle of the table from its two ends. Near an end the eleven-point window cannot be centred and
/// the polynomial is evaluated off-centre, which costs accuracy; that is the price of refusing to
/// extrapolate rather than a defect, and it is worth knowing how large it is.
/// </para>
/// </remarks>
[TestClass]
public sealed class Sp3InterpolatorTests
{
	/// <summary>The working precision of the reference arithmetic, in significant digits.</summary>
	private const int ReferenceDigits = 30;

	/// <summary>What holding out every interior epoch of one table measured, in kilometres.</summary>
	/// <param name="Held">How many epochs were held out.</param>
	/// <param name="WorstCentred">The worst error where the window could be centred.</param>
	/// <param name="WorstNearEnd">The worst error within half a window of either end.</param>
	private readonly record struct HeldOut(int Held, double WorstCentred, double WorstNearEnd);

	[TestMethod]
	public void GpsHeldOutEpochsComeBackWithinACentimetre()
	{
		Sp3File file = Sp3Parser.Parse(Sp3ParserTests.Igs);

		foreach (string satellite in file.Satellites)
		{
			HeldOut m = HoldOut(file.Tabulate<double>(satellite), DoubleStorageMath.Instance);

			Console.WriteLine($"{satellite}: {m.Held} held out, worst {m.WorstCentred * 1e6:F3} mm centred, {m.WorstNearEnd * 1e6:F3} mm near an end");

			Assert.AreEqual(94, m.Held);
			Assert.IsLessThan(GpsCentredBoundKm, m.WorstCentred);
			Assert.IsLessThan(GpsNearEndBoundKm, m.WorstNearEnd);
		}
	}

	[TestMethod]
	public void LageosHeldOutEpochsComeBackAtTheFilesOwnResolution()
	{
		Sp3File file = Sp3Parser.Parse(Sp3ParserTests.Lageos);
		HeldOut m = HoldOut(file.Tabulate<double>("L51"), DoubleStorageMath.Instance);

		Console.WriteLine($"L51: {m.Held} held out, worst {m.WorstCentred * 1e6:F4} mm centred, {m.WorstNearEnd * 1e6:F4} mm near an end");

		Assert.AreEqual(29, m.Held);
		Assert.IsLessThan(LageosCentredBoundKm, m.WorstCentred);
		Assert.IsLessThan(LageosNearEndBoundKm, m.WorstNearEnd);
	}

	[TestMethod]
	public void ThirtyDigitsInterpolateTheSameOrbit_SoTheArithmeticIsNotTheLimit()
	{
		Sp3File file = Sp3Parser.Parse(Sp3ParserTests.Igs);
		PreciseStorageMath precise = new(ReferenceDigits);

		HeldOut asDouble = HoldOut(file.Tabulate<double>("G02"), DoubleStorageMath.Instance);
		HeldOut asPrecise = HoldOut(file.Tabulate<PreciseNumber>("G02"), precise);

		Console.WriteLine($"G02 worst centred: double {asDouble.WorstCentred:E6} km, {ReferenceDigits} digits {asPrecise.WorstCentred:E6} km");

		Assert.AreEqual(asDouble.Held, asPrecise.Held);

		// The held-out error is the polynomial's, a few millimetres; the arithmetic underneath it
		// moves that by a few nanometres. More digits do not interpolate an orbit better, which is
		// this repository's central claim again in a second place.
		Assert.AreEqual(asPrecise.WorstCentred, asDouble.WorstCentred, 1e-9);
		Assert.AreEqual(asPrecise.WorstNearEnd, asDouble.WorstNearEnd, 1e-9);
	}

	[TestMethod]
	public void ATenthDegreePolynomialIsReproducedExactly_AndAnEleventhIsNot()
	{
		// This is what pins the order, rather than an accuracy figure that a different order might
		// happen to meet as well. Through eleven points a tenth-order Lagrange polynomial is the
		// unique polynomial of that degree, so it reproduces one exactly; it cannot reproduce one of
		// degree eleven. Evaluated at thirty digits so "exactly" can mean twenty-five of them.
		PreciseStorageMath precise = new(ReferenceDigits);

		double tenth = WorstPolynomialError(10, precise);
		double eleventh = WorstPolynomialError(11, precise);

		Console.WriteLine($"degree 10: {tenth:E3}; degree 11: {eleventh:E3}");

		Assert.IsLessThan(1e-20, tenth);
		Assert.IsGreaterThan(1e-5, eleventh);
	}

	[TestMethod]
	public void ATabulatedInstantReturnsTheTablesOwnValue()
	{
		Sp3File file = Sp3Parser.Parse(Sp3ParserTests.Igs);
		IReadOnlyList<TabulatedPosition<double>> table = file.Tabulate<double>("G05");
		Sp3Interpolator<double> interpolator = new(table, DoubleStorageMath.Instance);

		foreach (TabulatedPosition<double> point in table)
		{
			Assert.AreEqual(point, interpolator.At(point.Seconds));
		}
	}

	[TestMethod]
	public void AnInstantOutsideTheTableIsRefused_NotExtrapolated()
	{
		Sp3File file = Sp3Parser.Parse(Sp3ParserTests.Igs);
		Sp3Interpolator<double> interpolator = new(file.Tabulate<double>("G02"), DoubleStorageMath.Instance);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => interpolator.At(-1.0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => interpolator.At(interpolator.LastSeconds + 1.0));

		// Either end itself is inside.
		_ = interpolator.At(interpolator.FirstSeconds);
		_ = interpolator.At(interpolator.LastSeconds);
	}

	[TestMethod]
	public void ATableThatCannotCarryThePolynomialIsRefused()
	{
		TabulatedPosition<double>[] ten = new TabulatedPosition<double>[10];

		for (int i = 0; i < ten.Length; i++)
		{
			ten[i] = new(i, i, i, i);
		}

		Assert.ThrowsExactly<ArgumentException>(() => new Sp3Interpolator<double>(ten, DoubleStorageMath.Instance));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Sp3Interpolator<double>(ten, DoubleStorageMath.Instance, 0));

		// Nine points is enough for an eighth-order polynomial, and a repeated time is not.
		_ = new Sp3Interpolator<double>(ten, DoubleStorageMath.Instance, 8);
		ten[5] = ten[4];
		Assert.ThrowsExactly<ArgumentException>(() => new Sp3Interpolator<double>(ten, DoubleStorageMath.Instance, 8));
	}

	/// <summary>GPS, fifteen-minute spacing, centred window: measured 6.0 mm worst, on G02.</summary>
	private const double GpsCentredBoundKm = 1e-5;

	/// <summary>GPS within half a window of an end: measured 135 mm worst, on G01.</summary>
	private const double GpsNearEndBoundKm = 3e-4;

	/// <summary>
	/// LAGEOS-1, two-minute spacing, centred window: measured 1.47 mm, against a file that writes
	/// positions to the millimetre. The polynomial is no longer the limit; the last digit is.
	/// </summary>
	private const double LageosCentredBoundKm = 3e-6;

	/// <summary>LAGEOS-1 within half a window of an end: measured 54 mm.</summary>
	private const double LageosNearEndBoundKm = 1.5e-4;

	/// <summary>Drops each interior epoch in turn and interpolates it back from the rest.</summary>
	private static HeldOut HoldOut<T>(IReadOnlyList<TabulatedPosition<T>> table, IStorageMath<T> math)
		where T : struct, INumber<T>
	{
		int half = (Sp3Interpolator<T>.StandardOrder + 1) / 2;
		double worstCentred = 0.0;
		double worstNearEnd = 0.0;
		int held = 0;

		for (int k = 1; k < table.Count - 1; k++)
		{
			List<TabulatedPosition<T>> without = [.. table];
			without.RemoveAt(k);

			TabulatedPosition<T> truth = table[k];
			TabulatedPosition<T> back = new Sp3Interpolator<T>(without, math).At(truth.Seconds);

			double dx = double.CreateChecked(back.X - truth.X);
			double dy = double.CreateChecked(back.Y - truth.Y);
			double dz = double.CreateChecked(back.Z - truth.Z);
			double error = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

			if (k < half || k > table.Count - 1 - half)
			{
				worstNearEnd = Math.Max(worstNearEnd, error);
			}
			else
			{
				worstCentred = Math.Max(worstCentred, error);
			}

			held++;
		}

		return new HeldOut(held, worstCentred, worstNearEnd);
	}

	private static PreciseNumber From(double value) => Convert<PreciseNumber>(value);

	private static T Convert<T>(double value)
		where T : INumberBase<T> => T.CreateChecked(value);

	/// <summary>
	/// Tabulates a polynomial of the given degree at twenty integer seconds and measures how far the
	/// interpolator strays from it between them.
	/// </summary>
	private static double WorstPolynomialError(int degree, PreciseStorageMath math)
	{
		PreciseNumber Evaluate(PreciseNumber t)
		{
			// Roots spread across the table, so the polynomial is not small anywhere it is sampled.
			PreciseNumber value = PreciseNumber.One;

			for (int i = 0; i < degree; i++)
			{
				value *= t - From((i * 1.9) + 0.3);
			}

			return value / From(1e9);
		}

		TabulatedPosition<PreciseNumber>[] table = new TabulatedPosition<PreciseNumber>[20];

		for (int i = 0; i < table.Length; i++)
		{
			PreciseNumber t = From(i);
			PreciseNumber v = Evaluate(t);
			table[i] = new(t, v, v, v);
		}

		Sp3Interpolator<PreciseNumber> interpolator = new(table, math);
		double worst = 0.0;

		for (int i = 0; i < 19; i++)
		{
			PreciseNumber t = From(i + 0.5);
			double error = Math.Abs(double.CreateChecked(interpolator.At(t).X - Evaluate(t)));
			worst = Math.Max(worst, error);
		}

		return worst;
	}
}
