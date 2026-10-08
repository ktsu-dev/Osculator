// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using ktsu.Osculator.Core.Conjunction;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers conjunction screening: the shell pre-filter, the search for closest approaches, and the
/// miss distance as a measurement the storage types disagree on.
/// </summary>
/// <remarks>
/// <para>
/// Two constructed pairs, both built on one station-like orbit, because there is no published
/// conjunction vector to compare against and a constructed pair has an answer that can be reasoned
/// about:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <strong>The crossing pair</strong> shares the orbit's size, shape and inclination but has its
/// node 20° further east, with its phase tuned so that both arrive where the planes cross within
/// metres of each other. They pass at 2.09 km/s, once an orbit, which is a realistic encounter.
/// </description></item>
/// <item><description>
/// <strong>The formation pair</strong> differs only by a hundredth of a degree of inclination. The
/// two never separate by more than about a kilometre and close to metres at every node, so the
/// separation's maxima are inside any useful threshold too — which is what lets a test tell a
/// closest approach from a farthest one.
/// </description></item>
/// </list>
/// </remarks>
[TestClass]
public sealed class ConjunctionScreeningTests
{
	private const int StationId = 90001;

	private const int FormationId = 90002;

	private const int CrossingId = 90003;

	private const int GeostationaryId = 90004;

	private const int DecayingId = 90005;

	private static readonly DateTime Epoch = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

	/// <summary>The formation pair's node crossings in the first three hours, in minutes, to a millisecond-ish.</summary>
	private static readonly double[] FormationNodeTimes = [23.190, 69.732, 116.113, 162.655];

	private static ElementSet Station => Leo(StationId);

	private static ElementSet Formation => Leo(FormationId, inclination: 51.61);

	/// <summary>Gets the crossing object. Its mean anomaly was found by a sweep, to put the crossings at metres.</summary>
	private static ElementSet Crossing => Leo(CrossingId, meanAnomaly: 347.526, raan: 120.0);

	private static ElementSet Geostationary => Leo(GeostationaryId, inclination: 0.05, meanMotion: 1.0027);

	/// <summary>Gets an object low and with a large drag term, so the model gives up on it a few hours in.</summary>
	private static ElementSet Decaying => Leo(DecayingId, meanMotion: 16.3) with { BStar = 0.05 };

	private static ConjunctionScreener<double> Double => ConjunctionScreener.Create(DoubleStorageMath.Instance);

	[TestMethod]
	public void CrossingPair_IsFoundAtEachCrossing()
	{
		IReadOnlyList<ClosestApproach<double>> approaches = Double.FindClosestApproaches(Station, Crossing, Options(120.0, 5.0));

		Assert.AreEqual(2, approaches.Count);
		Assert.AreEqual(1.60848, approaches[0].MinutesFromWindowStart, 1e-5);
		Assert.AreEqual(94.45015, approaches[1].MinutesFromWindowStart, 1e-5);

		// One orbit apart: 15.5 revolutions a day is a 92.9-minute period.
		Assert.AreEqual(1440.0 / 15.5, approaches[1].MinutesFromWindowStart - approaches[0].MinutesFromWindowStart, 0.1);

		foreach (ClosestApproach<double> approach in approaches)
		{
			Assert.AreEqual(0.0134, approach.MissDistanceKm, 0.0005, "The crossings were tuned to about 13 m.");
			Assert.AreEqual(2.088, approach.RelativeSpeedKmPerSecond, 0.001);
			Assert.AreEqual(StationId, approach.PrimaryCatalogId);
			Assert.AreEqual(CrossingId, approach.SecondaryCatalogId);
		}
	}

