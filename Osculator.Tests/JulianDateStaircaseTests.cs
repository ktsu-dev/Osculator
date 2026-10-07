// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Numerics.Precise;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// The Julian date staircase: what one <see langword="double"/> does to a continuous sweep of time,
/// and what the two-part form and a type with enough digits do instead.
/// </summary>
/// <remarks>
/// Every sweep here is 400 microseconds long at 5-microsecond spacing, starting ten minutes past the
/// ISS epoch: long enough to cross about ten treads, short enough that the 30-digit run stays under a
/// second.
/// </remarks>
[TestClass]
public sealed class JulianDateStaircaseTests
{
	/// <summary>The same ISS element set the parser tests use.</summary>
	private const string IssLine1 = "1 25544U 98067A   26258.88499338  .00006292  00000+0  12172-3 0  9999";

	/// <summary>The second line of the same element set.</summary>
	private const string IssLine2 = "2 25544  51.6310 211.2092 0004923 144.3133 215.8185 15.49128922585812";

	/// <summary>The first requested instant, in seconds since epoch.</summary>
	private const double StartSeconds = 600.0;

	/// <summary>The length of every sweep, in seconds.</summary>
	private const double SpanSeconds = 400e-6;

	/// <summary>The number of samples, so that they fall 5 microseconds apart.</summary>
	private const int Samples = 81;

	/// <summary>The working precision of the reference arithmetic, in significant digits.</summary>
	private const int PreciseDigits = 30;

	private static readonly ElementSet Iss = TleParser.Parse(IssLine1, IssLine2, "ISS (ZARYA)");

	[TestMethod]
	public void TheTreadIsTwoToTheMinusThirtyOneDays_NotFortyEightMicroseconds()
	{
		double tread = JulianDateStaircase.TreadSeconds(Iss.EpochJulianDate);
		double julianDate = Iss.EpochJulianDate.Day + Iss.EpochJulianDate.DayFraction;

		double quoted = Math.Pow(2, -52) * julianDate * 86400.0;

		Console.WriteLine($"JD {julianDate:F8}: tread {tread * 1e6:F4} µs; epsilon x JD gives {quoted * 1e6:F4} µs");

		// A present-day Julian date lies between 2^21 and 2^22, so its last place is worth 2^-31 days.
		Assert.IsTrue(julianDate is > 2097152.0 and < 4194304.0, "The epoch is expected in [2^21, 2^22).");
		Assert.AreEqual(86400.0 / 2147483648.0, tread, 0.0);
		Assert.AreEqual(40.23e-6, tread, 0.01e-6);

		// The figure usually quoted is machine epsilon times the date, which is a bound on the
		// spacing rather than the spacing, and overstates it by JD / 2^21.
		Assert.AreEqual(47.2e-6, quoted, 0.1e-6);
		Assert.AreEqual(julianDate / 2097152.0, quoted / tread, 1e-12);
	}

	[TestMethod]
	public void OneDoubleJulianDate_HandsThePropagatorAStaircaseOfInstants()
	{
		IReadOnlyList<JulianDateStaircaseSample> sweep = DoubleSweep(JulianDateMode.SingleValue);
		double tread = JulianDateStaircase.TreadSeconds(Iss.EpochJulianDate);

		double[] instants = [.. sweep.Select(s => s.PropagatedSeconds).Distinct()];
		Console.WriteLine($"{Samples} requests reached the propagator as {instants.Length} distinct instants");

		// 400 µs over a 40.2 µs tread is ten treads, so ten or eleven distinct instants depending on
		// where the first request lands — against 81 requested.
		Assert.IsTrue(instants.Length is 10 or 11, $"Expected 10 or 11 distinct instants, saw {instants.Length}.");

		// Every step between them is exactly one tread: the instants are adjacent doubles.
		for (int i = 1; i < instants.Length; i++)
		{
			Assert.AreEqual(tread, instants[i] - instants[i - 1], 1e-12, $"Step {i} is not one tread.");
		}

		// And no request is further from the instant it got than one tread.
		foreach (JulianDateStaircaseSample sample in sweep)
		{
			Assert.IsLessThanOrEqualTo(tread, Math.Abs(sample.PropagatedSeconds - sample.RequestedSeconds));
		}
	}

