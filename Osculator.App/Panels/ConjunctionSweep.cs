// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Panels;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Osculator.Core.Conjunction;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Numerics.Precise;

/// <summary>
/// The work behind the conjunction screening panel, with no ImGui in it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The screen runs in <see langword="double"/>, and only the pairs it finds are run in all
/// four storage types.</strong> A thirty-digit propagation costs about seven thousand times a
/// <see langword="double"/> one, so screening a catalogue in <c>PreciseNumber</c> would take days,
/// and screening is a decision about which pairs are worth a closer look, padded by margins far
/// wider than <see langword="double"/>'s rounding. The measurement is the miss distance of a pair
/// already found, which is where the storage types disagree, so that is what
/// <see cref="CompareAcrossStorageTypes"/> takes in each of them.
/// </para>
/// <para>
/// Pairs are screened one at a time through <see cref="IConjunctionScreener.Screen"/>, so the shell
/// pre-filter and the failure reporting are the core's own, and the loop between pairs is where a
/// cancellation is noticed and progress is counted. The shell test is repeated here first, against
/// shells computed once per object, so a rejected pair costs a subtraction rather than two
/// initializations.
/// </para>
/// </remarks>
internal static class ConjunctionSweep
{
	/// <summary>The most objects screened all against all, which is about eighty thousand pairs.</summary>
	internal const int MaximumGroupSize = 400;

	/// <summary>Minutes in a day.</summary>
	private const double MinutesPerDay = 1440.0;

	/// <summary>How far either side of the screened time of closest approach the per-type re-run looks, in minutes.</summary>
	private const double ComparisonHalfWindowMinutes = 1.0;

	/// <summary>The coarse step of the per-type re-run, in minutes.</summary>
	private const double ComparisonStepMinutes = 0.25;

	/// <summary>
	/// The threshold the per-type re-run reports under, in kilometres.
	/// </summary>
	/// <remarks>
	/// Effectively none. The pair was already found; the re-run is a measurement, and a storage type
	/// that puts the pair tens of kilometres from where <see langword="double"/> did must still report
	/// it, because that disagreement is the result.
	/// </remarks>
	private const double ComparisonThresholdKm = 1.0e6;

	private static readonly ConjunctionScreener<double> Screener = ConjunctionScreener.Create(DoubleStorageMath.Instance);

	/// <summary>
	/// Screens one object against every other in a catalogue.
	/// </summary>
	/// <param name="primary">The object to screen.</param>
	/// <param name="catalogue">The objects to screen it against. The primary itself is skipped if present.</param>
	/// <param name="options">The window and thresholds.</param>
	/// <param name="progress">Counts the pairs as they are screened.</param>
	/// <param name="cancellationToken">Stops the screen between pairs.</param>
	/// <returns>The approaches found.</returns>
	internal static ConjunctionSweepResult SelectedAgainst(
		ElementSet primary,
		IReadOnlyList<ElementSet> catalogue,
		ConjunctionScreenOptions options,
		ConjunctionProgress progress,
		CancellationToken cancellationToken)
	{
		Ensure.NotNull(primary);
		Ensure.NotNull(catalogue);

		ElementSet[] others = [.. catalogue.Where(e => e.NoradCatalogId != primary.NoradCatalogId)];
		(ElementSet, ElementSet)[] pairs = [.. others.Select(other => (primary, other))];

		return Sweep(pairs, [primary, .. others], options, progress, cancellationToken);
	}