	[TestMethod]
	public void ReportedTime_IsAMinimumOfTheRange()
	{
		ClosestApproach<double> approach = Double.FindClosestApproaches(Station, Crossing, Options(120.0, 5.0))[0];
		double atApproach = RangeAt(approach.MinutesFromWindowStart);

		// A millisecond either side, at 2 km/s, is two metres along the relative track.
		double millisecond = 1.0 / 60000.0;
		Assert.IsTrue(RangeAt(approach.MinutesFromWindowStart - millisecond) > atApproach);
		Assert.IsTrue(RangeAt(approach.MinutesFromWindowStart + millisecond) > atApproach);
		Assert.AreEqual(atApproach, approach.MissDistanceKm, 1e-15);

		// And the relative position is perpendicular to the relative velocity there, to within what
		// stopping at a nanominute allows: the range rate through a closest approach changes at
		// v²/d, about 330 km/s every second for this pass, so 60 ns leaves around 2e-5 km/s.
		TemeState<double> a = approach.Primary;
		TemeState<double> b = approach.Secondary;
		double rangeRate = (((b.X - a.X) * (b.VelocityX - a.VelocityX))
			+ ((b.Y - a.Y) * (b.VelocityY - a.VelocityY))
			+ ((b.Z - a.Z) * (b.VelocityZ - a.VelocityZ))) / approach.MissDistanceKm;
		Assert.AreEqual(0.0, rangeRate, 1e-4, "The range rate at closest approach, in km/s.");

		static double RangeAt(double minutes)
		{
			Sgp4Result<double> first = Sgp4<double>.Propagate(Sgp4<double>.Initialize(Station, DoubleStorageMath.Instance), minutes, DoubleStorageMath.Instance);
			Sgp4Result<double> second = Sgp4<double>.Propagate(Sgp4<double>.Initialize(Crossing, DoubleStorageMath.Instance), minutes, DoubleStorageMath.Instance);
			double dx = second.State.X - first.State.X;
			double dy = second.State.Y - first.State.Y;
			double dz = second.State.Z - first.State.Z;
			return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
		}
	}

	[TestMethod]
	public void FormationPair_ReportsClosestApproachesAndNotFarthest()
	{
		// The two never separate by more than about a kilometre, so the threshold admits the
		// maxima as well as the minima. Every node is a minimum: one every half orbit.
		IReadOnlyList<ClosestApproach<double>> approaches = Double.FindClosestApproaches(Station, Formation, Options(180.0, 5.0));

		Assert.AreEqual(4, approaches.Count);

		double[] times = [.. approaches.Select(a => Math.Round(a.MinutesFromWindowStart, 3))];
		CollectionAssert.AreEqual(FormationNodeTimes, times);

		// Nodal regression differs with inclination, so the nodes drift apart and each pass is
		// wider than the last: metres, growing.
		Assert.IsTrue(approaches.Zip(approaches.Skip(1)).All(p => p.Second.MissDistanceKm > p.First.MissDistanceKm));
		Assert.IsTrue(approaches.All(a => a.MissDistanceKm < 0.05));
	}

