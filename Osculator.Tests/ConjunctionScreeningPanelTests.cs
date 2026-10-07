// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ktsu.ImGui.App.Testing;
using ktsu.Osculator.App.Panels;
using ktsu.Osculator.App.Shell;
using ktsu.Osculator.Core.Conjunction;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Time;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the conjunction screening panel: the sweep it runs, the per-type measurement of a row, and
/// the panel delivering both through the shell's work queue.
/// </summary>
/// <remarks>
/// The objects are the constructed ones <see cref="ConjunctionScreeningTests"/> reasons about: a
/// station-like orbit, a second one whose plane crosses it with the phase tuned to a 13 m pass at
/// 2.09 km/s, and a geostationary object no low orbit can meet.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ConjunctionScreeningPanelTests : IDisposable
{
	private const int StationId = 90001;

	private const int CrossingId = 90003;

	private const int GeostationaryId = 90004;

	private static readonly DateTime Epoch = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

	private AppShell? shell;
	private ImGuiAppHarness? harness;

	private static ElementSet Station => Leo(StationId);

	private static ElementSet Crossing => Leo(CrossingId, meanAnomaly: 347.526, raan: 120.0);

	private static ElementSet Geostationary => Leo(GeostationaryId, inclination: 0.05, meanMotion: 1.0027);

	/// <inheritdoc/>
	public void Dispose()
	{
		if (shell is not null)
		{
			foreach (Panel panel in shell.Registry.All)
			{
				panel.Dismiss();
			}

			shell.Dispose();
		}

		shell = null;
		harness?.Dispose();
		harness = null;
	}

	[TestMethod]
	public void SelectedAgainst_FindsBothCrossings_AndFiltersTheGeostationaryPair()
	{
		ConjunctionProgress progress = new();
		ConjunctionSweepResult result = ConjunctionSweep.SelectedAgainst(
			Station,
			[Station, Crossing, Geostationary],
			Options(120.0, 5.0),
			progress,
			CancellationToken.None);

		// The station itself is skipped, so two pairs, and the geostationary one never reaches the propagator.
		Assert.AreEqual(2L, result.PairCount);
		Assert.AreEqual(1L, result.PairsPropagated);
		Assert.AreEqual(1L, result.PairsRejectedByFilter);
		Assert.AreEqual(1, progress.Total);
		Assert.AreEqual(1, progress.Done);

		Assert.HasCount(2, result.Conjunctions);
		Assert.IsTrue(result.Conjunctions.All(c => c.PrimaryCatalogId == StationId && c.SecondaryCatalogId == CrossingId));
		Assert.IsTrue(result.Conjunctions.All(c => Math.Abs(c.MissDistanceKm - 0.0134) < 0.0005), "The crossings were tuned to about 13 m.");
		Assert.IsLessThanOrEqualTo(result.Conjunctions[1].MissDistanceKm, result.Conjunctions[0].MissDistanceKm, "Closest first.");
		Assert.IsEmpty(result.Failures);
		Assert.AreEqual(Crossing.ObjectName, result.Objects[CrossingId].ObjectName);
	}

	[TestMethod]
	public void AllAgainstAll_ScreensEveryPairOnce()
	{
		ConjunctionSweepResult result = ConjunctionSweep.AllAgainstAll(
			[Station, Crossing, Geostationary],
			Options(120.0, 5.0),
			new ConjunctionProgress(),
			CancellationToken.None);

		Assert.AreEqual(3L, result.PairCount);
		Assert.AreEqual(1L, result.PairsPropagated, "Both pairs with the geostationary object are a shell apart.");
		Assert.HasCount(2, result.Conjunctions);
	}

	[TestMethod]
	public void AllAgainstAll_RefusesMoreThanTheLimit()
	{
		ElementSet[] many = [.. Enumerable.Range(0, ConjunctionSweep.MaximumGroupSize + 1).Select(i => Leo(100000 + i))];

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ConjunctionSweep.AllAgainstAll(many, Options(10.0, 5.0), new ConjunctionProgress(), CancellationToken.None));
	}

	[TestMethod]
	public void Group_MatchesNamesCaseInsensitively_AndAnEmptyTermMatchesNothing()
	{
		ElementSet named = Station with { ObjectName = "ISS (ZARYA)" };
		ElementSet other = Crossing with { ObjectName = "STARLINK-1007" };

		IReadOnlyList<ElementSet> group = ConjunctionSweep.Group([other, named], "iss");

		Assert.HasCount(1, group);
		Assert.AreSame(named, group[0]);
		Assert.IsEmpty(ConjunctionSweep.Group([other, named], "  "));
	}

	[TestMethod]
	public void Sweep_StopsWhenCancelled()
	{
		using CancellationTokenSource cancelled = new();
		cancelled.Cancel();

		Assert.ThrowsExactly<OperationCanceledException>(() => ConjunctionSweep.SelectedAgainst(
			Station,
			[Crossing],
			Options(120.0, 5.0),
			new ConjunctionProgress(),
			cancelled.Token));
	}

	[TestMethod]
	public void CompareAcrossStorageTypes_FindsTheSameApproachInEachType_AndOnlyFloatIsVisiblyOff()
	{
		ConjunctionScreenOptions options = Options(120.0, 5.0);
		ConjunctionEvent screened = ConjunctionSweep.SelectedAgainst(Station, [Crossing], options, new ConjunctionProgress(), CancellationToken.None)
			.Conjunctions.OrderBy(c => c.MinutesFromWindowStart).First();

		PairComparison comparison = ConjunctionSweep.CompareAcrossStorageTypes(Station, Crossing, options, screened, CancellationToken.None);

		Assert.HasCount(4, comparison.Runs);
		Assert.AreEqual("float", comparison.Runs[0].StorageName);
		Assert.AreEqual("double", comparison.Runs[1].StorageName);
		Assert.AreEqual("decimal", comparison.Runs[2].StorageName);
		StringAssert.StartsWith(comparison.Reference.StorageName, "PreciseNumber");
		Assert.IsTrue(comparison.Runs.All(r => r.Approach is not null), "A storage type lost the approach.");

		// Every type finds it at the screened time, reported against the screen's own window: the
		// narrow re-run's offset is added back.
		foreach (StorageMiss run in comparison.Runs)
		{
			Assert.AreEqual(screened.MinutesFromWindowStart, run.Approach!.Value.MinutesFromWindowStart, 1e-4, run.StorageName);
		}

		// The double run is the screen measured again over a shifted window, so its bisection stops at a
		// different point inside the same nanominute: about 1e-11 km apart, measured. A micrometre bounds it.
		Assert.AreEqual(screened.MissDistanceKm, comparison.Runs[1].Approach!.Value.MissDistanceKm, 1e-9);

		// The cancellation case: float is off by the better part of a metre on a 13 m pass, double by
		// around a picometre-per-kilometre, decimal by less.
		double floatOff = Math.Abs(comparison.MissDifferenceKm(comparison.Runs[0])!.Value);
		double doubleOff = Math.Abs(comparison.MissDifferenceKm(comparison.Runs[1])!.Value);
		double decimalOff = Math.Abs(comparison.MissDifferenceKm(comparison.Runs[2])!.Value);
		Assert.IsGreaterThan(1e-4, floatOff, "float should be visibly off: tenths of a metre.");
		Assert.IsLessThan(1e-2, floatOff);
		Assert.IsLessThan(1e-9, doubleOff);
		Assert.IsLessThan(1e-9, decimalOff);
		Assert.AreEqual(0.0, comparison.MissDifferenceKm(comparison.Reference));
		Assert.AreEqual(0.0, comparison.TimeDifferenceSeconds(comparison.Reference));
	}

	[TestMethod]
	public void Panel_IsRegistered_ScreensThroughTheWorkQueue_AndMeasuresASelectedRow()
	{
		Assert.IsTrue(RegisteredPanels().OfType<ConjunctionScreeningPanel>().Any(), "The panel is missing from Panels.Register.");

		shell = new AppShell(registry => registry.Register<ConjunctionScreeningPanel>());
		harness = ImGuiAppHarness.Start(shell.BuildConfig(), new HarnessOptions());
		harness.Step(2);

		ConjunctionScreeningPanel panel = (ConjunctionScreeningPanel)shell.Registry.All[0];
		Assert.IsTrue(panel.DrawnThisFrame);

		panel.StartScreen(Station, [Station, Crossing, Geostationary], Options(120.0, 5.0), Epoch);
		Assert.IsTrue(harness.StepUntil(() => panel.Result is not null, 2000), "The screen never reached the panel.");
		Assert.HasCount(2, panel.Result!.Conjunctions);

		panel.SelectRow(0);
		Assert.IsTrue(harness.StepUntil(() => panel.Comparison is not null, 5000), "The per-type measurement never reached the panel.");
		Assert.HasCount(4, panel.Comparison!.Runs);

		harness.Step(2);
		Assert.IsTrue(panel.DrawnThisFrame, "The panel stopped drawing once it had results to show.");
	}

	private static List<Panel> RegisteredPanels()
	{
		using BackgroundWork work = new(_ => throw new InvalidOperationException("No work runs here."));
		PanelRegistry registry = new(work);
		Panels.Register(registry);
		return [.. registry.All];
	}

	private static ConjunctionScreenOptions Options(double windowMinutes, double thresholdKm) => new()
	{
		WindowStart = JulianDate.FromUtc(Epoch),
		WindowMinutes = windowMinutes,
		ThresholdKm = thresholdKm,
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
}
