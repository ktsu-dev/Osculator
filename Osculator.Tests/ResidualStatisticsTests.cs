// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Residuals;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers RMS and percentiles over an arc, and spec §1 demo 5: the same statistic accumulated
/// naively in <see langword="double"/> and exactly in <c>PreciseNumber</c>.
/// </summary>
[TestClass]
public sealed class ResidualStatisticsTests
{
	/// <summary>The arc length the spec names: a million residuals.</summary>
	private const int ArcLength = 1_000_000;

	/// <summary>
	/// Residual <c>i</c> of the arc is <c>((i mod 1000) + 1) / 1000</c> km: one metre to one
	/// kilometre, in whole metres, over and over.
	/// </summary>
	/// <remarks>
	/// Chosen so the exact answer is known in closed form rather than computed by the thing under
	/// test. Each block of a thousand contributes <c>Σk² / 10⁶ = 333,833,500 / 10⁶</c> km², so the arc
	/// sums to exactly <c>333,833.5</c> km². Every residual is <c>k / 1000.0</c>, the double nearest the
	/// whole number of metres, whose shortest round-tripping text is that number of metres exactly — so
	/// the <c>PreciseNumber</c> run sums the very values the closed form describes.
	/// </remarks>
	private static double ResidualKilometers(int i) => ((i % 1000) + 1) / 1000.0;

	[TestMethod]
	public void PreciseAccumulationOfAMillionResidualsIsExact()
	{
		PreciseNumber[] block = new PreciseNumber[1000];

		for (int k = 0; k < block.Length; k++)
		{
			block[k] = ResidualKilometers(k).ToPreciseNumber();
		}

		ResidualStatistics<PreciseNumber> precise = new(PreciseStorageMath.Instance);

		for (int i = 0; i < ArcLength; i++)
		{
			precise.Add(block[i % 1000]);
		}

		PreciseNumber exact = 667_667.ToPreciseNumber() / 2.ToPreciseNumber();

		// Equality, not a tolerance. Anything short of every digit would be a rounding error the
		// class claims not to make.
		Assert.AreEqual(exact, precise.SumOfSquares);
		Assert.AreEqual(ArcLength, precise.Count);

		double rms = double.CreateChecked(precise.Rms);
		Assert.AreEqual(Math.Sqrt(0.3338335), rms, 1e-16);
	}

	[TestMethod]
	public void NaiveDoubleAccumulationOfTheSameResidualsLosesDigits()
	{
		ResidualStatistics<double> naive = new(DoubleStorageMath.Instance);

		for (int i = 0; i < ArcLength; i++)
		{
			naive.Add(ResidualKilometers(i));
		}

		const double exact = 333_833.5;
		double relativeError = Math.Abs(naive.SumOfSquares - exact) / exact;
		double digitsLost = Math.Log10(relativeError / Math.ScaleB(1.0, -53));

		Console.WriteLine($"double sum of squares: {naive.SumOfSquares:R}");
		Console.WriteLine($"exact:                 {exact:R}");
		Console.WriteLine($"relative error:        {relativeError:E3}");
		Console.WriteLine($"digits lost:           {digitsLost:F2}");

		// A correctly rounded result is within half an ulp, 1.1e-16. Summing a million terms
		// naively is not, and the gap is the demonstration.
		Assert.IsGreaterThan(5e-14, relativeError, $"Naive double summation lost less than expected: {relativeError:E3}.");
		Assert.IsLessThan(1e-9, relativeError, $"Naive double summation lost more than round-off explains: {relativeError:E3}.");
	}

	[TestMethod]
	public void PercentilesAreNearestRank()
	{
		ResidualStatistics<double> stats = new(DoubleStorageMath.Instance);

		// Added out of order, so the percentile has to sort.
		for (int m = 100; m >= 1; m--)
		{
			stats.Add(m / 1000.0);
		}

		Assert.AreEqual(0.050, stats.Percentile(0.50), 1e-15);
		Assert.AreEqual(0.090, stats.Percentile(0.90), 1e-15);
		Assert.AreEqual(0.095, stats.Percentile(0.945), 1e-15, "0.945 of 100 is rank 94.5, which rounds up.");
		Assert.AreEqual(0.001, stats.Percentile(0.001), 1e-15);
		Assert.AreEqual(0.100, stats.Maximum(), 1e-15);
	}

	[TestMethod]
	public void APercentileOutsideZeroToOneIsRefused()
	{
		ResidualStatistics<double> stats = new(DoubleStorageMath.Instance);
		stats.Add(1.0);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => stats.Percentile(0.0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => stats.Percentile(1.5));
	}

	[TestMethod]
	public void NothingAddedMeansNothingToRead()
	{
		ResidualStatistics<double> stats = new(DoubleStorageMath.Instance);

		Assert.ThrowsExactly<InvalidOperationException>(() => stats.Rms);
		Assert.ThrowsExactly<InvalidOperationException>(() => stats.Percentile(0.5));
	}

	[TestMethod]
	public void ComponentsAreAccumulatedSeparately()
	{
		ResidualStatistics<double> stats = new(DoubleStorageMath.Instance);

		stats.Add(new RswResidual<double>(3.0, 4.0, 0.0, 0.0, 0.0, 0.0));
		stats.Add(new RswResidual<double>(-3.0, -4.0, 12.0, 0.0, 0.0, 0.0));

		Assert.AreEqual(3.0, stats.RadialRms, 1e-15, "A negative component squares to the same contribution.");
		Assert.AreEqual(4.0, stats.AlongTrackRms, 1e-15);
		Assert.AreEqual(Math.Sqrt(72.0), stats.CrossTrackRms, 1e-15);

		// Magnitudes 5 and 13.
		Assert.AreEqual(Math.Sqrt((25.0 + 169.0) / 2.0), stats.Rms, 1e-15);
		Assert.AreEqual(5.0, stats.Percentile(0.5), 1e-15);
		Assert.AreEqual(13.0, stats.Maximum(), 1e-15);
	}

	[TestMethod]
	public void ThePreciseSumsAreNotReducedToTheWorkingPrecision()
	{
		// Thirty digits of working precision, and a sum that needs more than thirty to hold. If the
		// accumulator went through ToWorkingPrecision the small term would vanish.
		PreciseStorageMath thirty = new(30);
		ResidualStatistics<PreciseNumber> stats = new(thirty);

		PreciseNumber large = 1_000_000.ToPreciseNumber();
		PreciseNumber small = PreciseNumber.Parse("1e-20", System.Globalization.CultureInfo.InvariantCulture);

		stats.Add(large);
		stats.Add(small);

		Assert.AreEqual((large * large) + (small * small), stats.SumOfSquares);
	}
}
