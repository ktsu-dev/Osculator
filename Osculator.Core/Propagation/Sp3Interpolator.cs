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

	private readonly TabulatedPosition<T>[] points;
	private readonly IStorageMath<T> math;

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
	}

	/// <summary>Gets the polynomial order.</summary>
	public int Order { get; }

	/// <summary>Gets the time of the first tabulated point, in seconds after the table's reference.</summary>
	public T FirstSeconds => points[0].Seconds;

	/// <summary>Gets the time of the last tabulated point, in seconds after the table's reference.</summary>
	public T LastSeconds => points[^1].Seconds;

	/// <summary>
	/// Interpolates the position at an instant inside the table.
	/// </summary>
	/// <param name="seconds">The instant, in seconds after the table's reference.</param>
	/// <returns>The interpolated position, carrying the instant asked for.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The instant is outside the table.</exception>
	public TabulatedPosition<T> At(T seconds)
	{
		if (seconds < FirstSeconds || seconds > LastSeconds)
		{
			throw new ArgumentOutOfRangeException(
				nameof(seconds),
				seconds,
				$"The table covers {FirstSeconds} s to {LastSeconds} s; refusing to extrapolate.");
		}

		int size = Order + 1;
		int start = System.Math.Clamp(IndexAtOrAfter(seconds) - (size / 2), 0, points.Length - size);

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
