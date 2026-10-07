// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Residuals;

using System;
using System.Collections.Generic;
using System.Numerics;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// RMS and percentiles of residuals over an arc, accumulated in the storage type itself.
/// </summary>
/// <typeparam name="T">The numeric storage type the statistic is accumulated in.</typeparam>
/// <remarks>
/// <para>
/// <strong>The accumulator is the experiment.</strong> Spec §6 asks for these statistics to be
/// accumulated in <c>PreciseNumber</c> so that the statistic itself carries no round-off, and spec §1
/// demo 5 asks for the contrast. Both come from one class: instantiated over <c>PreciseNumber</c> the
/// sums are exact, because addition and multiplication are exact there and nothing here calls
/// <see cref="IStorageMath{T}.ToWorkingPrecision"/> on them; instantiated over <see langword="double"/>
/// the same code is naive summation, which is the thing being shown to lose digits. No compensation
/// algorithm is written in, deliberately. Kahan summation would make the <see langword="double"/>
/// path look better and the comparison mean less.
/// </para>
/// <para>
/// <strong>Keeping the sums exact does not let them grow without bound.</strong> Exact addition costs
/// digits only for the span of exponents being added together plus the logarithm of the count, not
/// per term, so a million squares of 50-digit residuals stays near a hundred and twenty digits. That
/// is unlike a chain of products, which is what <see cref="IStorageMath{T}.ToWorkingPrecision"/>
/// exists for. The squares stored for the percentiles are another matter: there are a million of
/// them and nothing reads them to more than the working precision, so those are reduced.
/// </para>
/// <para>
/// <strong>Squares, not magnitudes, are what is stored.</strong> The RMS needs only the sum of squares,
/// and a percentile can be read from the sorted squares and rooted once, because the square root is
/// monotone. Storing magnitudes would cost a square root per sample, which at fifty digits is most of
/// the time the whole class takes.
/// </para>
/// </remarks>
/// <param name="math">The storage type's square root and working precision.</param>
public sealed class ResidualStatistics<T>(IStorageMath<T> math)
	where T : struct, INumber<T>
{
	private readonly IStorageMath<T> math = Ensure.NotNull(math);
	private readonly List<T> squaredMagnitudes = [];
	private bool sorted = true;

	/// <summary>Gets the number of residuals added.</summary>
	public int Count => squaredMagnitudes.Count;

	/// <summary>Gets the sum of the squared magnitudes, in square kilometres.</summary>
	/// <remarks>Exact for <c>PreciseNumber</c>; naively summed for every fixed-width type.</remarks>
	public T SumOfSquares { get; private set; } = T.Zero;

	/// <summary>Gets the sum of the squared radial components, in square kilometres.</summary>
	public T RadialSumOfSquares { get; private set; } = T.Zero;

	/// <summary>Gets the sum of the squared along-track components, in square kilometres.</summary>
	public T AlongTrackSumOfSquares { get; private set; } = T.Zero;

	/// <summary>Gets the sum of the squared cross-track components, in square kilometres.</summary>
	public T CrossTrackSumOfSquares { get; private set; } = T.Zero;

	/// <summary>Gets the root mean square of the residual magnitudes, in kilometres.</summary>
	/// <exception cref="InvalidOperationException">No residual has been added.</exception>
	public T Rms => RootMeanSquare(SumOfSquares);

	/// <summary>Gets the root mean square of the radial components, in kilometres.</summary>
	/// <exception cref="InvalidOperationException">No residual has been added.</exception>
	/// <remarks>Zero for residuals added as bare magnitudes, which carry no components.</remarks>
	public T RadialRms => RootMeanSquare(RadialSumOfSquares);

	/// <summary>Gets the root mean square of the along-track components, in kilometres.</summary>
	/// <exception cref="InvalidOperationException">No residual has been added.</exception>
	/// <remarks>Zero for residuals added as bare magnitudes, which carry no components.</remarks>
	public T AlongTrackRms => RootMeanSquare(AlongTrackSumOfSquares);

	/// <summary>Gets the root mean square of the cross-track components, in kilometres.</summary>
	/// <exception cref="InvalidOperationException">No residual has been added.</exception>
	/// <remarks>Zero for residuals added as bare magnitudes, which carry no components.</remarks>
	public T CrossTrackRms => RootMeanSquare(CrossTrackSumOfSquares);

	/// <summary>Adds one residual, keeping its components.</summary>
	/// <param name="residual">The residual, in kilometres.</param>
	public void Add(RswResidual<T> residual)
	{
		T radial = residual.Radial * residual.Radial;
		T alongTrack = residual.AlongTrack * residual.AlongTrack;
		T crossTrack = residual.CrossTrack * residual.CrossTrack;

		RadialSumOfSquares += radial;
		AlongTrackSumOfSquares += alongTrack;
		CrossTrackSumOfSquares += crossTrack;

		AddSquare(radial + alongTrack + crossTrack);
	}

	/// <summary>Adds one residual given only as a magnitude.</summary>
	/// <param name="kilometers">The magnitude, in kilometres. Its sign is ignored, since it is squared.</param>
	public void Add(T kilometers) => AddSquare(kilometers * kilometers);

	/// <summary>Reads a percentile of the residual magnitudes.</summary>
	/// <param name="fraction">The percentile as a fraction, greater than zero and at most one.</param>
	/// <returns>The magnitude, in kilometres, that this fraction of the residuals do not exceed.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="fraction"/> is not in (0, 1].</exception>
	/// <exception cref="InvalidOperationException">No residual has been added.</exception>
	/// <remarks>
	/// The nearest-rank definition: the smallest residual that at least this fraction of them are no
	/// greater than. It is always one of the residuals, so it needs no interpolation between two of
	/// them — and interpolating would have to choose between interpolating the squares and
	/// interpolating the magnitudes, which differ. Its one square root is taken at the end.
	/// </remarks>
	public T Percentile(T fraction)
	{
		if (fraction <= T.Zero || fraction > T.One)
		{
			throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "A percentile is a fraction in (0, 1].");
		}

		RequireAny();

		if (!sorted)
		{
			squaredMagnitudes.Sort();
			sorted = true;
		}

		// ceil(fraction * n), as a 1-based rank. Computed in T so a PreciseNumber fraction is not
		// rounded through double on the way to an index.
		T scaled = fraction * T.CreateChecked(Count);
		int rank = int.CreateTruncating(scaled);

		if (T.CreateChecked(rank) < scaled)
		{
			rank++;
		}

		rank = Math.Clamp(rank, 1, Count);

		return math.ToWorkingPrecision(math.Sqrt(squaredMagnitudes[rank - 1]));
	}

	/// <summary>Gets the largest residual magnitude, in kilometres.</summary>
	/// <returns>The maximum.</returns>
	/// <exception cref="InvalidOperationException">No residual has been added.</exception>
	public T Maximum() => Percentile(T.One);

	private void AddSquare(T square)
	{
		SumOfSquares += square;

		T stored = math.ToWorkingPrecision(square);

		if (sorted && squaredMagnitudes.Count > 0 && stored < squaredMagnitudes[^1])
		{
			sorted = false;
		}

		squaredMagnitudes.Add(stored);
	}

	private T RootMeanSquare(T sumOfSquares)
	{
		RequireAny();

		return math.ToWorkingPrecision(math.Sqrt(sumOfSquares / T.CreateChecked(Count)));
	}

	private void RequireAny()
	{
		if (Count == 0)
		{
			throw new InvalidOperationException("No residual has been added, so there is no statistic to read.");
		}
	}
}
