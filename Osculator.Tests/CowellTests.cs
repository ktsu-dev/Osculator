// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Globalization;
using System.Numerics;
using ktsu.Osculator.Core.Forces;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// The integrator: that the Prince–Dormand pair has the order it claims, and that Cowell's method
/// over it reproduces a two-body orbit.
/// </summary>
/// <remarks>
/// The round-off measurement the integrator exists for is in <see cref="CowellRoundOffTests"/>.
/// These are what make that measurement mean anything: an integrator with a mistyped coefficient
/// still runs, still conserves energy to a few digits, and still differs between storage types.
/// </remarks>
[TestClass]
public sealed class CowellTests
{
	/// <summary>The EGM96 gravitational parameter, in km³/s².</summary>
	private const double Mu = 398600.4418;

	[TestMethod]
	public void TheErrorEstimate_FallsAtTheEmbeddedOrder()
	{
		// The local error estimate is the difference between the eighth- and seventh-order solutions,
		// so it is the seventh-order solution's local error and falls as h⁸. That depends on every
		// one of the 92 coefficients: changing one digit of one coupling coefficient (a₁₀,₄) was
		// checked to drop the measured order from 8 to 1.
		//
		// Measured in fifty digits on an orbit of eccentricity 0.3, so neither round-off nor a
		// near-circular orbit's symmetry can flatter the result. Measured: 7.92 and 7.98, the second
		// closer to 8 because the steps are further into the asymptotic range.
		PreciseStorageMath math = new(50);
		TwoBody<PreciseNumber> force = new(Mu.ToPreciseNumber(), math);
		DormandPrince87<PreciseNumber> pair = new(math);

		const double Eccentricity = 0.3;
		const double PerigeeKm = 7000.0;
		double perigeeSpeed = Math.Sqrt(Mu * (1.0 + Eccentricity) / PerigeeKm);
		CartesianState<PreciseNumber> perigee = new(
			PerigeeKm.ToPreciseNumber(),
			PreciseNumber.Zero,
			PreciseNumber.Zero,
			PreciseNumber.Zero,
			perigeeSpeed.ToPreciseNumber(),
			PreciseNumber.Zero);

		// One radian of mean anomaly at this orbit's mean motion is about 1590 s; these steps are a
		// fifth, a tenth and a twentieth of that, where the Python check of the table ran.
		double[] steps = [318.0, 159.0, 79.5];
		double[] estimates = new double[steps.Length];
		for (int i = 0; i < steps.Length; i++)
		{
			DormandPrince87Step<PreciseNumber> step = pair.Step(force, PreciseNumber.Zero, perigee, steps[i].ToPreciseNumber());
			estimates[i] = Magnitude(step.ErrorEstimate);
		}

		for (int i = 0; i + 1 < steps.Length; i++)
		{
			double order = Math.Log2(estimates[i] / estimates[i + 1]);
			Console.WriteLine($"h {steps[i]} -> {steps[i + 1]} s: estimate {estimates[i]:E3} -> {estimates[i + 1]:E3} km, observed order {order:F2}");
			Assert.IsGreaterThan(7.5, order, $"The error estimate should fall as h^8; it fell as h^{order:F2}.");
			Assert.IsLessThan(8.5, order, $"The error estimate should fall as h^8; it fell as h^{order:F2}.");
		}
	}

	[TestMethod]
	public void ATwoBodyOrbit_ReturnsToItsStartAfterOnePeriod()
	{
		// Independent of any reference implementation: a Kepler orbit is periodic, and its period
		// follows from its energy alone. An integrator that gets the force, a coefficient or the step
		// control wrong does not land back on the start.
		IStorageMath<double> math = DoubleStorageMath.Instance;
		Cowell<double> cowell = new(new TwoBody<double>(Mu, math), math);

		CartesianState<double> start = new(7000.0, 0.0, 0.0, 0.0, 4.6875, 5.90625);
		double energy = SpecificEnergy(start);
		double semiMajorAxis = -Mu / (2.0 * energy);
		double period = 2.0 * Math.PI * Math.Sqrt(semiMajorAxis * semiMajorAxis * semiMajorAxis / Mu);

		CowellResult<double> result = cowell.Propagate(start, period, CowellTolerance.Default);
		CartesianState<double> end = result.State;

		double positionError = Math.Sqrt(Square(end.X - start.X) + Square(end.Y - start.Y) + Square(end.Z - start.Z));
		double energyDrift = Math.Abs((SpecificEnergy(end) - energy) / energy);
		double momentumDrift = Math.Abs((AngularMomentum(end) - AngularMomentum(start)) / AngularMomentum(start));

		Console.WriteLine($"one period ({period:F3} s): {result.AcceptedSteps} steps, {result.RejectedSteps} rejected, closes to {positionError:E3} km; energy drift {energyDrift:E3}, angular momentum drift {momentumDrift:E3}");

		// Measured: 56 steps, closing to 3.0e-9 km, with energy and angular momentum held to 9e-14
		// and 4e-14.
		Assert.IsLessThan(1e-6, positionError, "One period should bring a two-body orbit back to within a millimetre of its start.");
		Assert.IsLessThan(1e-12, energyDrift);
		Assert.IsLessThan(1e-12, momentumDrift);
	}

