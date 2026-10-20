// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Conjunction;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;

/// <summary>
/// Creates a conjunction screen over a storage type.
/// </summary>
/// <remarks>
/// A factory so that the storage type is inferred from the <see cref="IStorageMath{T}"/> passed in,
/// as with <c>Sgp4PropagatorHost</c>: a storage facade can then expose a screen
/// without spelling its type.
/// </remarks>
public static class ConjunctionScreener
{
	/// <summary>Creates a screen over the storage type <paramref name="math"/> serves.</summary>
	/// <typeparam name="T">The numeric storage type, inferred from <paramref name="math"/>.</typeparam>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The screen.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="math"/> is null.</exception>
	public static ConjunctionScreener<T> Create<T>(IStorageMath<T> math)
		where T : struct, INumber<T> => new(math);
}

/// <summary>
/// All-against-all conjunction screening, with every propagation and every difference taken in one
/// storage type.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// Three stages, each cheaper than the next and each discarding what the next need not see:
/// </para>
/// <list type="number">
/// <item><description>
/// <strong>Shells.</strong> A pair whose perigee-to-apogee bands are further apart than the
/// threshold plus <see cref="ConjunctionScreenOptions.ShellMarginKm"/> is rejected without being
/// propagated. See <see cref="OrbitShell"/> for why that is safe. For a real catalogue this removes
/// most pairs: low-earth objects never meet geostationary ones.
/// </description></item>
/// <item><description>
/// <strong>Scan.</strong> Each object a survivor needs is propagated once on a grid of
/// <see cref="ConjunctionScreenOptions.StepMinutes"/>, not once per pair it is in, and every pair
/// is read from those states, watching the sign of
/// <c>Δr · Δv</c> — the range rate times the range. A closest approach is where it changes from
/// negative (closing) to positive (opening). Only those inside the window count; a pair already
/// opening at the window's start, or still closing at its end, has its minimum outside the window,
/// and the window's edge is not an approach.
/// </description></item>
/// <item><description>
/// <strong>Refinement.</strong> Each bracket is bisected on that sign down to
/// <see cref="ConjunctionScreenOptions.TimeToleranceMinutes"/>, unless a bound shows the pair cannot
/// come within the threshold inside it. The bound is rigorous rather than a guess: the relative
/// speed cannot grow faster than the two objects' combined gravitational acceleration at the
/// Earth's surface, so the range cannot dip lower than the two end ranges allow over one step.
/// </description></item>
/// </list>
/// <para>
/// <strong>The scan and the bound are allowed to be in <see langword="double"/>; the time and the
/// miss distance are not.</strong> Which brackets are worth refining is a screening decision, padded
/// by a margin, and rounding cannot move it. The time of closest approach and the distance at it are
/// the measurement, so the time is carried and bisected in <typeparamref name="T"/>, both objects are
/// propagated in <typeparamref name="T"/>, and the difference of their positions — the cancellation
/// the spec names — is taken in <typeparamref name="T"/>.
/// </para>
/// <para>
/// <strong>The sign test reads SGP4's stated velocity, which is not quite the derivative of its
/// position</strong> (domain trap 13: about 1e-3 km/s). So the time found is where the stated
/// relative velocity is perpendicular to the relative position, which sits a little off the true
/// minimum of the range. The miss distance is still the range at the time found, and because the
/// range is at a minimum there the cost is second order: about <c>d·(δv/v)²/2</c>, a micrometre on a
/// 13 m miss at 2 km/s. It would only matter for a pair whose relative speed is itself around a
/// metre per second, and two objects that close in velocity also share most of the discrepancy.
/// </para>
/// </remarks>
public sealed class ConjunctionScreener<T> : IConjunctionScreener
	where T : struct, INumber<T>
{
	private const double MinutesPerDay = 1440.0;

	private const double SecondsPerMinute = 60.0;

	/// <summary>
	/// The largest bisection count. Halving a one-minute bracket reaches a nanominute in thirty
	/// steps; this is a backstop for a storage type whose midpoint never stops moving.
	/// </summary>
	private const int MaximumBisections = 200;

	/// <summary>
	/// A bound on how fast the relative velocity of two Earth-orbiting objects can change, in km/s².
	/// </summary>
	/// <remarks>
	/// Each object's acceleration is at most the surface gravity μ/Rₑ² ≈ 0.0098 km/s², so the
	/// difference is at most twice that. Three times leaves room for the oblateness term, which is a
	/// thousandth of the central one, and for anything else short of the object being below ground.
	/// </remarks>
	private static readonly double RelativeAccelerationBound =
		3.0 * Wgs72<double>.Mu / (Wgs72<double>.RadiusEarthKm * Wgs72<double>.RadiusEarthKm);

	private long scanPropagations;

	private long refinePropagations;

	/// <summary>Initializes a new instance of the <see cref="ConjunctionScreener{T}"/> class.</summary>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <exception cref="ArgumentNullException"><paramref name="math"/> is null.</exception>
	public ConjunctionScreener(IStorageMath<T> math)
	{
		Ensure.NotNull(math);
		Math = math;
	}

	/// <summary>Gets the transcendental functions the screen runs with.</summary>
	public IStorageMath<T> Math { get; }

	/// <inheritdoc/>
	/// <exception cref="ArgumentNullException"><paramref name="objects"/> or <paramref name="options"/> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException">An option is out of range.</exception>
	public ConjunctionScreenResult Screen(IReadOnlyList<ElementSet> objects, ConjunctionScreenOptions options) =>
		Screen(objects, options, null, CancellationToken.None);

	/// <summary>
	/// Screens every pair in a set of objects for closest approaches within a threshold, reporting
	/// progress and stopping when asked.
	/// </summary>
	/// <param name="objects">The objects to screen against each other.</param>
	/// <param name="options">The window and thresholds.</param>
	/// <param name="progress">
	/// Told how many pairs have been scanned, once before the first and once after each. Called from
	/// worker threads, so it must be safe to call concurrently.
	/// </param>
	/// <param name="cancellationToken">Stops the screen, which then throws <see cref="OperationCanceledException"/>.</param>
	/// <returns>The approaches found, with the pre-filter's counts and any objects that failed.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="objects"/> or <paramref name="options"/> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException">An option is out of range.</exception>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
	public ConjunctionScreenResult Screen(
		IReadOnlyList<ElementSet> objects,
		ConjunctionScreenOptions options,
		IProgress<ConjunctionScreenProgress>? progress,
		CancellationToken cancellationToken)
	{
		Ensure.NotNull(objects);
		Ensure.NotNull(options);
		options.Validate();

		Tracked[] tracked = [.. objects.Select(e => Track(e, options.WindowStart))];
		List<PairIndex> survivors = [];
		long pairCount = 0;

		for (int i = 0; i < tracked.Length; i++)
		{
			for (int j = i + 1; j < tracked.Length; j++)
			{
				pairCount++;

				if (ShellsCanMeet(tracked[i], tracked[j], options))
				{
					survivors.Add(new PairIndex(i, j, survivors.Count));
				}
			}
		}

		return Run(tracked, survivors, pairCount, options, progress, cancellationToken);
	}

	/// <summary>
	/// Screens one object against every other in a catalogue.
	/// </summary>
	/// <param name="primary">The object to screen.</param>
	/// <param name="catalogue">The objects to screen it against. Any with the primary's catalogue number are skipped.</param>
	/// <param name="options">The window and thresholds.</param>
	/// <param name="progress">
	/// Told how many pairs have been scanned, once before the first and once after each. Called from
	/// worker threads, so it must be safe to call concurrently.
	/// </param>
	/// <param name="cancellationToken">Stops the screen, which then throws <see cref="OperationCanceledException"/>.</param>
	/// <returns>The approaches found, with the pre-filter's counts and any objects that failed.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="primary"/>, <paramref name="catalogue"/> or <paramref name="options"/> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException">An option is out of range.</exception>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
	/// <remarks>
	/// The primary is propagated over the grid once however many partners survive the pre-filter,
	/// and each partner once.
	/// </remarks>
	public ConjunctionScreenResult ScreenAgainst(
		ElementSet primary,
		IReadOnlyList<ElementSet> catalogue,
		ConjunctionScreenOptions options,
		IProgress<ConjunctionScreenProgress>? progress,
		CancellationToken cancellationToken)
	{
		Ensure.NotNull(primary);
		Ensure.NotNull(catalogue);
		Ensure.NotNull(options);
		options.Validate();

		Tracked[] tracked =
		[
			Track(primary, options.WindowStart),
			.. catalogue.Where(e => e.NoradCatalogId != primary.NoradCatalogId).Select(e => Track(e, options.WindowStart)),
		];

		List<PairIndex> survivors = [];

		for (int j = 1; j < tracked.Length; j++)
		{
			if (ShellsCanMeet(tracked[0], tracked[j], options))
			{
				survivors.Add(new PairIndex(0, j, survivors.Count));
			}
		}

		return Run(tracked, survivors, tracked.Length - 1, options, progress, cancellationToken);
	}

	/// <summary>Gets how many propagations the scan stage has made, over every screen this instance has run.</summary>
	internal long ScanPropagations => Interlocked.Read(ref scanPropagations);

	/// <summary>Gets how many propagations the refinement stage has made, over every screen this instance has run.</summary>
	internal long RefinePropagations => Interlocked.Read(ref refinePropagations);

	/// <summary>
	/// Finds every closest approach between two objects within the threshold, without the shell
	/// pre-filter.
	/// </summary>
	/// <param name="primary">The first object.</param>
	/// <param name="secondary">The second object.</param>
	/// <param name="options">The window and thresholds.</param>
	/// <returns>The approaches, in time order, each with both states in <typeparamref name="T"/>.</returns>
	/// <exception cref="ArgumentNullException">An argument is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException">An option is out of range.</exception>
	/// <remarks>
	/// This is the entry point for comparing storage types on one pair, which is what the generic
	/// result is for: <see cref="ClosestApproach{T}"/> keeps the states, so a caller can difference
	/// two storage types' answers in the more precise of them.
	/// </remarks>
	public IReadOnlyList<ClosestApproach<T>> FindClosestApproaches(ElementSet primary, ElementSet secondary, ConjunctionScreenOptions options)
	{
		Ensure.NotNull(primary);
		Ensure.NotNull(secondary);
		Ensure.NotNull(options);
		options.Validate();

		Tracked first = Track(primary, options.WindowStart);
		Tracked second = Track(secondary, options.WindowStart);
		T[] times = SampleTimes(options);

		return ScanPair(first, BuildGrid(first, times), second, BuildGrid(second, times), options).Approaches;
	}

	private static bool ShellsCanMeet(Tracked a, Tracked b, ConjunctionScreenOptions options) =>
		a.Shell.GapTo(b.Shell) <= options.ThresholdKm + options.ShellMarginKm;

	/// <remarks>
	/// The whole days and the fractions are differenced separately, so the result keeps the
	/// fraction's resolution rather than that of a seven-digit Julian day (domain trap 4).
	/// </remarks>
	private static double MinutesBetween(JulianDate from, JulianDate to) =>
		(to.Day - from.Day + (to.DayFraction - from.DayFraction)) * MinutesPerDay;

	private static double ToDouble(T value) => double.CreateSaturating(value);

	private static T Dot(T ax, T ay, T az, T bx, T by, T bz) => (ax * bx) + (ay * by) + (az * bz);

	private Tracked Track(ElementSet elements, JulianDate windowStart) => new(
		elements,
		Sgp4<T>.Initialize(elements, Math),
		T.CreateChecked(MinutesBetween(elements.EpochJulianDate, windowStart)),
		OrbitShell.Of(elements));

	/// <summary>The instants the scan samples, in minutes from the window start.</summary>
	private static T[] SampleTimes(ConjunctionScreenOptions options)
	{
		int steps = (int)System.Math.Ceiling(options.WindowMinutes / options.StepMinutes);
		T windowEnd = T.CreateChecked(options.WindowMinutes);
		T step = T.CreateChecked(options.StepMinutes);
		T[] times = new T[steps + 1];

		for (int i = 0; i <= steps; i++)
		{
			// The last sample is the window's end exactly, not the step past it.
			times[i] = i == steps ? windowEnd : T.CreateChecked(i) * step;
		}

		return times;
	}

	/// <summary>Keeps the failure of an object from the earliest pair that met it, as the pair order would.</summary>
	private static void Note(Dictionary<int, (long Order, ScreeningFailure Failure)> failures, long order, ScreeningFailure failure)
	{
		if (!failures.TryGetValue(failure.CatalogId, out (long Order, ScreeningFailure Failure) existing) || order < existing.Order)
		{
			failures[failure.CatalogId] = (order, failure);
		}
	}

	/// <summary>
	/// Scans the pairs that survived the pre-filter, propagating each object over the grid once per
	/// block it is held in rather than once per pair it is in, and refines what the scan brackets.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The objects are cut into blocks small enough that two of them fit in
	/// <see cref="ConjunctionScreenOptions.MaximumCachedStates"/>, or into one block when they all
	/// do. Pairs are taken a tile at a time — one block against another — and a grid is built only
	/// when a pair in the tile needs it, so an object every partner of which was rejected by the
	/// pre-filter is never propagated, as before. The row's block is held for the whole row and each
	/// column's block is released after its tile.
	/// </para>
	/// <para>
	/// <strong>The answer is the one the pair-by-pair scan gave, exactly.</strong> Each sample is the
	/// same propagation at the same instant, the scan reads the same states, and refinement still
	/// propagates both objects afresh. Pairs run in parallel, so each is numbered by its place in the
	/// pair order and the results are put back in that order: approaches by miss distance with ties
	/// in pair order, and each failed object reported from the first pair that reached its failure.
	/// </para>
	/// </remarks>
	private ConjunctionScreenResult Run(
		Tracked[] tracked,
		List<PairIndex> survivors,
		long pairCount,
		ConjunctionScreenOptions options,
		IProgress<ConjunctionScreenProgress>? progress,
		CancellationToken cancellationToken)
	{
		Dictionary<int, (long Order, ScreeningFailure Failure)> failures = [];

		foreach (Tracked item in tracked.Where(t => t.Satellite.InitializationError != Sgp4Error.None))
		{
			Note(failures, -1, new(item.Elements.NoradCatalogId, item.Satellite.InitializationError, 0.0));
		}

		T[] times = SampleTimes(options);
		int blockSize = (long)tracked.Length * times.Length <= options.MaximumCachedStates
			? System.Math.Max(1, tracked.Length)
			: System.Math.Max(1, options.MaximumCachedStates / times.Length / 2);

		Grid?[] grids = new Grid?[tracked.Length];
		ConcurrentBag<(long Order, int Index, ClosestApproach<T> Approach)> found = [];
		ConcurrentBag<(long Order, ScreeningFailure Failure)> pairFailures = [];
		ParallelOptions parallel = new() { CancellationToken = cancellationToken };
		long scanned = 0;

		progress?.Report(new ConjunctionScreenProgress(0, survivors.Count));

		foreach (IGrouping<int, PairIndex> row in survivors.GroupBy(p => p.First / blockSize))
		{
			foreach (IGrouping<int, PairIndex> tile in row.GroupBy(p => p.Second / blockSize).OrderBy(g => g.Key))
			{
				cancellationToken.ThrowIfCancellationRequested();

				PairIndex[] pairs = [.. tile];
				int[] missing = [.. pairs.SelectMany(p => new[] { p.First, p.Second }).Distinct().Where(k => grids[k] is null)];

				Parallel.ForEach(missing, parallel, k => grids[k] = BuildGrid(tracked[k], times));

				Parallel.ForEach(
					pairs,
					parallel,
					pair =>
					{
						PairScan scan = ScanPair(tracked[pair.First], grids[pair.First]!, tracked[pair.Second], grids[pair.Second]!, options);

						for (int i = 0; i < scan.Approaches.Count; i++)
						{
							found.Add((pair.Order, i, scan.Approaches[i]));
						}

						if (scan.Failure is ScreeningFailure failure)
						{
							pairFailures.Add((pair.Order, failure));
						}

						progress?.Report(new ConjunctionScreenProgress(Interlocked.Increment(ref scanned), survivors.Count));
					});

				if (tile.Key != row.Key)
				{
					foreach (PairIndex pair in pairs)
					{
						grids[pair.Second] = null;
					}
				}
			}

			Array.Clear(grids);
		}

		foreach ((long order, ScreeningFailure failure) in pairFailures)
		{
			Note(failures, order, failure);
		}

		ConjunctionEvent[] events =
		[
			.. found
				.OrderBy(f => f.Approach.MissDistanceKm)
				.ThenBy(f => f.Order)
				.ThenBy(f => f.Index)
				.Select(f => f.Approach.ToEvent()),
		];

		ScreeningFailure[] failureList = [.. failures.Values.Select(f => f.Failure).OrderBy(f => f.CatalogId)];

		return new ConjunctionScreenResult(events, pairCount, survivors.Count, failureList);
	}

	/// <summary>
	/// Propagates one object over the scan's grid, stopping at the first sample the model refuses.
	/// </summary>
	private Grid BuildGrid(Tracked item, T[] times)
	{
		TemeState<T>[] states = new TemeState<T>[times.Length];

		for (int i = 0; i < times.Length; i++)
		{
			Interlocked.Increment(ref scanPropagations);
			Sgp4Result<T> result = Sgp4<T>.Propagate(item.Satellite, item.MinutesToWindowStart + times[i], Math);

			if (!result.IsSuccess)
			{
				return new Grid(times, states, i, new(item.Elements.NoradCatalogId, result.Error, ToDouble(times[i])));
			}

			states[i] = result.State;
		}

		return new Grid(times, states, times.Length, null);
	}

	private PairScan ScanPair(Tracked primary, Grid primaryGrid, Tracked secondary, Grid secondaryGrid, ConjunctionScreenOptions options)
	{
		List<ClosestApproach<T>> approaches = [];
		T threshold = T.CreateChecked(options.ThresholdKm);
		T[] times = primaryGrid.Times;

		Sample previous = default;

		for (int i = 0; i < times.Length; i++)
		{
			Sample current = SampleAt(primaryGrid, secondaryGrid, i);

			if (current.Failure is ScreeningFailure failure)
			{
				return new PairScan(approaches, failure);
			}

			if (i > 0 && previous.RangeRate < 0.0 && current.RangeRate >= 0.0 && CanDipBelow(previous, current, options))
			{
				ClosestApproach<T>? approach = Refine(primary, secondary, previous.Time, current.Time, options);

				if (approach is ClosestApproach<T> found && found.MissDistanceKm <= threshold)
				{
					approaches.Add(found);
				}
			}

			previous = current;
		}

		return new PairScan(approaches, null);
	}

	/// <summary>
	/// Whether the range over a bracket could fall to the threshold, bounded from the two end samples.
	/// </summary>
	private static bool CanDipBelow(Sample start, Sample end, ConjunctionScreenOptions options)
	{
		double seconds = (ToDouble(end.Time) - ToDouble(start.Time)) * SecondsPerMinute;
		double speedBound = System.Math.Max(start.RelativeSpeed, end.RelativeSpeed) + (RelativeAccelerationBound * seconds);

		// The range is at least each end's range less the furthest it could have moved since or
		// will move until; the lowest point of the larger of those two lines is this.
		double lowestPossible = (start.Range + end.Range - (speedBound * seconds)) / 2.0;

		return lowestPossible <= options.ThresholdKm;
	}

	/// <remarks>
	/// The primary's failure is checked first, as when the pair propagated the primary first: a
	/// pair whose objects both fail at one sample reports the primary.
	/// </remarks>
	private static Sample SampleAt(Grid primary, Grid secondary, int index)
	{
		T time = primary.Times[index];

		if (index >= primary.Count)
		{
			return new Sample(time, 0.0, 0.0, 0.0, primary.Failure);
		}

		if (index >= secondary.Count)
		{
			return new Sample(time, 0.0, 0.0, 0.0, secondary.Failure);
		}

		Relative relative = Relative.Of(primary.States[index], secondary.States[index]);

		return new Sample(
			time,
			ToDouble(relative.RangeRateTimesRange),
			System.Math.Sqrt(ToDouble(relative.RangeSquared)),
			System.Math.Sqrt(ToDouble(relative.SpeedSquared)),
			null);
	}

	private ClosestApproach<T>? Refine(Tracked primary, Tracked secondary, T low, T high, ConjunctionScreenOptions options)
	{
		T tolerance = T.CreateChecked(options.TimeToleranceMinutes);
		T two = T.CreateChecked(2);

		for (int i = 0; i < MaximumBisections && high - low > tolerance; i++)
		{
			T middle = Math.ToWorkingPrecision((low + high) / two);

			// A storage type too coarse to split the bracket further has found its answer.
			if (middle <= low || middle >= high)
			{
				break;
			}

			(Sgp4Result<T> a, Sgp4Result<T> b) = PropagateBoth(primary, secondary, middle);

			if (!a.IsSuccess || !b.IsSuccess)
			{
				return null;
			}

			if (Relative.Of(a.State, b.State).RangeRateTimesRange < T.Zero)
			{
				low = middle;
			}
			else
			{
				high = middle;
			}
		}

		T time = Math.ToWorkingPrecision((low + high) / two);
		(Sgp4Result<T> first, Sgp4Result<T> second) = PropagateBoth(primary, secondary, time);

		if (!first.IsSuccess || !second.IsSuccess)
		{
			return null;
		}

		Relative closest = Relative.Of(first.State, second.State);

		return new ClosestApproach<T>(
			primary.Elements.NoradCatalogId,
			secondary.Elements.NoradCatalogId,
			time,
			first.State,
			second.State,
			Math.Sqrt(closest.RangeSquared),
			Math.Sqrt(closest.SpeedSquared));
	}

	private (Sgp4Result<T> Primary, Sgp4Result<T> Secondary) PropagateBoth(Tracked primary, Tracked secondary, T time)
	{
		Interlocked.Add(ref refinePropagations, 2);

		return (
			Sgp4<T>.Propagate(primary.Satellite, primary.MinutesToWindowStart + time, Math),
			Sgp4<T>.Propagate(secondary.Satellite, secondary.MinutesToWindowStart + time, Math));
	}

	/// <summary>One object, initialized once per screen, with its offset from the window start.</summary>
	private sealed record Tracked(ElementSet Elements, Sgp4Satellite<T> Satellite, T MinutesToWindowStart, OrbitShell Shell);

	/// <summary>
	/// One object's states on the scan's grid: the first <paramref name="Count"/> are good, and if
	/// that is short of the grid, <paramref name="Failure"/> is why.
	/// </summary>
	private sealed record Grid(T[] Times, TemeState<T>[] States, int Count, ScreeningFailure? Failure);

	/// <summary>A pair that survived the pre-filter, with its place in the pair order.</summary>
	private readonly record struct PairIndex(int First, int Second, long Order);

	/// <summary>The relative state of two objects, reduced to what the search reads.</summary>
	private readonly record struct Relative(T RangeSquared, T RangeRateTimesRange, T SpeedSquared)
	{
		public static Relative Of(TemeState<T> a, TemeState<T> b)
		{
			// Signed componentwise differences, in T. This is the cancellation the screen exists to
			// expose; nothing here passes through a magnitude form, whose subtraction is absolute.
			T dx = b.X - a.X;
			T dy = b.Y - a.Y;
			T dz = b.Z - a.Z;
			T dvx = b.VelocityX - a.VelocityX;
			T dvy = b.VelocityY - a.VelocityY;
			T dvz = b.VelocityZ - a.VelocityZ;

			return new Relative(
				Dot(dx, dy, dz, dx, dy, dz),
				Dot(dx, dy, dz, dvx, dvy, dvz),
				Dot(dvx, dvy, dvz, dvx, dvy, dvz));
		}
	}

	/// <summary>One grid sample, in <see langword="double"/>, which is all the scan needs.</summary>
	private readonly record struct Sample(T Time, double RangeRate, double Range, double RelativeSpeed, ScreeningFailure? Failure);

	private readonly record struct PairScan(List<ClosestApproach<T>> Approaches, ScreeningFailure? Failure);
}