	[TestMethod]
	public void OneDoubleJulianDate_IsFlatBetweenRisersOfThirtyCentimetres()
	{
		IReadOnlyList<JulianDateStaircaseSample> sweep = DoubleSweep(JulianDateMode.SingleValue);
		double tread = JulianDateStaircase.TreadSeconds(Iss.EpochJulianDate);

		List<double> risers = [];
		int flats = 0;

		for (int i = 1; i < sweep.Count; i++)
		{
			double rise = sweep[i].AlongTrackMeters - sweep[i - 1].AlongTrackMeters;

			if (sweep[i].PropagatedSeconds == sweep[i - 1].PropagatedSeconds)
			{
				// The same instant, so bit-for-bit the same state.
				Assert.AreEqual(0.0, rise, 0.0, $"Sample {i} moved without its instant moving.");
				flats++;
			}
			else
			{
				risers.Add(rise);
			}
		}

		Console.WriteLine($"{flats} flat steps, {risers.Count} risers of {risers.Min():F4} to {risers.Max():F4} m");

		// Each riser is the along-track distance covered in one tread, at about 7.66 km/s.
		foreach (double riser in risers)
		{
			Assert.AreEqual(7660.0 * tread, riser, 0.01);
		}

		Assert.IsGreaterThan(risers.Count * 5, flats, "The curve is expected to be mostly treads.");
	}

	[TestMethod]
	public void TheTwoPartForm_IsALine()
	{
		IReadOnlyList<JulianDateStaircaseSample> sweep = DoubleSweep(JulianDateMode.TwoPart);

		AssertStrictlyIncreasing(sweep);

		// The fraction of a day is below one, where a double resolves about ten picoseconds.
		foreach (JulianDateStaircaseSample sample in sweep)
		{
			Assert.AreEqual(sample.RequestedSeconds, sample.PropagatedSeconds, 1e-9);
		}
	}

	[TestMethod]
	public void OnePreciseNumberJulianDate_IsALine_AndTheSameLineAsTheTwoPartForm()
	{
		PreciseStorageMath precise = new(PreciseDigits);
		IReadOnlyList<JulianDateStaircaseSample> sweep = JulianDateStaircase.Sweep(Iss, StartSeconds, SpanSeconds, Samples, JulianDateMode.SingleValue, precise);
		IReadOnlyList<JulianDateStaircaseSample> twoPart = DoubleSweep(JulianDateMode.TwoPart);

		AssertStrictlyIncreasing(sweep);

		double worst = 0.0;

		for (int i = 0; i < sweep.Count; i++)
		{
			Assert.AreEqual(sweep[i].RequestedSeconds, sweep[i].PropagatedSeconds, 1e-12);
			worst = Math.Max(worst, Math.Abs(sweep[i].AlongTrackMeters - twoPart[i].AlongTrackMeters));
		}

		Console.WriteLine($"PreciseNumber single value vs double two-part: worst {worst:E3} m");

		// The hack and the exact date agree to well under a micrometre (measured 5.8e-8 m): the two-part form is a
		// complete fix for the time term, which is why everyone uses it.
		Assert.IsLessThan(1e-6, worst);
	}

	[TestMethod]
	public void OneFloatJulianDate_CannotMoveAtAll()
	{
		// A float carries 24 bits, and the last of them on a date of 2.46 million is a quarter of a
		// day. Every request in the sweep — every request within three hours of it — is the epoch.
		IReadOnlyList<JulianDateStaircaseSample> sweep = JulianDateStaircase.Sweep(Iss, StartSeconds, SpanSeconds, Samples, JulianDateMode.SingleValue, FloatStorageMath.Instance);

		Assert.IsTrue(sweep.All(s => s.PropagatedSeconds == 0.0), "Every request is expected to collapse onto the epoch.");
	}

	[TestMethod]
	public void Sweep_RejectsADegenerateSpanOrSampleCount()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => JulianDateStaircase.Sweep(Iss, 0.0, 0.0, 10, JulianDateMode.TwoPart, DoubleStorageMath.Instance));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => JulianDateStaircase.Sweep(Iss, 0.0, double.NaN, 10, JulianDateMode.TwoPart, DoubleStorageMath.Instance));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => JulianDateStaircase.Sweep(Iss, 0.0, 1.0, 1, JulianDateMode.TwoPart, DoubleStorageMath.Instance));
	}

	private static IReadOnlyList<JulianDateStaircaseSample> DoubleSweep(JulianDateMode mode) =>
		JulianDateStaircase.Sweep(Iss, StartSeconds, SpanSeconds, Samples, mode, DoubleStorageMath.Instance);

	private static void AssertStrictlyIncreasing(IReadOnlyList<JulianDateStaircaseSample> sweep)
	{
		for (int i = 1; i < sweep.Count; i++)
		{
			Assert.IsGreaterThan(sweep[i - 1].AlongTrackMeters, sweep[i].AlongTrackMeters, $"Sample {i} did not advance.");
		}
	}
}
