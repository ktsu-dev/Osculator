// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Panels;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Osculator.Core.Forces;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;

/// <summary>
/// Demonstration 4: a thirty-day two-body Cowell arc integrated in every storage type, each measured
/// against a thirty-digit run of the same integration.
/// </summary>
/// <remarks>
/// <para>
/// This is the measurement <c>CowellRoundOffTests</c> makes, with the same inputs, so the panel and
/// the test measure the same thing: fixed 120-second steps, μ = 398600.4375 (exact in all four
/// types, unlike EGM96's 398600.4418), and a 7000 km orbit whose every component is exact in all
/// four. What differs is the number of checkpoints: the test reads four, and the plot reads one at
/// every point of <see cref="CheckpointSteps"/>. Two-body gravity ignores its time argument, so
/// stopping at a checkpoint and carrying on cannot change the answer.
/// </para>
/// <para>
/// The <see cref="PreciseNumber"/> row is a forty-digit run measured against the thirty-digit
/// reference. It cannot be the reference against itself, which is zero on every row and so says
/// nothing; this way it is the reference's own round-off floor across the whole arc, which is the
/// figure every other row has to clear for the measurement to mean anything.
/// </para>
/// </remarks>
internal static class CowellRoundOffArc
{
	/// <summary>The fixed step, in seconds.</summary>
	internal const int StepSeconds = 120;

	/// <summary>Steps per day at that step.</summary>
	internal const int StepsPerDay = 86400 / StepSeconds;

	/// <summary>The working precision of the reference, in significant digits.</summary>
	internal const int ReferenceDigits = 30;

	/// <summary>The precision of the run the reference is checked against.</summary>
	internal const int ConvergenceDigits = 40;

	/// <summary>The gravitational parameter, in km³/s²: 398600 + 7/16, exact in all four types.</summary>
	internal const string GravitationalParameter = "398600.4375";

	/// <summary>The growth exponent the spec projected for round-off that random-walks as √t.</summary>
	internal const double SpecExponent = 0.5;

	/// <summary>The most steps integrated between two cancellation checks.</summary>
	private const int ChunkSteps = 180;

	/// <summary>Gets the checkpoints, in steps from the start: from an eighth of a day to thirty days.</summary>
	internal static IReadOnlyList<int> CheckpointSteps { get; } =
	[
		StepsPerDay / 8, StepsPerDay / 4, StepsPerDay / 2,
		StepsPerDay, 2 * StepsPerDay, 3 * StepsPerDay, 5 * StepsPerDay, 7 * StepsPerDay,
		10 * StepsPerDay, 15 * StepsPerDay, 20 * StepsPerDay, 30 * StepsPerDay,
	];

	/// <summary>Gets the index in <see cref="CheckpointSteps"/> of the first day.</summary>
	internal static int DayOneIndex { get; } = IndexOf(StepsPerDay);

	/// <summary>Gets the total number of steps every run integrates.</summary>
	internal static int ArcSteps => CheckpointSteps[^1];

	/// <summary>Gets the checkpoints in days, which is the plot's horizontal axis.</summary>
	internal static double[] CheckpointDays
	{
		get
		{
			double[] days = new double[CheckpointSteps.Count];
			for (int i = 0; i < days.Length; i++)
			{
				days[i] = (double)CheckpointSteps[i] / StepsPerDay;
			}

			return days;
		}
	}

	/// <summary>
	/// Integrates the arc in float, double, decimal and forty-digit PreciseNumber, and the thirty-digit
	/// reference, and measures each against the reference at every checkpoint.
	/// </summary>
	/// <param name="progress">Counts integrated steps across all five runs.</param>
	/// <param name="cancellationToken">Stops the runs between chunks of steps.</param>
	/// <returns>What the arc measured.</returns>
	internal static CowellRoundOffResult Measure(CowellRoundOffProgress progress, CancellationToken cancellationToken)
	{
		progress.Start(5L * ArcSteps);

		CartesianState<PreciseNumber>[] reference = [];
		CartesianState<float>[] single = [];
		CartesianState<double>[] twice = [];
		CartesianState<decimal>[] dec = [];
		CartesianState<PreciseNumber>[] wider = [];
		double[] seconds = new double[5];

		// Five independent integrations; the two PreciseNumber runs are nearly all of the cost, so
		// running side by side roughly halves the wait.
		ParallelOptions options = new() { CancellationToken = cancellationToken };
		Parallel.Invoke(
			options,
			() => reference = Timed(() => Run(new PreciseStorageMath(ReferenceDigits), progress, cancellationToken), seconds, 4),
			() => single = Timed(() => Run(FloatStorageMath.Instance, progress, cancellationToken), seconds, 0),
			() => twice = Timed(() => Run(DoubleStorageMath.Instance, progress, cancellationToken), seconds, 1),
			() => dec = Timed(() => Run(DecimalStorageMath.Instance, progress, cancellationToken), seconds, 2),
			() => wider = Timed(() => Run(new PreciseStorageMath(ConvergenceDigits), progress, cancellationToken), seconds, 3));

		cancellationToken.ThrowIfCancellationRequested();

		return new CowellRoundOffResult(
			[
				new StorageArc("float", Distances(reference, single), seconds[0]),
				new StorageArc("double", Distances(reference, twice), seconds[1]),
				new StorageArc("decimal", Distances(reference, dec), seconds[2]),
				new StorageArc(string.Create(CultureInfo.InvariantCulture, $"PreciseNumber ({ConvergenceDigits} digits)"), Distances(reference, wider), seconds[3]),
			],
			seconds[4]);
	}