	/// <summary>
	/// Screens every pair in a set of objects.
	/// </summary>
	/// <param name="objects">The objects, at most <see cref="MaximumGroupSize"/>.</param>
	/// <param name="options">The window and thresholds.</param>
	/// <param name="progress">Counts the pairs as they are screened.</param>
	/// <param name="cancellationToken">Stops the screen between pairs.</param>
	/// <returns>The approaches found.</returns>
	/// <exception cref="ArgumentOutOfRangeException">There are more than <see cref="MaximumGroupSize"/> objects.</exception>
	internal static ConjunctionSweepResult AllAgainstAll(
		IReadOnlyList<ElementSet> objects,
		ConjunctionScreenOptions options,
		ConjunctionProgress progress,
		CancellationToken cancellationToken)
	{
		Ensure.NotNull(objects);

		if (objects.Count > MaximumGroupSize)
		{
			throw new ArgumentOutOfRangeException(nameof(objects), objects.Count, $"All-against-all screening is limited to {MaximumGroupSize} objects.");
		}

		List<(ElementSet, ElementSet)> pairs = [];

		for (int i = 0; i < objects.Count; i++)
		{
			for (int j = i + 1; j < objects.Count; j++)
			{
				pairs.Add((objects[i], objects[j]));
			}
		}

		return Sweep(pairs, objects, options, progress, cancellationToken);
	}

	/// <summary>
	/// Picks the objects whose name contains a search term, for an all-against-all screen.
	/// </summary>
	/// <param name="catalogue">The catalogue.</param>
	/// <param name="nameContains">The term, matched case-insensitively against the object's name.</param>
	/// <returns>The matching objects, in catalogue-number order.</returns>
	internal static IReadOnlyList<ElementSet> Group(IReadOnlyList<ElementSet> catalogue, string nameContains)
	{
		Ensure.NotNull(catalogue);
		Ensure.NotNull(nameContains);

		string term = nameContains.Trim();

		return term.Length == 0
			? []
			: [.. catalogue
				.Where(e => e.ObjectName?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)
				.OrderBy(e => e.NoradCatalogId)];
	}

	/// <summary>
	/// Measures one screened approach again in every storage type.
	/// </summary>
	/// <param name="primary">The first object of the pair.</param>
	/// <param name="secondary">The second object of the pair.</param>
	/// <param name="screen">The options the pair was screened with.</param>
	/// <param name="screened">The approach the <see langword="double"/> screen found.</param>
	/// <param name="cancellationToken">Stops between storage types.</param>
	/// <returns>The four measurements, <c>PreciseNumber</c> last.</returns>
	/// <remarks>
	/// Each type searches a two-minute window centred on the screened time, which holds exactly one
	/// closest approach for anything in Earth orbit, and reports the one nearest the centre. The
	/// window is narrow so the thirty-digit run takes a fraction of a second rather than a day's
	/// worth of propagation; the time and the miss distance are still found and measured in each
	/// type, from that type's own states.
	/// </remarks>
	internal static PairComparison CompareAcrossStorageTypes(
		ElementSet primary,
		ElementSet secondary,
		ConjunctionScreenOptions screen,
		ConjunctionEvent screened,
		CancellationToken cancellationToken)
	{
		Ensure.NotNull(primary);
		Ensure.NotNull(secondary);
		Ensure.NotNull(screen);

		double offsetMinutes = screened.MinutesFromWindowStart - ComparisonHalfWindowMinutes;
		ConjunctionScreenOptions narrow = screen with
		{
			WindowStart = AddMinutes(screen.WindowStart, offsetMinutes),
			WindowMinutes = 2.0 * ComparisonHalfWindowMinutes,
			StepMinutes = ComparisonStepMinutes,
			ThresholdKm = ComparisonThresholdKm,
		};

		StorageMiss[] runs =
		[
			Measure("float", FloatStorageMath.Instance),
			Measure("double", DoubleStorageMath.Instance),
			Measure("decimal", DecimalStorageMath.Instance),
			Measure($"PreciseNumber ({PreciseStorageMath.Instance.SignificantDigits} digits)", PreciseStorageMath.Instance),
		];

		return new PairComparison(screened, runs);

		StorageMiss Measure<T>(string name, IStorageMath<T> math)
			where T : struct, INumber<T>
		{
			cancellationToken.ThrowIfCancellationRequested();

			Stopwatch stopwatch = Stopwatch.StartNew();
			IReadOnlyList<ClosestApproach<T>> found = ConjunctionScreener.Create(math).FindClosestApproaches(primary, secondary, narrow);
			stopwatch.Stop();

			ConjunctionEvent? nearest = null;

			if (found.Count > 0)
			{
				ConjunctionEvent closest = found
					.Select(a => a.ToEvent())
					.MinBy(e => Math.Abs(e.MinutesFromWindowStart - ComparisonHalfWindowMinutes));

				// Reported against the screen's window rather than the narrow one, so it reads beside the screened row.
				nearest = closest with { MinutesFromWindowStart = closest.MinutesFromWindowStart + offsetMinutes };
			}

			return new StorageMiss(name, nearest, stopwatch.Elapsed.TotalSeconds);
		}
	}

