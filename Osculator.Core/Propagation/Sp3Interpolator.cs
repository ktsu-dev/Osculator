// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System;
using System.Collections.Generic;
using System.Numerics;

/// <summary>
/// Lagrange interpolation through a precise orbit's tabulated positions: the IGS and ILRS standard
/// way of reading an SP3 file between its epochs.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// <strong>A tenth-order polynomial through the eleven tabulated points nearest the instant.</strong>
/// That is the convention the orbit producers assume when they choose a spacing — fifteen minutes
/// for GPS, two for the geodetic laser-ranging satellites — so it is the reading of the file the
/// published accuracy refers to. The order is a parameter because a test has to be able to prove
/// which polynomial is being fitted, not because another order is recommended.
/// </para>
/// <para>
/// <strong>It refuses to extrapolate.</strong> Inside the table a polynomial of this order is
/// accurate to a millimetre or better; outside it the same polynomial diverges within an epoch or
/// two, and nothing in its output says so. An instant before the first point or after the last
/// throws rather than returning a confident wrong position, which is the expensive failure mode
/// this repository keeps refusing to ship.
/// </para>
/// <para>
/// Near either end of the table the window cannot be centred, so it is slid inwards and the
/// polynomial is evaluated off-centre. That is less accurate than the middle of the table and the
/// held-out-epoch test measures by how much; it is still interpolation, never extrapolation,
/// because the instant is always inside the window.
/// </para>
/// <para>
/// <strong>It refuses across a gap, too.</strong> An SP3 file marks an epoch it has no position for,
/// and the parser drops it, so the table can have a hole of any length in the middle. A step longer
/// than <see cref="MaximumBridgedSpacings"/> times the nominal spacing (the median step) is a gap:
/// an instant inside one throws exactly as an instant past either end does, because a polynomial
/// evaluated many spacings from its nearest node is extrapolating whatever the table's ends say.
/// One missing epoch is bridged rather than refused. That doubles a step, and the held-out-epoch
/// test is precisely the measurement of how well a doubled step interpolates: millimetres.
/// </para>
/// <para>
/// Each run of the table between gaps is read as a table of its own. The window never spans a gap,
/// so beside one it slides inwards and is evaluated off-centre, exactly as at the table's ends, and
/// an instant one normal step from a gap reads what an interpolator over that run alone would read.
/// A run too short to carry the polynomial is refused throughout. <see cref="Covers"/> answers
/// whether an instant can be read, so a caller sweeping a range skips gaps the way it skips the ends.
/// </para>
/// <para>
/// Every product and sum is passed through <see cref="IStorageMath{T}.ToWorkingPrecision"/>, because
/// each weight is a product of ten time differences over ten more: exact in an arbitrary-precision
/// type, and therefore growing without bound if nothing trims it.
/// </para>
/// </remarks>
public sealed class Sp3Interpolator<T>
	where T : struct, INumber<T>
{
	/// <summary>The polynomial order IGS and ILRS orbits are interpolated with.</summary>
	public const int StandardOrder = 10;

	/// <summary>
	/// The longest step bridged, in nominal spacings: a step up to this long is one missing epoch and is
	/// interpolated across, and anything longer than half a spacing beyond it is a gap.
	/// </summary>
	public const int MaximumBridgedSpacings = 2;

	private readonly TabulatedPosition<T>[] points;
	private readonly IStorageMath<T> math;

	/// <summary>For each point, the index of the first point of the run between gaps it belongs to.</summary>
	private readonly int[] runStart;

	/// <summary>For each point, the index of the last point of the run between gaps it belongs to.</summary>
	private readonly int[] runEnd;

	/// <summary>
	/// Initializes a new instance of the <see cref="Sp3Interpolator{T}"/> class.
	/// </summary>
	/// <param name="points">The tabulated positions, in strictly increasing time order.</param>
	/// <param name="math">The storage type's functions, used here only to bound working precision.</param>
	/// <param name="order">The polynomial order; the window holds one more point than this.</param>
	/// <exception cref="ArgumentOutOfRangeException">The order is below one.</exception>
	/// <exception cref="ArgumentException">
	/// There are too few points for the order, or their times are not strictly increasing.
	/// </exception>
	public Sp3Interpolator(IReadOnlyList<TabulatedPosition<T>> points, IStorageMath<T> math, int order = StandardOrder)
	{
		Ensure.NotNull(points);
		Ensure.NotNull(math);

		if (order < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(order), order, "An interpolating polynomial needs an order of at least one.");
		}

		if (points.Count < order + 1)
		{
			throw new ArgumentException(
				$"An order-{order} polynomial needs {order + 1} points and the table has {points.Count}.",
				nameof(points));
		}

		this.points = new TabulatedPosition<T>[points.Count];

		for (int i = 0; i < points.Count; i++)
		{
			// A repeated or reversed time would put a zero, or a sign error, into a denominator. A
			// repeat is not harmless even when the positions agree: the weight is a division by it.
			if (i > 0 && points[i].Seconds <= points[i - 1].Seconds)
			{
				throw new ArgumentException(
					$"The table's times must strictly increase; point {i} is at {points[i].Seconds} s, after {points[i - 1].Seconds} s.",
					nameof(points));
			}

			this.points[i] = points[i];
		}

		this.math = math;
		Order = order;
		NominalSpacing = MedianStep(this.points);
		(runStart, runEnd) = Runs(this.points, NominalSpacing);
	}

	/// <summary>Gets the polynomial order.</summary>
	public int Order { get; }

	/// <summary>Gets the time of the first tabulated point, in seconds after the table's reference.</summary>
	public T FirstSeconds => points[0].Seconds;

	/// <summary>Gets the time of the last tabulated point, in seconds after the table's reference.</summary>
	public T LastSeconds => points[^1].Seconds;

	/// <summary>
	/// Gets the table's nominal spacing: the median of its steps, which one missing epoch or a few
	/// gaps do not move.
	/// </summary>
	public T NominalSpacing { get; }

	/// <summary>
	/// Determines whether an instant can be interpolated: inside the table, not in a gap, and in a run
	/// between gaps long enough to carry the polynomial.
	/// </summary>
	/// <param name="seconds">The instant, in seconds after the table's reference.</param>
	/// <returns><see langword="true"/> when <see cref="At"/> would answer rather than throw.</returns>
	public bool Covers(T seconds) => Refusal(seconds, out _) is null;

	/// <summary>
	/// Interpolates the position at an instant inside the table.
	/// </summary>
	/// <param name="seconds">The instant, in seconds after the table's reference.</param>
	/// <returns>The interpolated position, carrying the instant asked for.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The instant is outside the table, inside a gap, or in a run between gaps too short for the order.
	/// </exception>
	public TabulatedPosition<T> At(T seconds)
	{
		string? refusal = Refusal(seconds, out int index);

		if (refusal is not null)
		{
			throw new ArgumentOutOfRangeException(nameof(seconds), seconds, refusal);
		}

		int size = Order + 1;
		int start = System.Math.Clamp(index - (size / 2), runStart[index], runEnd[index] - size + 1);

		T x = T.Zero;
		T y = T.Zero;
		T z = T.Zero;

		for (int k = start; k < start + size; k++)
		{
			T numerator = T.One;
			T denominator = T.One;

			for (int j = start; j < start + size; j++)
			{
				if (j == k)
				{
					continue;
				}

				numerator = math.ToWorkingPrecision(numerator * (seconds - points[j].Seconds));
				denominator = math.ToWorkingPrecision(denominator * (points[k].Seconds - points[j].Seconds));
			}

			// On a tabulated instant every other weight has a factor of exactly zero and this one is
			// a product divided by itself, so the table's own value comes back without a special case.
			T weight = math.ToWorkingPrecision(numerator / denominator);

			x = math.ToWorkingPrecision(x + (weight * points[k].X));
			y = math.ToWorkingPrecision(y + (weight * points[k].Y));
			z = math.ToWorkingPrecision(z + (weight * points[k].Z));
		}

		return new TabulatedPosition<T>(seconds, x, y, z);
	}

	/// <summary>The median step of a table, which is what a gap is measured against.</summary>
	/// <param name="table">The points, in strictly increasing time order.</param>
	/// <returns>The median step; the lower of the two middle ones for an even count.</returns>
	private static T MedianStep(TabulatedPosition<T>[] table)
	{
		T[] steps = new T[table.Length - 1];

		for (int i = 0; i < steps.Length; i++)
		{
			steps[i] = table[i + 1].Seconds - table[i].Seconds;
		}

		Array.Sort(steps);
		return steps[(steps.Length - 1) / 2];
	}

	/// <summary>Splits a table into the runs between its gaps.</summary>
	/// <param name="table">The points, in strictly increasing time order.</param>
	/// <param name="nominal">The nominal spacing.</param>
	/// <returns>For each point, the first and last index of its run.</returns>
	private static (int[] Start, int[] End) Runs(TabulatedPosition<T>[] table, T nominal)
	{
		// A step is a gap beyond MaximumBridgedSpacings + 1/2 spacings, compared doubled so the half is
		// exact in an integer storage type as well: 2·step > (2·bridged + 1)·nominal.
		T limit = T.CreateChecked((2 * MaximumBridgedSpacings) + 1) * nominal;
		int[] start = new int[table.Length];
		int[] end = new int[table.Length];

		for (int i = 1; i < table.Length; i++)
		{
			T step = table[i].Seconds - table[i - 1].Seconds;
			start[i] = step + step > limit ? i : start[i - 1];
		}

		end[^1] = table.Length - 1;

		for (int i = table.Length - 2; i >= 0; i--)
		{
			end[i] = start[i + 1] == start[i] ? end[i + 1] : i;
		}

		return (start, end);
	}

	/// <summary>Why an instant cannot be interpolated, if it cannot.</summary>
	/// <param name="seconds">The instant.</param>
	/// <param name="index">The first point at or after the instant, when it can be.</param>
	/// <returns>The reason, or <see langword="null"/> when the instant can be interpolated.</returns>
	private string? Refusal(T seconds, out int index)
	{
		index = 0;

		if (seconds < FirstSeconds || seconds > LastSeconds)
		{
			return $"The table covers {FirstSeconds} s to {LastSeconds} s; refusing to extrapolate.";
		}

		index = IndexAtOrAfter(seconds);

		// Strictly between two points that belong to different runs is inside a gap. On a point is
		// inside that point's run, so a gap's two edges are read from their own sides.
		if (points[index].Seconds != seconds && runStart[index] == index)
		{
			return $"{seconds} s falls in a gap from {points[index - 1].Seconds} s to {points[index].Seconds} s, "
				+ $"longer than {MaximumBridgedSpacings} spacings of {NominalSpacing} s; refusing to extrapolate across it.";
		}

		int length = runEnd[index] - runStart[index] + 1;

		if (length < Order + 1)
		{
			return $"{seconds} s is in a run of {length} points between gaps, from {points[runStart[index]].Seconds} s "
				+ $"to {points[runEnd[index]].Seconds} s; an order-{Order} polynomial needs {Order + 1}.";
		}

		return null;
	}

	/// <summary>Binary search for the first point at or after an instant.</summary>
	/// <param name="seconds">The instant.</param>
	/// <returns>Its index.</returns>
	private int IndexAtOrAfter(T seconds)
	{
		int low = 0;
		int high = points.Length - 1;

		while (low < high)
		{
			int middle = (low + high) / 2;

			if (points[middle].Seconds < seconds)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}

		return low;
	}
}
