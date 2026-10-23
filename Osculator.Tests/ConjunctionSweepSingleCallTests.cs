// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Linq;
using System.Threading;
using ktsu.Osculator.App.Panels;
using ktsu.Osculator.Core.Conjunction;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the panel's sweep making one call into the core screen rather than one per pair
/// (issue #126), so each object is propagated once per grid sample in the app as well.
/// </summary>
/// <remarks>
/// Propagations are counted on the sweep's own screener, whose counters run for the life of the
/// process, so each test reads the difference across one sweep. The class does not run in
/// parallel with itself so that difference is this sweep's alone.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ConjunctionSweepSingleCallTests
{
	private static readonly DateTime Epoch = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

	/// <summary>
	/// Gets twelve station-like objects around one shell, so every pair survives the pre-filter, and
	/// the crossing object of <see cref="ConjunctionScreeningTests"/>, which meets the first of them
	/// within metres, so there is something to find.
	/// </summary>
	private static ElementSet[] Group =>
	[
		.. Enumerable.Range(0, 12).Select(k => Leo(92000 + k, raan: 100.0 + (7.0 * k), meanAnomaly: 29.0 * k)),
		Leo(92100, raan: 120.0, meanAnomaly: 347.526),
	];

	[TestMethod]
	public void AllAgainstAll_PropagatesEachObjectOncePerSample_AndAgreesWithTheCore()
	{
		ConjunctionScreenOptions options = Options();
		ElementSet[] group = Group;
		ConjunctionProgress progress = new();
		long before = ConjunctionSweep.Screener.ScanPropagations;

		ConjunctionSweepResult result = ConjunctionSweep.AllAgainstAll(group, options, progress, CancellationToken.None);

		long scan = ConjunctionSweep.Screener.ScanPropagations - before;

		Assert.AreEqual(78L, result.PairsPropagated);
		Assert.AreEqual(13L * SampleCount(options), scan, "every object over the whole grid, once");
		Assert.AreEqual(78, progress.Total);
		Assert.AreEqual(78, progress.Done);

		ConjunctionScreenResult direct = ConjunctionScreener.Create(DoubleStorageMath.Instance).Screen(group, options);

		Assert.IsNotEmpty(result.Conjunctions);
		CollectionAssert.AreEquivalent(direct.Conjunctions.ToArray(), result.Conjunctions.ToArray());
		Assert.AreEqual(direct.PairCount, result.PairCount);
	}

	[TestMethod]
	public void SelectedAgainst_PropagatesThePrimaryOnce()
	{
		ConjunctionScreenOptions options = Options();
		ElementSet[] group = Group;
		long before = ConjunctionSweep.Screener.ScanPropagations;

		ConjunctionSweepResult result = ConjunctionSweep.SelectedAgainst(group[0], group, options, new ConjunctionProgress(), CancellationToken.None);

		Assert.AreEqual(12L, result.PairsPropagated);
		Assert.AreEqual(13L * SampleCount(options), ConjunctionSweep.Screener.ScanPropagations - before, "the primary once and each partner once");
	}

	[TestMethod]
	public void Progress_NeverMovesBackwards()
	{
		ConjunctionProgress progress = new();

		progress.Report(new ConjunctionScreenProgress(5, 10));
		progress.Report(new ConjunctionScreenProgress(3, 10));

		Assert.AreEqual(5, progress.Done);
		Assert.AreEqual(10, progress.Total);
	}

	private static int SampleCount(ConjunctionScreenOptions options) =>
		(int)Math.Ceiling(options.WindowMinutes / options.StepMinutes) + 1;

	private static ConjunctionScreenOptions Options() => new()
	{
		WindowStart = JulianDate.FromUtc(Epoch),
		WindowMinutes = 360.0,
		ThresholdKm = 300.0,
	};

	private static ElementSet Leo(int catalogId, double meanAnomaly, double raan) => new()
	{
		ObjectName = $"TEST {catalogId}",
		ObjectId = "2026-001A",
		NoradCatalogId = catalogId,
		Epoch = Epoch,
		EpochJulianDate = JulianDate.FromUtc(Epoch),
		MeanMotion = 15.5,
		Eccentricity = 0.0005,
		Inclination = 51.6,
		RightAscensionOfAscendingNode = raan,
		ArgumentOfPericenter = 90.0,
		MeanAnomaly = meanAnomaly,
		BStar = 0.0001,
	};
}
