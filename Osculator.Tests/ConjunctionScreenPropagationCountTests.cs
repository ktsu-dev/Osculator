// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ktsu.Osculator.Core.Conjunction;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the screen propagating each object once per grid sample rather than once per pair
/// (issue #126), and that doing so changes nothing it reports.
/// </summary>
/// <remarks>
/// <para>
/// "Nothing changes" is checked against the pair-by-pair screen itself rather than against
/// recorded numbers: <see cref="Reference"/> runs every surviving pair alone and gathers the results
/// the way the screen did before #126 — approaches in pair order then sorted by miss distance,
/// failures first-come in pair order. Every comparison is exact equality of the records, not a
/// tolerance, because the propagations are the same calls at the same instants.
/// </para>
/// <para>
/// The group is twelve station-like objects spread around one shell, so nearly every pair survives
/// the pre-filter and the cached scan has something to save; one object the model gives up on a
/// few hours in, so failure reporting is exercised mid-window; and one geostationary object that
/// the pre-filter rejects against all of them, so it is never propagated at all.
/// </para>
/// </remarks>
[TestClass]
public sealed class ConjunctionScreenPropagationCountTests
{
	private const int GeostationaryId = 91100;

	private const int DecayingId = 91101;

	private static readonly DateTime Epoch = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

	private static ElementSet[] Group =>
	[
		.. Enumerable.Range(0, 12).Select(k => Leo(91000 + k, raan: 100.0 + (7.0 * k), meanAnomaly: 29.0 * k)),
		Leo(DecayingId, meanMotion: 16.3) with { BStar = 0.05 },
		Leo(GeostationaryId, inclination: 0.05, meanMotion: 1.0027),
	];

	[TestMethod]
	public void Screen_PropagatesEachObjectAtMostOncePerSample()
	{
		ConjunctionScreenOptions options = Options();
		ConjunctionScreener<double> screener = ConjunctionScreener.Create(DoubleStorageMath.Instance);
		ElementSet[] group = Group;

		ConjunctionScreenResult result = screener.Screen(group, options);

		int samples = SampleCount(options);
		long pairsTimesSamples = result.PairsPropagated * 2L * samples;

		Assert.IsTrue(result.PairsPropagated >= 66, $"only {result.PairsPropagated} pairs survived; the group is meant to share a shell");
		Assert.IsTrue(result.Conjunctions.Count > 0, "the group is meant to have approaches to refine");

		// The geostationary object meets nobody's shell, so it is never propagated: thirteen objects, not fourteen.
		Assert.IsTrue(screener.ScanPropagations <= 13L * samples, $"{screener.ScanPropagations} scan propagations for 13 objects over {samples} samples");
		Assert.IsTrue(screener.ScanPropagations > 12L * samples, "every station-like object is propagated over the whole grid");
		Assert.IsTrue(screener.RefinePropagations > 0, "refinement still propagates afresh");

		Console.WriteLine($"{result.PairsPropagated} pairs over {samples} samples: {screener.ScanPropagations} scan propagations, against {pairsTimesSamples} pair by pair ({(double)pairsTimesSamples / screener.ScanPropagations:F1}x); {screener.RefinePropagations} in refinement");
	}

	[TestMethod]
	public void Screen_ReportsExactlyWhatThePairByPairScreenDid()
	{
		ConjunctionScreenOptions options = Options();
		ElementSet[] group = Group;

		ConjunctionScreenResult cached = ConjunctionScreener.Create(DoubleStorageMath.Instance).Screen(group, options);
		ConjunctionScreenResult reference = Reference(group, options);

		AssertSame(reference, cached);
		Assert.AreEqual(1, cached.Failures.Count(f => f.CatalogId == DecayingId), "a failing object is reported once");
	}

	[TestMethod]
	public void Screen_GivesTheSameAnswerWhenTheObjectsDoNotFitAtOnce()
	{
		ConjunctionScreenOptions options = Options();
		ElementSet[] group = Group;
		int samples = SampleCount(options);

		ConjunctionScreener<double> roomy = ConjunctionScreener.Create(DoubleStorageMath.Instance);
		ConjunctionScreener<double> blocked = ConjunctionScreener.Create(DoubleStorageMath.Instance);
		ConjunctionScreener<double> single = ConjunctionScreener.Create(DoubleStorageMath.Instance);

		ConjunctionScreenResult all = roomy.Screen(group, options);

		// Blocks of three objects, and blocks of one: the smallest the budget allows.
		ConjunctionScreenResult inBlocks = blocked.Screen(group, options with { MaximumCachedStates = 6 * samples });
		ConjunctionScreenResult oneByOne = single.Screen(group, options with { MaximumCachedStates = 1 });

		AssertSame(all, inBlocks);
		AssertSame(all, oneByOne);

		// Holding less costs propagations, and never more than the pair-by-pair screen made.
		Assert.IsTrue(blocked.ScanPropagations > roomy.ScanPropagations);
		Assert.IsTrue(single.ScanPropagations >= blocked.ScanPropagations);
		Assert.IsTrue(single.ScanPropagations <= all.PairsPropagated * 2L * samples);
	}

