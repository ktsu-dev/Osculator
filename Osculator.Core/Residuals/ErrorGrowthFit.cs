// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Residuals;

using System;
using System.Collections.Generic;
using System.Numerics;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// How fast a residual grows with the prediction horizon: a straight line through
/// |residual| against Δt.
/// </summary>
/// <typeparam name="T">The numeric storage type the fit is computed in.</typeparam>
/// <param name="KilometersPerDay">The slope, in kilometres of residual per day of horizon.</param>
/// <param name="InterceptKilometers">The residual the line gives at Δt = 0, in kilometres.</param>
/// <param name="Count">The number of points the line was fitted to.</param>
/// <remarks>
/// <para>
/// Spec §6 reports error growth in km/day, which is the number an operator actually asks about —
/// how much worse does the prediction get for each day it is pushed out.
/// </para>
/// <para>
/// <strong>The intercept is fitted, not pinned to zero.</strong> Against an independent truth the
/// residual at the epoch is not zero: a later element set, an SP3 orbit and the element set being
/// propagated are three different fits to the orbit and disagree at every instant, including the
/// first. Forcing the line through the origin would fold that offset into the slope and report a
/// growth rate for something that is not growing.
/// </para>
/// <para>
/// <strong>It is straight because that is the question, not because the error is.</strong>
/// Along-track error under a drag mismatch grows nearer the square of the horizon than linearly. A
/// km/day figure over an arc is the average rate across it, and comparing two arcs of different
/// lengths compares two averages.
/// </para>
/// </remarks>
public readonly record struct ErrorGrowthFit<T>(T KilometersPerDay, T InterceptKilometers, int Count)
	where T : struct, INumber<T>
{
	/// <summary>
	/// Fits the line by ordinary least squares.
	/// </summary>
	/// <param name="points">Each point's horizon in days and residual magnitude in kilometres.</param>
	/// <param name="math">The storage type's working precision.</param>
	/// <returns>The fitted line.</returns>
	/// <exception cref="ArgumentException">
	/// Fewer than two points, or every point at the same horizon, so no slope exists.
	/// </exception>
	/// <remarks>
	/// The centred two-pass form — subtract the means, then accumulate — rather than the one-pass
	/// normal equations. The one-pass form takes the difference of <c>Σt²</c> and <c>(Σt)²/n</c>,
	/// two large and nearly equal numbers, and in <see langword="double"/> that cancellation costs
	/// more digits than the statistic is worth. In <c>PreciseNumber</c> the two forms agree, which is
	/// the point of having it as the reference; the centred one is what keeps the fixed-width types
	/// honest enough to compare against it.
	/// </remarks>
	public static ErrorGrowthFit<T> Fit(IReadOnlyList<(T Days, T Kilometers)> points, IStorageMath<T> math)
	{
		Ensure.NotNull(points);
		Ensure.NotNull(math);

		if (points.Count < 2)
		{
			throw new ArgumentException("A slope needs at least two points.", nameof(points));
		}

		T count = T.CreateChecked(points.Count);
		T sumDays = T.Zero;
		T sumKilometers = T.Zero;

		for (int i = 0; i < points.Count; i++)
		{
			sumDays += points[i].Days;
			sumKilometers += points[i].Kilometers;
		}

		T meanDays = math.ToWorkingPrecision(sumDays / count);
		T meanKilometers = math.ToWorkingPrecision(sumKilometers / count);

		T sumSquaredDeviation = T.Zero;
		T sumCrossDeviation = T.Zero;

		for (int i = 0; i < points.Count; i++)
		{
			T dt = points[i].Days - meanDays;

			sumSquaredDeviation += dt * dt;
			sumCrossDeviation += dt * (points[i].Kilometers - meanKilometers);
		}

		if (sumSquaredDeviation == T.Zero)
		{
			throw new ArgumentException(
				"Every point is at the same horizon, so the residual has no rate of growth to fit.", nameof(points));
		}

		T slope = math.ToWorkingPrecision(sumCrossDeviation / sumSquaredDeviation);
		T intercept = math.ToWorkingPrecision(meanKilometers - (slope * meanDays));

		return new ErrorGrowthFit<T>(slope, intercept, points.Count);
	}

	/// <summary>Evaluates the fitted line.</summary>
	/// <param name="days">The horizon, in days.</param>
	/// <returns>The residual the line gives there, in kilometres.</returns>
	public T At(T days) => InterceptKilometers + (KilometersPerDay * days);
}