	/// <summary>
	/// The growth exponent between the first and the thirtieth day, the way
	/// <c>CowellRoundOffTests</c> reads it: the slope of the line through the two on log-log axes.
	/// </summary>
	/// <param name="distances">Distances from the reference at each checkpoint, in km.</param>
	/// <returns>The exponent p in distance ∝ tᵖ.</returns>
	internal static double Exponent(IReadOnlyList<double> distances)
	{
		double firstDay = (double)CheckpointSteps[DayOneIndex] / StepsPerDay;
		double lastDay = (double)CheckpointSteps[^1] / StepsPerDay;
		return Math.Log(distances[^1] / distances[DayOneIndex]) / Math.Log(lastDay / firstDay);
	}

	private static int IndexOf(int steps)
	{
		for (int i = 0; i < CheckpointSteps.Count; i++)
		{
			if (CheckpointSteps[i] == steps)
			{
				return i;
			}
		}

		throw new InvalidOperationException("The checkpoints must include the first day.");
	}

	private static TResult Timed<TResult>(Func<TResult> work, double[] seconds, int slot)
	{
		long started = System.Diagnostics.Stopwatch.GetTimestamp();
		TResult result = work();
		seconds[slot] = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds;
		return result;
	}

	private static CartesianState<T>[] Run<T>(IStorageMath<T> math, CowellRoundOffProgress progress, CancellationToken cancellationToken)
		where T : struct, INumber<T>
	{
		Cowell<T> cowell = new(new TwoBody<T>(Parse<T>(GravitationalParameter), math), math);
		T step = T.CreateChecked(StepSeconds);
		CartesianState<T> state = Start<T>();
		CartesianState<T>[] checkpoints = new CartesianState<T>[CheckpointSteps.Count];

		int done = 0;
		for (int i = 0; i < CheckpointSteps.Count; i++)
		{
			while (done < CheckpointSteps[i])
			{
				cancellationToken.ThrowIfCancellationRequested();
				int chunk = Math.Min(ChunkSteps, CheckpointSteps[i] - done);
				state = cowell.PropagateFixedStep(state, step, chunk).State;
				done += chunk;
				progress.Add(chunk);
			}

			checkpoints[i] = state;
		}

		return checkpoints;
	}

	/// <summary>
	/// A low Earth orbit at 7000 km, inclined about 52°, every component exact in all four types.
	/// </summary>
	private static CartesianState<T> Start<T>()
		where T : struct, INumber<T> =>
		new(Parse<T>("7000"), T.Zero, T.Zero, T.Zero, Parse<T>("4.6875"), Parse<T>("5.90625"));

	private static T Parse<T>(string literal)
		where T : struct, INumber<T> =>
		T.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture);

	private static double[] Distances<T>(CartesianState<PreciseNumber>[] reference, CartesianState<T>[] other)
		where T : struct, INumber<T>
	{
		double[] result = new double[reference.Length];
		for (int i = 0; i < reference.Length; i++)
		{
			result[i] = Distance(reference[i], other[i]);
		}

		return result;
	}

	/// <summary>The distance between two positions, differenced in thirty digits and reported in km.</summary>
	private static double Distance<T>(CartesianState<PreciseNumber> reference, CartesianState<T> other)
		where T : struct, INumber<T>
	{
		double dx = double.CreateChecked(reference.X - Convert<T, PreciseNumber>(other.X));
		double dy = double.CreateChecked(reference.Y - Convert<T, PreciseNumber>(other.Y));
		double dz = double.CreateChecked(reference.Z - Convert<T, PreciseNumber>(other.Z));
		return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
	}

	private static TTo Convert<TFrom, TTo>(TFrom value)
		where TFrom : struct, INumber<TFrom>
		where TTo : struct, INumber<TTo> =>
		TTo.CreateChecked(value);
}

/// <summary>One storage type's distance from the reference across the arc.</summary>
/// <param name="StorageName">The storage type, as the panel names it.</param>
/// <param name="Distances">Distance from the reference at each of <see cref="CowellRoundOffArc.CheckpointSteps"/>, in km.</param>
/// <param name="Seconds">How long the run took.</param>
internal sealed record StorageArc(string StorageName, double[] Distances, double Seconds)
{
	/// <summary>Gets the growth exponent from the first day to the last.</summary>
	internal double Exponent => CowellRoundOffArc.Exponent(Distances);
}

/// <summary>What the thirty-day arc measured.</summary>
/// <param name="Runs">Float, double, decimal and forty-digit PreciseNumber, in that order.</param>
/// <param name="ReferenceSeconds">How long the thirty-digit reference took.</param>
internal sealed record CowellRoundOffResult(IReadOnlyList<StorageArc> Runs, double ReferenceSeconds);

/// <summary>Integrated steps across every run of a measurement, read by the UI thread while it runs.</summary>
internal sealed class CowellRoundOffProgress
{
	private long total;
	private long done;

	/// <summary>Gets the steps the measurement will integrate.</summary>
	internal long Total => Interlocked.Read(ref total);

	/// <summary>Gets the steps integrated so far.</summary>
	internal long Done => Interlocked.Read(ref done);

	/// <summary>Resets the count for a new measurement.</summary>
	/// <param name="steps">The steps it will integrate.</param>
	internal void Start(long steps)
	{
		Interlocked.Exchange(ref done, 0);
		Interlocked.Exchange(ref total, steps);
	}

	/// <summary>Records integrated steps.</summary>
	/// <param name="steps">How many.</param>
	internal void Add(int steps) => Interlocked.Add(ref done, steps);
}