	/// <summary>Moves a Julian date by a number of minutes, keeping the whole day separate.</summary>
	/// <param name="date">The date.</param>
	/// <param name="minutes">The minutes to add.</param>
	/// <returns>The moved date.</returns>
	internal static JulianDate AddMinutes(JulianDate date, double minutes) =>
		new(date.Day, date.DayFraction + (minutes / MinutesPerDay));

	private static ConjunctionSweepResult Sweep(
		IReadOnlyList<(ElementSet Primary, ElementSet Secondary)> pairs,
		IReadOnlyList<ElementSet> objects,
		ConjunctionScreenOptions options,
		ConjunctionProgress progress,
		CancellationToken cancellationToken)
	{
		Ensure.NotNull(options);
		Ensure.NotNull(progress);
		options.Validate();

		Dictionary<int, OrbitShell> shells = [];

		foreach (ElementSet elements in objects)
		{
			shells.TryAdd(elements.NoradCatalogId, OrbitShell.Of(elements));
		}

		double reach = options.ThresholdKm + options.ShellMarginKm;
		(ElementSet Primary, ElementSet Secondary)[] survivors =
			[.. pairs.Where(p => shells[p.Primary.NoradCatalogId].GapTo(shells[p.Secondary.NoradCatalogId]) <= reach)];

		progress.Start(survivors.Length);

		ConcurrentBag<ConjunctionEvent> found = [];
		ConcurrentDictionary<int, ScreeningFailure> failures = [];

		// Pairs are independent and the screener holds no state between calls, so they spread across
		// cores; the result is sorted afterwards, so the order they finish in does not show.
		Parallel.ForEach(
			survivors,
			new ParallelOptions { CancellationToken = cancellationToken },
			pair =>
			{
				ConjunctionScreenResult result = Screener.Screen([pair.Primary, pair.Secondary], options);

				foreach (ConjunctionEvent conjunction in result.Conjunctions)
				{
					found.Add(conjunction);
				}

				foreach (ScreeningFailure failure in result.Failures)
				{
					failures.TryAdd(failure.CatalogId, failure);
				}

				progress.Advance();
			});

		Dictionary<int, ElementSet> byId = [];

		foreach (ElementSet elements in objects)
		{
			byId.TryAdd(elements.NoradCatalogId, elements);
		}

		return new ConjunctionSweepResult(
			options,
			[.. found
				.OrderBy(c => c.MissDistanceKm)
				.ThenBy(c => c.PrimaryCatalogId)
				.ThenBy(c => c.SecondaryCatalogId)
				.ThenBy(c => c.MinutesFromWindowStart)],
			pairs.Count,
			survivors.Length,
			[.. failures.Values.OrderBy(f => f.CatalogId)],
			byId);
	}
}

