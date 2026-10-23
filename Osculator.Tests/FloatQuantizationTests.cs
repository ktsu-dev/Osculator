// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ktsu.Osculator.App.Panels;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Residuals;
using ktsu.Osculator.Data.Sp3;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Spec demonstration 2: an SGP4 residual against a laser-ranging orbit cannot be represented in
/// <see langword="float"/>, measured on the committed LAGEOS-1 SP3 hour.
/// </summary>
/// <remarks>
/// <para>
/// LAGEOS sits about 12,270 km from the geocentre, where consecutive <see langword="float"/> values
/// are 2⁻¹⁰ km apart: 0.9766 m. The spec quotes half a metre, which is the figure for a low orbit at
/// 7,000 km, one binade lower. Every TEME component of a <see langword="float"/> state is a whole
/// multiple of the spacing at its own magnitude, and the difference of two nearby ones is exact, so
/// the residual is a whole number of steps — not approximately, exactly.
/// </para>
/// <para>
/// The residual itself moves by millimetres between samples five seconds apart. So the
/// <see langword="double"/> run follows it smoothly, the <see langword="double"/> states rounded to
/// <see langword="float"/> make a staircase a step high, and the run done in <see langword="float"/>
/// end to end scatters across a handful of bands either side, because the propagator's own rounding
/// lands on the same lattice.
/// </para>
/// </remarks>
[TestClass]
public sealed class FloatQuantizationTests
{
	/// <summary>2⁻¹⁰ km in metres: the <see langword="float"/> spacing from 8,192 to 16,384 km.</summary>
	private const double LageosStepMeters = 1000.0 / 1024.0;

	/// <summary>Five seconds: closer than the panel draws, since nothing here draws.</summary>
	private const double SpacingSeconds = 5.0;

	private static readonly Lazy<FloatQuantizationResult> Measured = new(
		() => FloatQuantization.Compute(SpacingSeconds, CancellationToken.None));

	private static FloatQuantizationResult Result => Measured.Value;

	[TestMethod]
	public void TheStepAtLageosRadiusIsTwoToTheMinusTenKilometres()
	{
		Console.WriteLine($"float step at LAGEOS's mean radius: {Result.MagnitudeStepMeters:F7} m");

		Assert.AreEqual(LageosStepMeters, Result.MagnitudeStepMeters);

		// No component of a 12,270 km vector can be held more coarsely than the vector itself.
		foreach (QuantizedComponent component in Result.Components)
		{
			Assert.IsLessThanOrEqualTo(LageosStepMeters, component.LargestStep, component.Name);
		}

		Assert.AreEqual(LageosStepMeters, Result.Components.Max(c => c.LargestStep), "Some component spends the hour beyond 8,192 km.");
	}

	[TestMethod]
	public void TheFloatResidualIsAWholeNumberOfStepsInEveryComponent()
	{
		foreach (QuantizedComponent component in Result.Components)
		{
			for (int i = 0; i < component.Float.Length; i++)
			{
				double steps = component.Float[i] / component.Step[i];
				Assert.AreEqual(Math.Round(steps), steps, $"{component.Name} at sample {i}: {component.Float[i]} m is {steps} steps of {component.Step[i]} m.");
			}

			Console.WriteLine(
				$"{component.Name}: {component.Float.Length} samples, {component.DistinctFloatLevels} float levels, worst float − double {component.WorstFloatError:F3} m");

			// The double run takes a different value at every sample; the float run repeats itself.
			Assert.IsLessThan(component.Float.Length / 2, component.DistinctFloatLevels, component.Name);
			Assert.AreEqual(component.Double.Length, new HashSet<double>(component.Double).Count, component.Name);
		}
	}

	[TestMethod]
	public void SuccessiveFloatResidualsDifferByWholeSteps()
	{
		QuantizedComponent x = Result.Components[0];

		for (int i = 1; i < x.Float.Length; i++)
		{
			if (x.Step[i] != x.Step[i - 1])
			{
				continue;
			}

			double steps = (x.Float[i] - x.Float[i - 1]) / x.Step[i];
			Assert.AreEqual(Math.Round(steps), steps, $"sample {i}");
		}
	}

	[TestMethod]
	public void StoringTheDoubleStatesAsFloatMakesAStaircaseOneStepHigh()
	{
		foreach (QuantizedComponent component in Result.Components)
		{
			int risers = 0;

			for (int i = 1; i < component.Stored.Length; i++)
			{
				double rise = component.Stored[i] - component.Stored[i - 1];

				// Where a component crosses a power of two the lattice changes size, and a riser
				// across the change is a whole number of the finer step only.
				if (rise != 0.0 && component.Step[i] == component.Step[i - 1])
				{
					risers++;

					// Each state is rounded to the nearest lattice point, so a riser is a whole number
					// of steps, and never more than the residual moved plus the two roundings.
					double steps = rise / component.Step[i];
					Assert.AreEqual(Math.Round(steps), steps, $"{component.Name} riser at sample {i}");
					Assert.IsLessThanOrEqualTo(
						Math.Abs(component.Double[i] - component.Double[i - 1]) + (2.0 * component.Step[i]),
						Math.Abs(rise),
						$"{component.Name} riser at sample {i}");
				}

				// Two roundings of half a step each.
				Assert.IsLessThanOrEqualTo(component.Step[i], Math.Abs(component.Stored[i] - component.Double[i]), $"{component.Name} at sample {i}");
			}

			Console.WriteLine(
				$"{component.Name}: stored in float, {component.DistinctStoredLevels} levels, {risers} risers, worst {component.WorstStoredError:F3} m from double");

			// Mostly treads: the stored residual holds its value from one sample to the next more
			// often than not, and moves only in whole steps when it does.
			Assert.IsGreaterThan(0, risers, component.Name);
			Assert.IsLessThan(component.Stored.Length / 2, risers, component.Name);
		}
	}

