// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Forces;

using System;
using System.Collections.Generic;
using System.Numerics;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// A body's position interpolated from a table of positions, by Lagrange interpolation through the
/// nearest samples.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// Nine points by default. The Moon tabulated hourly is a smooth function over nine hours — a
/// tenth of its orbit at most — and an eighth-degree polynomial through it is good to far better
/// than the third-body acceleration needs; the test measures it against the analytic motion the
/// table was sampled from.
/// </para>
/// <para>
/// An instant outside the table is refused rather than extrapolated. A polynomial past its last
/// node diverges quickly, and an integration that ran off the end of its ephemeris has a wrong
/// table, not a reason to guess.
/// </para>
/// </remarks>
public sealed class TabulatedEphemeris<T> : IBodyEphemeris<T>
	where T : struct, INumber<T>
{
	private readonly T[] times;
	private readonly BodyPosition<T>[] positions;
	private readonly IStorageMath<T> math;

	/// <summary>Initializes a new instance of the <see cref="TabulatedEphemeris{T}"/> class.</summary>
	/// <param name="secondsSinceEpoch">The sample times, strictly increasing, in seconds since the integration's epoch.</param>
	/// <param name="samples">The positions at those times.</param>
	/// <param name="storageMath">The working precision for <typeparamref name="T"/>.</param>
	/// <param name="points">How many samples each interpolation passes through; at least 2.</param>
	/// <exception cref="ArgumentNullException">An argument is null.</exception>
	/// <exception cref="ArgumentException">
	/// The lists differ in length, hold fewer than <paramref name="points"/> samples, or the times do
	/// not increase.
	/// </exception>
	public TabulatedEphemeris(IReadOnlyList<T> secondsSinceEpoch, IReadOnlyList<BodyPosition<T>> samples, IStorageMath<T> storageMath, int points = 9)
	{
		Ensure.NotNull(secondsSinceEpoch);
		Ensure.NotNull(samples);
		Ensure.NotNull(storageMath);
		if (points < 2)
		{
			throw new ArgumentException("Interpolation needs at least two points.", nameof(points));
		}

		if (secondsSinceEpoch.Count != samples.Count)
		{
			throw new ArgumentException("There must be one position per time.", nameof(samples));
		}

		if (samples.Count < points)
		{
			throw new ArgumentException($"The table has {samples.Count} samples and each interpolation needs {points}.", nameof(samples));
		}

		times = new T[samples.Count];
		positions = new BodyPosition<T>[samples.Count];
		for (int i = 0; i < samples.Count; i++)
		{
			times[i] = secondsSinceEpoch[i];
			positions[i] = samples[i];
			if (i > 0 && times[i] <= times[i - 1])
			{
				throw new ArgumentException($"The times must increase; sample {i} does not.", nameof(secondsSinceEpoch));
			}
		}

		math = storageMath;
		Points = points;
	}

	/// <summary>Gets how many samples each interpolation passes through.</summary>
	public int Points { get; }

	/// <summary>Gets the first instant the table covers, in seconds since the epoch.</summary>
	public T Start => times[0];

	/// <summary>Gets the last instant the table covers, in seconds since the epoch.</summary>
	public T End => times[^1];

	/// <inheritdoc />
	/// <exception cref="ArgumentOutOfRangeException">The instant is outside the table.</exception>
	public BodyPosition<T> PositionAt(T secondsSinceEpoch)
	{
		if (secondsSinceEpoch < Start || secondsSinceEpoch > End)
		{
			throw new ArgumentOutOfRangeException(
				nameof(secondsSinceEpoch),
				$"{secondsSinceEpoch} s is outside the table, which covers {Start} s to {End} s.");
		}

		// The window of Points samples that most nearly centres on the instant.
		int after = Array.BinarySearch(times, secondsSinceEpoch);
		if (after < 0)
		{
			after = ~after;
		}

		int first = Math.Clamp(after - (Points / 2), 0, times.Length - Points);

		T x = T.Zero;
		T y = T.Zero;
		T z = T.Zero;
		for (int j = first; j < first + Points; j++)
		{
			T weight = T.One;
			for (int k = first; k < first + Points; k++)
			{
				if (k != j)
				{
					weight = math.ToWorkingPrecision(weight * (secondsSinceEpoch - times[k]) / (times[j] - times[k]));
				}
			}

			x = math.ToWorkingPrecision(x + (weight * positions[j].X));
			y = math.ToWorkingPrecision(y + (weight * positions[j].Y));
			z = math.ToWorkingPrecision(z + (weight * positions[j].Z));
		}

		return new(x, y, z);
	}
}