	/// <summary>
	/// The point of the panel, pinned: the same encounter measured in four storage types.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Measured on the first crossing, a 13.24 m miss at 2.09 km/s, against 30 significant digits:
	/// <see langword="float"/> is off by 0.82 m (1.87 m at the second crossing),
	/// <see langword="double"/> by 1.6e-12 km — a nanometre and a half — (8.2e-12 km at the second),
	/// and <see langword="decimal"/> by 3e-26 km (1e-24 km).
	/// </para>
	/// <para>
	/// Note what the cancellation does and does not do. Subtracting two nearly equal values of one
	/// type is exact; what it does is strip away the leading digits the two states share, so that
	/// what remains of the miss distance is the states' own absolute rounding — about a
	/// <see langword="double"/> ulp at seven thousand kilometres, which is where the
	/// <see langword="double"/> figure sits. In <see langword="float"/> that ulp is half a metre and
	/// the propagation has piled more on top, so a metre-scale miss distance is wrong by its own
	/// size. <see langword="double"/> is not: the claim the rest of the repository makes holds here
	/// too, ten orders of magnitude below the element set's own quantization.
	/// </para>
	/// </remarks>
	[TestMethod]
	public void MissDistance_IsMeasuredInTheStorageType()
	{
		ConjunctionScreenOptions options = Options(120.0, 5.0);

		IReadOnlyList<ClosestApproach<float>> single = ConjunctionScreener.Create(FloatStorageMath.Instance).FindClosestApproaches(Station, Crossing, options);
		IReadOnlyList<ClosestApproach<double>> twice = Double.FindClosestApproaches(Station, Crossing, options);
		IReadOnlyList<ClosestApproach<decimal>> money = ConjunctionScreener.Create(DecimalStorageMath.Instance).FindClosestApproaches(Station, Crossing, options);
		IReadOnlyList<ClosestApproach<PreciseNumber>> reference = ConjunctionScreener.Create(new PreciseStorageMath(30)).FindClosestApproaches(Station, Crossing, options);

		Assert.AreEqual(2, single.Count);
		Assert.AreEqual(2, twice.Count);
		Assert.AreEqual(2, money.Count);
		Assert.AreEqual(2, reference.Count);

		for (int i = 0; i < reference.Count; i++)
		{
			PreciseNumber truth = reference[i].MissDistanceKm;

			double floatError = Math.Abs(single[i].MissDistanceKm - double.CreateChecked(truth));
			PreciseNumber doubleError = PreciseNumber.Abs(twice[i].MissDistanceKm.ToPreciseNumber() - truth);
			PreciseNumber decimalError = PreciseNumber.Abs(money[i].MissDistanceKm.ToPreciseNumber() - truth);

			Console.WriteLine($"crossing {i}: float {floatError:E3} km, double {double.CreateChecked(doubleError):E3} km, decimal {double.CreateChecked(decimalError):E3} km");

			Assert.IsTrue(floatError > 1e-4, $"float should disagree in the metre range on a 13 m miss; it was off by {floatError:E3} km.");

			// Above zero first: a difference of exactly zero would mean the reference was not
			// actually computed in more digits, and every bound below would pass vacuously.
			Assert.IsTrue(doubleError > PreciseNumber.Zero, "double agreeing with thirty digits to every digit means the reference is not one.");
			Assert.IsTrue(doubleError < 1e-10.ToPreciseNumber(), $"double was off by {doubleError} km.");

			// Tight enough that differencing states rounded to double would fail it: that leaves
			// a double ulp at 7,000 km, about 1e-12 km, in the answer whatever type made the states.
			Assert.IsTrue(decimalError < 1e-18.ToPreciseNumber(), $"decimal was off by {decimalError} km.");
		}
	}

	[TestMethod]
	public void Shells_RejectOnlyPairsThatCannotMeet()
	{
		ElementSet[] objects = [Station, Formation, Geostationary];
		ConjunctionScreenResult screened = Double.Screen(objects, Options(180.0, 5.0));

		Assert.AreEqual(3, screened.PairCount);
		Assert.AreEqual(1, screened.PairsPropagated, "Only the two low orbits share a shell.");
		Assert.AreEqual(2, screened.PairsRejectedByFilter);
		Assert.AreEqual(4, screened.Conjunctions.Count);

		// With the filter padded out of existence every pair is propagated, and nothing more is
		// found: the filter only ever rejects what could not have been a conjunction.
		ConjunctionScreenResult unfiltered = Double.Screen(objects, Options(180.0, 5.0) with { ShellMarginKm = 1e6 });

		Assert.AreEqual(3, unfiltered.PairsPropagated);
		CollectionAssert.AreEqual(screened.Conjunctions.ToArray(), unfiltered.Conjunctions.ToArray());
	}

	[TestMethod]
	public void Shell_IsTheMeanOrbitsRadialBand()
	{
		OrbitShell station = OrbitShell.Of(Station);
		OrbitShell geostationary = OrbitShell.Of(Geostationary);
		double stationMean = (station.PerigeeRadiusKm + station.ApogeeRadiusKm) / 2.0;

		// 15.5 revolutions a day is a semi-major axis of about 6,795 km; e = 0.0005 adds and
		// subtracts 3.4 km of it.
		Assert.AreEqual(6795.0, stationMean, 5.0);
		Assert.AreEqual(2.0 * 0.0005 * stationMean, station.ApogeeRadiusKm - station.PerigeeRadiusKm, 1e-6);
		Assert.AreEqual(42164.0, geostationary.PerigeeRadiusKm, 25.0);

		Assert.AreEqual(geostationary.PerigeeRadiusKm - station.ApogeeRadiusKm, station.GapTo(geostationary), 1e-9);
		Assert.AreEqual(station.GapTo(geostationary), geostationary.GapTo(station), 1e-9);
		Assert.AreEqual(0.0, station.GapTo(OrbitShell.Of(Formation)));
	}