	[TestMethod]
	public void APreciseIntegration_StaysAtItsWorkingPrecision()
	{
		// Domain trap 9: PreciseNumber multiplication is exact, so a state that is not reduced grows
		// by the width of a coefficient at every stage of every step. Every value the integrator
		// stores has to come back at the working precision.
		const int Digits = 30;
		PreciseStorageMath math = new(Digits);
		Cowell<PreciseNumber> cowell = new(new TwoBody<PreciseNumber>(Mu.ToPreciseNumber(), math), math);
		CartesianState<PreciseNumber> start = new(
			7000.ToPreciseNumber(), PreciseNumber.Zero, PreciseNumber.Zero, PreciseNumber.Zero, 4.6875.ToPreciseNumber(), 5.90625.ToPreciseNumber());

		CartesianState<PreciseNumber> end = cowell.PropagateFixedStep(start, 120.ToPreciseNumber(), 20).State;

		foreach (PreciseNumber component in (PreciseNumber[])[end.X, end.Y, end.Z, end.VelocityX, end.VelocityY, end.VelocityZ])
		{
			Assert.IsLessThanOrEqualTo(Digits, component.SignificantDigits);
		}
	}

	[TestMethod]
	public void AnAdaptiveArc_EndsExactlyWhereItWasAskedTo()
	{
		IStorageMath<double> math = DoubleStorageMath.Instance;
		Cowell<double> cowell = new(new TwoBody<double>(Mu, math), math);

		CowellResult<double> result = cowell.Propagate(new(7000.0, 0.0, 0.0, 0.0, 4.6875, 5.90625), 1234.5, CowellTolerance.Default);

		Assert.AreEqual(1234.5, result.ElapsedSeconds);
		Assert.AreEqual(result.AcceptedSteps + result.RejectedSteps, result.StepsAttempted);
		Assert.AreEqual(result.StepsAttempted * 13, result.ForceEvaluations);
	}

	[TestMethod]
	public void AToleranceFinerThanTheStorageType_IsRefusedRatherThanSpunOn()
	{
		// float resolves about half a metre at 7000 km, so its error estimate never falls below a
		// micrometre however small the step. The controller must give up rather than shrink forever.
		IStorageMath<float> math = FloatStorageMath.Instance;
		Cowell<float> cowell = new(new TwoBody<float>(398600.4375f, math), math);

		Assert.ThrowsExactly<ArithmeticException>(() =>
			cowell.Propagate(new(7000f, 0f, 0f, 0f, 4.6875f, 5.90625f), 5400f, CowellTolerance.Default));
	}

	[TestMethod]
	public void TheEarthsGravitationalParameter_IsTheLiteral_InEveryStorageType()
	{
		Assert.AreEqual(398600.4418m, TwoBody<decimal>.EarthGravitationalParameter);
		Assert.AreEqual(PreciseNumber.Parse("398600.4418", CultureInfo.InvariantCulture), TwoBody<PreciseNumber>.EarthGravitationalParameter);
	}

	private static double Magnitude<T>(CartesianState<T> state)
		where T : struct, INumber<T>
	{
		double x = double.CreateChecked(state.X);
		double y = double.CreateChecked(state.Y);
		double z = double.CreateChecked(state.Z);
		return Math.Sqrt((x * x) + (y * y) + (z * z));
	}

	private static double SpecificEnergy(CartesianState<double> s)
	{
		double r = Math.Sqrt(Square(s.X) + Square(s.Y) + Square(s.Z));
		double v2 = Square(s.VelocityX) + Square(s.VelocityY) + Square(s.VelocityZ);
		return (v2 / 2.0) - (Mu / r);
	}

	private static double AngularMomentum(CartesianState<double> s)
	{
		double hx = (s.Y * s.VelocityZ) - (s.Z * s.VelocityY);
		double hy = (s.Z * s.VelocityX) - (s.X * s.VelocityZ);
		double hz = (s.X * s.VelocityY) - (s.Y * s.VelocityX);
		return Math.Sqrt(Square(hx) + Square(hy) + Square(hz));
	}

	private static double Square(double value) => value * value;
}