/// <summary>
/// The outcome of a screen run by the panel.
/// </summary>
/// <param name="Options">The window and thresholds it ran with.</param>
/// <param name="Conjunctions">Every approach within the threshold, closest first.</param>
/// <param name="PairCount">The pairs considered.</param>
/// <param name="PairsPropagated">The pairs that survived the shell pre-filter and were propagated.</param>
/// <param name="Failures">Objects the model could not follow through the window, each once.</param>
/// <param name="Objects">Every object screened, by catalogue number.</param>
internal sealed record ConjunctionSweepResult(
	ConjunctionScreenOptions Options,
	IReadOnlyList<ConjunctionEvent> Conjunctions,
	long PairCount,
	long PairsPropagated,
	IReadOnlyList<ScreeningFailure> Failures,
	IReadOnlyDictionary<int, ElementSet> Objects)
{
	/// <summary>Gets the pairs the shell pre-filter rejected without propagating them.</summary>
	internal long PairsRejectedByFilter => PairCount - PairsPropagated;
}

/// <summary>
/// One storage type's measurement of a screened approach.
/// </summary>
/// <param name="StorageName">The storage type's name.</param>
/// <param name="Approach">The approach it found, or <see langword="null"/> when it found none near the screened time.</param>
/// <param name="Seconds">The wall-clock time the search took.</param>
internal sealed record StorageMiss(string StorageName, ConjunctionEvent? Approach, double Seconds);

/// <summary>
/// One screened approach measured in all four storage types.
/// </summary>
/// <param name="Screened">The approach as the <see langword="double"/> screen found it.</param>
/// <param name="Runs">Float, double, decimal, then the reference.</param>
internal sealed record PairComparison(ConjunctionEvent Screened, IReadOnlyList<StorageMiss> Runs)
{
	/// <summary>Gets the reference every other run is measured against.</summary>
	internal StorageMiss Reference => Runs[^1];

	/// <summary>
	/// Gets how far a run's miss distance is from the reference's, in kilometres, signed.
	/// </summary>
	/// <param name="run">The run.</param>
	/// <returns>The difference, or <see langword="null"/> when either found nothing.</returns>
	/// <remarks>
	/// Both miss distances were computed in their own types and only rounded to
	/// <see langword="double"/> at the end, so this difference is of two finished answers: a
	/// kilometre-scale distance rounded to <see langword="double"/> keeps about 1e-13 km, which is
	/// the floor the table can show.
	/// </remarks>
	internal double? MissDifferenceKm(StorageMiss run)
	{
		Ensure.NotNull(run);

		return run.Approach is ConjunctionEvent own && Reference.Approach is ConjunctionEvent reference
			? own.MissDistanceKm - reference.MissDistanceKm
			: null;
	}

	/// <summary>
	/// Gets how far a run's time of closest approach is from the reference's, in seconds, signed.
	/// </summary>
	/// <param name="run">The run.</param>
	/// <returns>The difference, or <see langword="null"/> when either found nothing.</returns>
	internal double? TimeDifferenceSeconds(StorageMiss run)
	{
		Ensure.NotNull(run);

		return run.Approach is ConjunctionEvent own && Reference.Approach is ConjunctionEvent reference
			? (own.MinutesFromWindowStart - reference.MinutesFromWindowStart) * 60.0
			: null;
	}
}

/// <summary>
/// Counts a screen's progress, written by the workers and read by the panel each frame.
/// </summary>
internal sealed class ConjunctionProgress
{
	private int total;
	private int done;

	/// <summary>Gets the number of pairs to propagate.</summary>
	internal int Total => Volatile.Read(ref total);

	/// <summary>Gets the number of pairs propagated so far.</summary>
	internal int Done => Volatile.Read(ref done);

	/// <summary>Resets the count for a screen of a given size.</summary>
	/// <param name="pairs">The number of pairs to propagate.</param>
	internal void Start(int pairs)
	{
		Volatile.Write(ref done, 0);
		Volatile.Write(ref total, pairs);
	}

	/// <summary>Counts one pair as propagated.</summary>
	internal void Advance() => Interlocked.Increment(ref done);
}
