// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Residuals;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the km/day error-growth fit.
/// </summary>
[TestClass]
public sealed class ErrorGrowthFitTests
{
	[TestMethod]
	public void AStraightLineIsRecoveredExactlyInPreciseNumber()
	{
		// 0.25 km at epoch, growing 1.5 km/day, sampled every quarter day for a week.
		PreciseNumber intercept = 0.25.ToPreciseNumber();
		PreciseNumber slope = 1.5.ToPreciseNumber();
		List<(PreciseNumber Days, PreciseNumber Kilometers)> points = [];

		for (int i = 0; i <= 28; i++)
		{
			PreciseNumber days = i.ToPreciseNumber() / 4.ToPreciseNumber();
			points.Add((days, intercept + (slope * days)));
		}

		ErrorGrowthFit<PreciseNumber> fit = ErrorGrowthFit<PreciseNumber>.Fit(points, PreciseStorageMath.Instance);

		Assert.AreEqual(slope, fit.KilometersPerDay);
		Assert.AreEqual(intercept, fit.InterceptKilometers);
		Assert.AreEqual(29, fit.Count);
	}

	[TestMethod]
	public void AnOffsetAtEpochIsNotFoldedIntoTheSlope()
	{
		// A residual that does not grow at all, but is not zero at the epoch. A fit through the
		// origin would report it as growing; this one has to say zero.
		List<(double Days, double Kilometers)> points = [(1.0, 2.0), (2.0, 2.0), (3.0, 2.0), (7.0, 2.0)];

		ErrorGrowthFit<double> fit = ErrorGrowthFit<double>.Fit(points, DoubleStorageMath.Instance);

		Assert.AreEqual(0.0, fit.KilometersPerDay, 1e-15);
		Assert.AreEqual(2.0, fit.InterceptKilometers, 1e-15);
	}

	[TestMethod]
	public void NoisyPointsGiveTheLeastSquaresLine()
	{
		// Noise about y = 1 + 2t. The least-squares line, worked by hand: mean t 1.5, mean y 4.0,
		// Σ(dt)² = 5, Σ dt·dy = 9.8, so the slope is 1.96 and the intercept 4.0 - 1.96 · 1.5 = 1.06.
		List<(double Days, double Kilometers)> points = [(0.0, 1.1), (1.0, 2.9), (2.0, 5.1), (3.0, 6.9)];

		ErrorGrowthFit<double> fit = ErrorGrowthFit<double>.Fit(points, DoubleStorageMath.Instance);

		Assert.AreEqual(1.96, fit.KilometersPerDay, 1e-12);
		Assert.AreEqual(1.06, fit.InterceptKilometers, 1e-12);
		Assert.AreEqual(1.06 + (1.96 * 10.0), fit.At(10.0), 1e-12);
	}

	[TestMethod]
	public void TheCentredFormSurvivesAHorizonFarFromZero()
	{
		// Horizons near 1e8 days: the one-pass form subtracts two numbers near 1e16 · n and keeps
		// nothing of the spread between them. The centred form only ever sees the spread.
		List<(double Days, double Kilometers)> points = [];

		for (int i = 0; i < 10; i++)
		{
			double days = 1e8 + i;
			points.Add((days, 3.0 * i));
		}

		ErrorGrowthFit<double> fit = ErrorGrowthFit<double>.Fit(points, DoubleStorageMath.Instance);

		Assert.AreEqual(3.0, fit.KilometersPerDay, 1e-9);
	}

	[TestMethod]
	public void TooFewPointsOrNoSpreadInTheHorizonIsRefused()
	{
		Assert.ThrowsExactly<ArgumentException>(
			() => ErrorGrowthFit<double>.Fit([(1.0, 1.0)], DoubleStorageMath.Instance));

		Assert.ThrowsExactly<ArgumentException>(
			() => ErrorGrowthFit<double>.Fit([(1.0, 1.0), (1.0, 2.0)], DoubleStorageMath.Instance));
	}
}
