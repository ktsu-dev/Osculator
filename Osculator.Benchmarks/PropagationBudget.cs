// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Benchmarks;

using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;

/// <summary>
/// The M3 gate: one <see cref="PreciseNumber"/> propagation has a stated budget, and blowing it by an
/// order of magnitude fails the build.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately not a BenchmarkDotNet run. BenchmarkDotNet on a shared runner measures the
/// runner, and a gate built on its means would either be loose enough to be useless or flaky. The
/// regression this exists to catch is not a few percent: it is trap 9, where dropping
/// <c>ToWorkingPrecision</c> took initialization from tens of milliseconds to 18.8 seconds and a
/// single propagation past fifteen minutes. A factor of ten over budget separates that from runner
/// noise by a wide margin on both sides.
/// </para>
/// <para>
/// The figure taken is the <em>fastest</em> of several timed runs after a warm-up. Noise on a shared
/// machine only ever adds time, so the minimum is the estimate least disturbed by it, and the first
/// call is excluded because it pays for JIT compilation of the whole generic propagator.
/// </para>
/// </remarks>
public static class PropagationBudget
{
	/// <summary>The command-line switch that runs this gate instead of BenchmarkDotNet.</summary>
	public const string Switch = "--budget";

	/// <summary>
	/// The budget for one <see cref="PreciseNumber"/> initialization plus one-day propagation, in
	/// milliseconds, at <see cref="Sgp4Benchmarks.ReferenceDigits"/> significant digits.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The budget is what makes the precise path usable as the spec intends it: an on-demand reference
	/// for the one object a user has selected, computed when they ask rather than precomputed. A tenth
	/// of a second is the point past which that stops feeling immediate.
	/// </para>
	/// <para>
	/// Measured at 9 ms near-earth and 21 ms deep-space under BenchmarkDotNet, and at 15 and 33 ms as
	/// this gate times them on the same machine, so the budget carries about a factor of three for a
	/// slower runner and the gate a factor of thirty. See <c>CLAUDE.md</c> for the figures.
	/// </para>
	/// </remarks>
	public const double BudgetMilliseconds = 100.0;

	/// <summary>How many times the budget a run has to take before the gate fails.</summary>
	public const double FailureFactor = 10.0;

	/// <summary>Timed runs per orbit after the warm-up.</summary>
	private const int TimedRuns = 5;

	/// <summary>
	/// Times the reference propagation over every benchmark orbit and reports against the budget.
	/// </summary>
	/// <returns>Zero when every orbit is within <see cref="FailureFactor"/> of the budget, one otherwise.</returns>
	public static int Run()
	{
		PreciseStorageMath precise = new(Sgp4Benchmarks.ReferenceDigits);
		PreciseNumber minutes = BenchmarkOrbits.MinutesSinceEpoch.ToPreciseNumber();
		double ceiling = BudgetMilliseconds * FailureFactor;
		bool withinBudget = true;

		Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
			$"M3 gate: one PreciseNumber({Sgp4Benchmarks.ReferenceDigits}) initialize + {BenchmarkOrbits.MinutesSinceEpoch:F0}-minute propagation, budget {BudgetMilliseconds:F0} ms, fails above {ceiling:F0} ms"));

		foreach (BenchmarkOrbit orbit in Enum.GetValues<BenchmarkOrbit>())
		{
			ElementSet elements = BenchmarkOrbits.Elements(orbit);

			double preciseMs = Fastest(() =>
			{
				Sgp4Satellite<PreciseNumber> satellite = Sgp4<PreciseNumber>.Initialize(elements, precise);
				Sgp4Result<PreciseNumber> result = Sgp4<PreciseNumber>.Propagate(satellite, minutes, precise);
				return result.IsSuccess;
			}, ceiling);

			double doubleMs = Fastest(() =>
			{
				Sgp4Satellite<double> satellite = Sgp4<double>.Initialize(elements, DoubleStorageMath.Instance);
				Sgp4Result<double> result = Sgp4<double>.Propagate(satellite, BenchmarkOrbits.MinutesSinceEpoch, DoubleStorageMath.Instance);
				return result.IsSuccess;
			}, ceiling);

			bool passed = preciseMs <= ceiling;
			withinBudget &= passed;

			string preciseText = double.IsPositiveInfinity(preciseMs)
				? string.Create(CultureInfo.InvariantCulture, $"did not finish within {ceiling:F0} ms")
				: string.Create(CultureInfo.InvariantCulture, $"{preciseMs,10:F3} ms   double {doubleMs,8:F4} ms   ratio {preciseMs / doubleMs,6:F0}x");

			Console.WriteLine($"  {orbit,-10} precise {preciseText}   {(passed ? "ok" : "OVER BUDGET")}");
		}

		if (!withinBudget)
		{
			Console.Error.WriteLine("The PreciseNumber propagation is over ten times its budget. Check that everything the propagator accumulates still goes through ToWorkingPrecision (CLAUDE.md, trap 9) before raising the budget.");
		}

		return withinBudget ? 0 : 1;
	}

	/// <summary>Runs an operation once to warm up, then reports the fastest of several timed runs.</summary>
	/// <param name="operation">The operation, which reports whether the model succeeded.</param>
	/// <param name="ceilingMilliseconds">How long any one run may take before the measurement is abandoned.</param>
	/// <returns>The fastest run in milliseconds, or positive infinity when a run outlasted the ceiling.</returns>
	/// <remarks>
	/// Every run is bounded, warm-up included. The regression this gate exists for does not make a
	/// propagation slow, it makes it not finish: trap 9 measured a single one still running after
	/// fifteen minutes. An unbounded gate would report that as a CI timeout naming no cause, so a run
	/// that outlasts the ceiling is abandoned on its pool thread and reported as over budget, and the
	/// process exits underneath it.
	/// </remarks>
	private static double Fastest(Func<bool> operation, double ceilingMilliseconds)
	{
		TimeSpan ceiling = TimeSpan.FromMilliseconds(ceilingMilliseconds);
		Task<bool> warmUp = Task.Run(operation);
		if (!warmUp.Wait(ceiling))
		{
			return double.PositiveInfinity;
		}

		if (!warmUp.Result)
		{
			throw new InvalidOperationException("A benchmark orbit failed to propagate; the gate would be timing an early return.");
		}

		double fastest = double.MaxValue;
		for (int i = 0; i < TimedRuns; i++)
		{
			long start = Stopwatch.GetTimestamp();
			if (!Task.Run(operation).Wait(ceiling))
			{
				return double.PositiveInfinity;
			}

			fastest = Math.Min(fastest, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
		}

		return fastest;
	}
}