	[TestMethod]
	public void ScreenAgainst_PropagatesThePrimaryOnce_AndAgreesWithThePairByPairScreen()
	{
		ConjunctionScreenOptions options = Options();
		ElementSet[] group = Group;
		ElementSet primary = group[0];
		ConjunctionScreener<double> screener = ConjunctionScreener.Create(DoubleStorageMath.Instance);

		ConjunctionScreenResult result = screener.ScreenAgainst(primary, group, options, null, CancellationToken.None);

		// The primary is in the catalogue too, and is skipped there rather than paired with itself.
		Assert.AreEqual(group.Length - 1, result.PairCount);
		Assert.IsTrue(screener.ScanPropagations <= 13L * SampleCount(options));

		ConjunctionScreenResult reference = Reference([primary, .. group.Skip(1)], options, onlyFirst: true);

		AssertSame(reference, result);
	}

	[TestMethod]
	public void Screen_ReportsProgressToTheEnd_AndStopsWhenCancelled()
	{
		ConjunctionScreenOptions options = Options();
		ElementSet[] group = Group;
		ConjunctionScreener<double> screener = ConjunctionScreener.Create(DoubleStorageMath.Instance);
		ConcurrentBag<ConjunctionScreenProgress> reports = [];

		ConjunctionScreenResult result = screener.Screen(group, options, new Recorder(reports.Add), CancellationToken.None);

		Assert.IsTrue(reports.All(r => r.PairsToScan == result.PairsPropagated));
		Assert.AreEqual(result.PairsPropagated + 1, reports.Count, "once before the first pair and once after each");
		Assert.AreEqual(result.PairsPropagated, reports.Max(r => r.PairsScanned));

		using CancellationTokenSource cancelled = new();
		cancelled.Cancel();

		Assert.ThrowsExactly<OperationCanceledException>(() => screener.Screen(group, options, null, cancelled.Token));
	}

	[TestMethod]
	public void Options_RefuseAnEmptyStateBudget() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => (Options() with { MaximumCachedStates = 0 }).Validate());

	/// <summary>
	/// The screen as it ran before #126: every surviving pair screened on its own, gathered in pair order.
	/// </summary>
	private static ConjunctionScreenResult Reference(ElementSet[] objects, ConjunctionScreenOptions options, bool onlyFirst = false)
	{
		ConjunctionScreener<double> screener = ConjunctionScreener.Create(DoubleStorageMath.Instance);
		Dictionary<int, ScreeningFailure> failures = [];
		List<ClosestApproach<double>> approaches = [];
		long pairCount = 0;
		long survivors = 0;

		foreach (ElementSet item in objects)
		{
			Sgp4Satellite<double> satellite = Sgp4<double>.Initialize(item, DoubleStorageMath.Instance);

			if (satellite.InitializationError != Sgp4Error.None)
			{
				failures.TryAdd(item.NoradCatalogId, new(item.NoradCatalogId, satellite.InitializationError, 0.0));
			}
		}

		for (int i = 0; i < (onlyFirst ? 1 : objects.Length); i++)
		{
			for (int j = i + 1; j < objects.Length; j++)
			{
				pairCount++;

				if (OrbitShell.Of(objects[i]).GapTo(OrbitShell.Of(objects[j])) > options.ThresholdKm + options.ShellMarginKm)
				{
					continue;
				}

				survivors++;
				approaches.AddRange(screener.FindClosestApproaches(objects[i], objects[j], options));

				foreach (ScreeningFailure failure in screener.Screen([objects[i], objects[j]], options).Failures)
				{
					failures.TryAdd(failure.CatalogId, failure);
				}
			}
		}

		return new ConjunctionScreenResult(
			[.. approaches.OrderBy(a => a.MissDistanceKm).Select(a => a.ToEvent())],
			pairCount,
			survivors,
			[.. failures.Values.OrderBy(f => f.CatalogId)]);
	}

	private static void AssertSame(ConjunctionScreenResult expected, ConjunctionScreenResult actual)
	{
		Assert.AreEqual(expected.PairCount, actual.PairCount);
		Assert.AreEqual(expected.PairsPropagated, actual.PairsPropagated);
		CollectionAssert.AreEqual(expected.Conjunctions.ToArray(), actual.Conjunctions.ToArray());
		CollectionAssert.AreEqual(expected.Failures.ToArray(), actual.Failures.ToArray());
	}

	private static int SampleCount(ConjunctionScreenOptions options) =>
		(int)Math.Ceiling(options.WindowMinutes / options.StepMinutes) + 1;

	private static ConjunctionScreenOptions Options() => new()
	{
		WindowStart = JulianDate.FromUtc(Epoch),
		WindowMinutes = 360.0,
		ThresholdKm = 300.0,
	};

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

	/// <summary>An <see cref="IProgress{T}"/> that reports on the calling thread, unlike <see cref="Progress{T}"/>.</summary>
	private sealed class Recorder(Action<ConjunctionScreenProgress> record) : IProgress<ConjunctionScreenProgress>
	{
		public void Report(ConjunctionScreenProgress value) => record(value);
	}
}