	[TestMethod]
	public void Screen_OrdersConjunctionsByMissDistance()
	{
		ConjunctionScreenResult result = Double.Screen([Station, Formation, Crossing], Options(180.0, 5.0));

		Assert.IsTrue(result.Conjunctions.Count >= 6, $"Expected the four formation passes and the crossings; found {result.Conjunctions.Count}.");
		Assert.IsTrue(result.Conjunctions.Zip(result.Conjunctions.Skip(1)).All(p => p.First.MissDistanceKm <= p.Second.MissDistanceKm));
		Assert.AreEqual(0, result.Failures.Count);
	}

	[TestMethod]
	public void Window_IsAnInstantNotAnEpochOffset()
	{
		ClosestApproach<double> fromEpoch = Double.FindClosestApproaches(Station, Crossing, Options(120.0, 5.0))[1];

		// Thirty minutes later the same encounter is thirty minutes nearer the start.
		ConjunctionScreenOptions later = Options(120.0, 5.0) with
		{
			WindowStart = JulianDate.FromUtc(Epoch.AddMinutes(30)),
		};
		IReadOnlyList<ClosestApproach<double>> fromLater = Double.FindClosestApproaches(Station, Crossing, later);

		Assert.AreEqual(fromEpoch.MinutesFromWindowStart - 30.0, fromLater[0].MinutesFromWindowStart, 1e-6);
		Assert.AreEqual(fromEpoch.MissDistanceKm, fromLater[0].MissDistanceKm, 1e-9);
	}

	[TestMethod]
	public void Window_EdgeIsNotAnApproach()
	{
		// The first crossing is at 1.6 minutes. A window starting just after it opens with the pair
		// already separating, and that is the window's edge, not a closest approach.
		ConjunctionScreenOptions afterFirst = Options(60.0, 5.0) with
		{
			WindowStart = JulianDate.FromUtc(Epoch.AddMinutes(2)),
		};

		Assert.AreEqual(0, Double.FindClosestApproaches(Station, Crossing, afterFirst).Count);
	}

	[TestMethod]
	public void ObjectsTheModelGivesUpOn_AreReportedNotDropped()
	{
		// A threshold wide enough that the decaying object's shell still overlaps the station's,
		// so the pair is propagated and the failure is met.
		ConjunctionScreenResult result = Double.Screen([Station, Decaying], Options(240.0, 200.0));

		Assert.AreEqual(1, result.PairsPropagated);
		Assert.AreEqual(1, result.Failures.Count);
		Assert.AreEqual(DecayingId, result.Failures[0].CatalogId);
		Assert.AreEqual(Sgp4Error.EccentricityOutOfRange, result.Failures[0].Error);
		Assert.IsTrue(result.Failures[0].MinutesFromWindowStart is > 0.0 and <= 240.0);
	}

	[TestMethod]
	public void Options_AreValidated()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Double.Screen([Station], Options(0.0, 5.0)));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Double.Screen([Station], Options(60.0, -1.0)));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Double.Screen([Station], Options(60.0, 5.0) with { StepMinutes = 0.0 }));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Double.Screen([Station], Options(60.0, 5.0) with { TimeToleranceMinutes = double.NaN }));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Double.Screen([Station], Options(60.0, 5.0) with { ShellMarginKm = double.PositiveInfinity }));
	}

	private static ConjunctionScreenOptions Options(double windowMinutes, double thresholdKm) => new()
	{
		WindowStart = JulianDate.FromUtc(Epoch),
		WindowMinutes = windowMinutes,
		ThresholdKm = thresholdKm,
	};

	/// <summary>
	/// A station-like orbit: near-circular, about 420 km up, at 51.6°.
	/// </summary>
	private static ElementSet Leo(int catalogId, double inclination = 51.6, double meanMotion = 15.5, double meanAnomaly = 0.0, double raan = 100.0) => new()
	{
		ObjectName = $"TEST {catalogId}",
		ObjectId = "2026-001A",
		NoradCatalogId = catalogId,
		Epoch = Epoch,
		EpochJulianDate = JulianDate.FromUtc(Epoch),
		MeanMotion = meanMotion,
		Eccentricity = 0.0005,
		Inclination = inclination,
		RightAscensionOfAscendingNode = raan,
		ArgumentOfPericenter = 90.0,
		MeanAnomaly = meanAnomaly,
		BStar = 0.0001,
	};
}