	[TestMethod]
	public void TheDoubleResidualIsSmooth()
	{
		foreach (QuantizedComponent component in Result.Components)
		{
			double[] d = component.Double;
			double worstFirst = 0.0;
			double worstSecond = 0.0;
			int onLattice = 0;

			for (int i = 0; i < d.Length; i++)
			{
				double steps = d[i] / LageosStepMeters;
				if (Math.Abs(steps - Math.Round(steps)) < 0.01)
				{
					onLattice++;
				}

				if (i >= 1)
				{
					worstFirst = Math.Max(worstFirst, Math.Abs(d[i] - d[i - 1]));
				}

				if (i >= 2)
				{
					worstSecond = Math.Max(worstSecond, Math.Abs(d[i] - (2.0 * d[i - 1]) + d[i - 2]));
				}
			}

			Console.WriteLine(
				$"{component.Name}: double moves at most {worstFirst * 1000.0:F2} mm per sample, second difference at most {worstSecond * 1e6:F2} µm");

			// Measured: at most 117 mm per sample, which is the residual turning with the orbit as it is
			// seen in TEME, and a second difference of at most 0.52 mm, where the interpolator's
			// eleven-point window slides along the table. Both are far inside one float step.
			Assert.IsLessThan(LageosStepMeters / 4.0, worstFirst, component.Name);
			Assert.IsLessThan(LageosStepMeters / 500.0, worstSecond, component.Name);

			// One in fifty would land within a hundredth of a step by chance.
			Assert.IsLessThan(d.Length / 10, onLattice, component.Name);
		}
	}

	[TestMethod]
	public void TheFittedElementSetFollowsTheLaserRangingHour()
	{
		// The set was fitted to this hour (FloatQuantization's remarks), so this pins the fit rather
		// than measuring SGP4: 8.6 m RMS before the two-line format rounded it, 17 m worst after.
		double worst = Result.Components.Max(c => c.Double.Max(Math.Abs));
		Console.WriteLine($"fitted set against the SP3 hour: worst TEME component {worst:F2} m");

		Assert.IsLessThan(25.0, worst);
		Assert.IsGreaterThan(LageosStepMeters, worst, "A residual inside one step would show nothing.");
	}

	[TestMethod]
	public void FloatArithmeticScattersSeveralStepsEitherSideOfTheTruth()
	{
		double worst = Result.Components.Max(c => c.WorstFloatError);
		Console.WriteLine($"float end to end: worst {worst:F3} m from double");

		// Measured: 33.6 m. More than representation alone can explain, which is at most one step:
		// SGP4 in float rounds at a part in ten million of a 12,000 km orbit, a metre a time, through
		// a few hundred operations. Nowhere near the kilometres of a broken frame.
		Assert.IsGreaterThan(LageosStepMeters, worst);
		Assert.IsLessThan(60.0, worst);
	}

	[TestMethod]
	public void ATableBuiltFromSgp4ItselfLeavesNoResidualAtItsEpochs()
	{
		ElementSet elements = FloatQuantization.Elements;
		Sp3File file = FloatQuantization.Truth;
		Sgp4Satellite<double> satellite = Sgp4<double>.Initialize(elements, DoubleStorageMath.Instance);
		List<TabulatedPosition<double>> positions = [];
		List<TabulatedPosition<double>> velocities = [];
		List<double> seconds = [];

		foreach (Sp3Record record in file.RecordsOf(file.Satellites[0]))
		{
			double minutes = (record.Instant.Day - elements.EpochJulianDate.Day + (record.Instant.DayFraction - elements.EpochJulianDate.DayFraction)) * 1440.0;
			TemeState<double> teme = Sgp4<double>.Propagate(satellite, minutes, DoubleStorageMath.Instance).State;
			ItrfState<double> itrf = EarthFixedFrame<double>.ToItrf(teme, record.Instant, EarthOrientation.Ignored, DoubleStorageMath.Instance);
			positions.Add(new TabulatedPosition<double>(record.SecondsSinceStart, itrf.X, itrf.Y, itrf.Z));
			velocities.Add(new TabulatedPosition<double>(record.SecondsSinceStart, itrf.VelocityX, itrf.VelocityY, itrf.VelocityZ));
			seconds.Add(record.SecondsSinceStart);
		}

		IReadOnlyList<TabulatedTruthSample<double>> samples = TabulatedTruthResidual<double>.Compute(
			elements, positions, velocities, file.Start, seconds, _ => EarthOrientation.Ignored, DoubleStorageMath.Instance);

		foreach (TabulatedTruthSample<double> sample in samples)
		{
			// Not zero: the instant goes from seconds after the table's start through a two-part date
			// and back to minutes from the epoch, which rounds it by about a nanosecond.
			Assert.AreEqual(0.0, sample.Residual.Magnitude(DoubleStorageMath.Instance).Value, 1e-7);
		}
	}

	[TestMethod]
	public void MismatchedTablesAreRefused()
	{
		Sp3File file = FloatQuantization.Truth;
		(IReadOnlyList<TabulatedPosition<double>> positions, IReadOnlyList<TabulatedPosition<double>> velocities) = FloatQuantization.Tables<double>(file);

		Assert.ThrowsExactly<ArgumentException>(() => TabulatedTruthResidual<double>.Compute(
			FloatQuantization.Elements, positions, [.. velocities.Skip(1)], file.Start, [0.0], _ => EarthOrientation.Ignored, DoubleStorageMath.Instance));
	}
}
