// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Numerics;

using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Validates the two-body propagator against closed-form positions for every kind of conic.
/// </summary>
/// <remarks>
/// Each case has an answer that owes nothing to the universal variable: a circle's position is a
/// rotation, an ellipse's comes from Kepler's equation in eccentric anomaly, a parabola's from
/// Barker's equation, and a hyperbola's has to conserve energy and angular momentum and retrace
/// itself when run backwards.
/// </remarks>
[TestClass]
public sealed class KeplerianTests
{
	/// <summary>The Earth's gravitational parameter, EGM96, in km³/s².</summary>
	private const double Mu = 398600.4418;

	private static readonly DoubleStorageMath Math64 = DoubleStorageMath.Instance;

	[TestMethod]
	public void Circular_MatchesTheRotation_AfterManyPeriods()
	{
		const double radius = 7000.0;
		double speed = Math.Sqrt(Mu / radius);
		double meanMotion = speed / radius;
		double period = 2.0 * Math.PI / meanMotion;
		TwoBodyState<double> start = new(radius, 0.0, 0.0, 0.0, speed, 0.0);

		foreach (double periods in new[] { 0.25, 1.0, 10.37, 100.5 })
		{
			double seconds = periods * period;
			KeplerianResult<double> result = Keplerian<double>.Propagate(start, seconds, Mu, Math64);
			Assert.IsTrue(result.Solution.Converged);

			double angle = meanMotion * seconds;
			double error = Distance(result.State, radius * Math.Cos(angle), radius * Math.Sin(angle), 0.0);
			Console.WriteLine($"circular, {periods} periods: {error:E3} km");

			// The closed form itself carries the error of n·t, which grows with the number of periods.
			// Measured 1.5e-11 km after one period and 1.5e-9 km after a hundred.
			Assert.IsLessThan(5e-11 * Math.Max(1.0, periods), error, $"{periods} periods");
		}
	}

	[TestMethod]
	public void Elliptic_MatchesKeplersEquation_AfterManyPeriods()
	{
		// An inclined Molniya-like orbit, started away from perigee.
		const double a = 26554.0;
		const double e = 0.74;
		const double inclination = 63.4 * Math.PI / 180.0;
		const double node = 0.7;
		const double perigee = 270.0 * Math.PI / 180.0;
		const double trueAnomaly = 0.4;

		double p = a * (1.0 - (e * e));
		TwoBodyState<double> start = Keplerian<double>.FromElements(p, e, inclination, node, perigee, trueAnomaly, Mu, Math64);
		double period = 2.0 * Math.PI * Math.Sqrt(a * a * a / Mu);

		foreach (double periods in new[] { 0.13, 1.0, 10.37, 50.0 })
		{
			double seconds = periods * period;
			KeplerianResult<double> result = Keplerian<double>.Propagate(start, seconds, Mu, Math64);
			Assert.IsTrue(result.Solution.Converged);

			TwoBodyState<double> expected = Expected(a, e, inclination, node, perigee, trueAnomaly, seconds);
			double error = Distance(result.State, expected.X, expected.Y, expected.Z);
			Console.WriteLine($"elliptic, {periods} periods: {error:E3} km");

			// Measured 2.3e-10 km after one period and 1.1e-8 km after fifty.
			Assert.IsLessThan(1e-9 * Math.Max(1.0, periods), error, $"{periods} periods");
		}
	}

	[TestMethod]
	public void Elliptic_ReturnsToItsStart_AfterWholePeriods()
	{
		const double a = 26554.0;
		const double e = 0.74;
		double p = a * (1.0 - (e * e));
		TwoBodyState<double> start = Keplerian<double>.FromElements(p, e, 1.1, 0.2, 4.7, 2.0, Mu, Math64);
		double period = 2.0 * Math.PI * Math.Sqrt(a * a * a / Mu);

		foreach (int periods in new[] { 1, 3, 20 })
		{
			KeplerianResult<double> result = Keplerian<double>.Propagate(start, periods * period, Mu, Math64);
			double error = Distance(result.State, start.X, start.Y, start.Z);
			Assert.IsLessThan(1e-9 * periods, error, $"{periods} periods");
		}
	}

	[TestMethod]
	public void Parabolic_MatchesBarkersEquation()
	{
		const double perigeeRadius = 7000.0;
		const double p = 2.0 * perigeeRadius;
		TwoBodyState<double> start = Keplerian<double>.FromElements(p, 1.0, 0.0, 0.0, 0.0, 0.0, Mu, Math64);

		foreach (double seconds in new[] { 60.0, 3600.0, 86400.0, 30.0 * 86400.0, -7200.0 })
		{
			KeplerianResult<double> result = Keplerian<double>.Propagate(start, seconds, Mu, Math64);
			Assert.IsTrue(result.Solution.Converged, $"{seconds} s");

			// Barker: D + D³/3 = √(μ/p³)·t with D = tan(ν/2), a depressed cubic solved by Cardano.
			double w = 3.0 * Math.Sqrt(Mu / (p * p * p)) * seconds;
			double root = Math.Sqrt((w * w) + 1.0);
			double d = Math.Cbrt(w + root) - Math.Cbrt(root - w);
			double nu = 2.0 * Math.Atan(d);
			double r = p / (1.0 + Math.Cos(nu));

			double error = Distance(result.State, r * Math.Cos(nu), r * Math.Sin(nu), 0.0);
			Console.WriteLine($"parabolic, {seconds} s: {error:E3} km at r = {r:F0} km");
			Assert.IsLessThan(1e-12 * r, error, $"{seconds} s");
		}
	}

	[TestMethod]
	public void NearParabolic_IsContinuousAcrossTheParabola()
	{
		// A fixed perigee and an eccentricity a hair either side of one. The universal variable is
		// what makes these one computation rather than three; a formulation dividing by 1 − e would
		// fall apart here.
		const double perigeeRadius = 7000.0;
		const double seconds = 6.0 * 3600.0;
		static TwoBodyState<double> Run(double e)
		{
			TwoBodyState<double> start = Keplerian<double>.FromElements(perigeeRadius * (1.0 + e), e, 0.5, 0.0, 0.0, -0.3, Mu, Math64);
			KeplerianResult<double> result = Keplerian<double>.Propagate(start, seconds, Mu, Math64);
			Assert.IsTrue(result.Solution.Converged, $"e = {e}");
			return result.State;
		}

		TwoBodyState<double> parabola = Run(1.0);
		foreach (double delta in new[] { 1e-6, 1e-9, 1e-12 })
		{
			TwoBodyState<double> below = Run(1.0 - delta);
			TwoBodyState<double> above = Run(1.0 + delta);
			double gapBelow = Distance(below, parabola.X, parabola.Y, parabola.Z);
			double gapAbove = Distance(above, parabola.X, parabola.Y, parabola.Z);
			Console.WriteLine($"near-parabolic, δe = {delta:E0}: {gapBelow:E3} km below, {gapAbove:E3} km above");

			// The trajectories genuinely differ by an amount proportional to δe, so the bound scales
			// with it; a discontinuity at e = 1 would show as a gap that does not shrink.
			double bound = (1e8 * delta) + 1e-6;
			Assert.IsLessThan(bound, gapBelow, $"δe = {delta}");
			Assert.IsLessThan(bound, gapAbove, $"δe = {delta}");
		}
	}

	[TestMethod]
	public void Hyperbolic_ConservesItsIntegrals_AndRetracesItself()
	{
		const double perigeeRadius = 7000.0;
		const double e = 1.8;
		TwoBodyState<double> start = Keplerian<double>.FromElements(perigeeRadius * (1.0 + e), e, 0.3, 1.0, 2.0, -1.0, Mu, Math64);

		foreach (double seconds in new[] { 600.0, 6.0 * 3600.0, 10.0 * 86400.0 })
		{
			KeplerianResult<double> forward = Keplerian<double>.Propagate(start, seconds, Mu, Math64);
			Assert.IsTrue(forward.Solution.Converged, $"{seconds} s");

			double energy = Energy(start);
			double energyError = Math.Abs(Energy(forward.State) - energy) / Math.Abs(energy);
			(double hx, double hy, double hz) = AngularMomentum(start);
			(double fx, double fy, double fz) = AngularMomentum(forward.State);
			double momentumError = Math.Sqrt(((fx - hx) * (fx - hx)) + ((fy - hy) * (fy - hy)) + ((fz - hz) * (fz - hz))) / Math.Sqrt((hx * hx) + (hy * hy) + (hz * hz));

			KeplerianResult<double> back = Keplerian<double>.Propagate(forward.State, -seconds, Mu, Math64);
			double returnError = Distance(back.State, start.X, start.Y, start.Z);
			Console.WriteLine($"hyperbolic, {seconds} s: energy {energyError:E2}, momentum {momentumError:E2}, return {returnError:E3} km");

			Assert.IsLessThan(1e-10, energyError, $"{seconds} s");
			Assert.IsLessThan(1e-10, momentumError, $"{seconds} s");
			Assert.IsLessThan(1e-6, returnError, $"{seconds} s");
		}
	}

	[TestMethod]
	public void Stumpff_IsContinuousAcrossBothSeriesBoundaries()
	{
		// Either side of z = 4 and z = -2500, where the closed forms take over from the series, the
		// two must agree to the last few digits or the propagator has a seam in it.
		// Adjacent doubles, so the functions themselves move by nothing measurable between them.
		// At z = 4 the seam was measured at two ulps. At z = -2500 it is 2.8e-14 relative, which is
		// the closed form's exponential: seven halvings and seven squarings double its error seven
		// times, and √−z = 50 is already fifty ulps of conditioning on the argument alone.
		foreach ((double boundary, double tolerance) in new[] { (4.0, 1e-15), (-2500.0, 5e-14) })
		{
			(double c1, double s1) = KeplerSolvers<double>.Stumpff(Math.BitDecrement(boundary), Math64);
			(double c2, double s2) = KeplerSolvers<double>.Stumpff(Math.BitIncrement(boundary), Math64);
			Assert.AreEqual(c1, c2, tolerance * c1, $"C at {boundary}");
			Assert.AreEqual(s1, s2, tolerance * s1, $"S at {boundary}");
		}

		(double c0, double s0) = KeplerSolvers<double>.Stumpff(0.0, Math64);
		Assert.AreEqual(0.5, c0);
		Assert.AreEqual(1.0 / 6.0, s0);
	}

	[TestMethod]
	public void Stumpff_MatchesClosedForms_InPreciseArithmetic()
	{
		// At sixty digits, sampled across both series and both closed forms.
		PreciseStorageMath math = new(60);
		foreach (double sample in new[] { -3000.0, -30.0, -4.5, -1.0, -1e-6, 1e-6, 1.0, 3.9, 4.5, 30.0 })
		{
			PreciseNumber z = sample.ToPreciseNumber();
			(PreciseNumber c, PreciseNumber s) = KeplerSolvers<PreciseNumber>.Stumpff(z, math);

			PreciseNumber expectedC;
			PreciseNumber expectedS;
			if (sample > 0)
			{
				PreciseNumber root = PreciseNumber.Sqrt(z, 80);
				expectedC = (PreciseNumber.One - PreciseNumber.Cos(root, 80)) / z;
				expectedS = (root - PreciseNumber.Sin(root, 80)) / (z * root);
			}
			else
			{
				PreciseNumber root = PreciseNumber.Sqrt(-z, 80);
				PreciseNumber growing = PreciseNumber.Exp(root, 80);
				PreciseNumber decaying = PreciseNumber.One / growing;
				PreciseNumber two = 2.ToPreciseNumber();
				expectedC = (((growing + decaying) / two) - PreciseNumber.One) / -z;
				expectedS = (((growing - decaying) / two) - root) / (-z * root);
			}

			// The closed forms lose digits near zero — which is why the propagator does not use
			// them there — so the comparison is only as tight as the closed form allows. Elsewhere
			// the bound is set by the reference's own divisions, which round at fifty digits.
			double tolerance = Math.Abs(sample) < 1e-3 ? 1e-40 : 1e-49;
			Assert.IsLessThan(tolerance, (PreciseNumber.Abs(c - expectedC) / expectedC).To<double>(), $"C({sample})");
			Assert.IsLessThan(tolerance, (PreciseNumber.Abs(s - expectedS) / expectedS).To<double>(), $"S({sample})");
		}
	}

	/// <summary>The closed-form elliptic position, through Kepler's equation in sixty digits.</summary>
	private static TwoBodyState<double> Expected(double a, double e, double inclination, double node, double perigee, double trueAnomaly, double seconds)
	{
		PreciseStorageMath math = new(60);
		PreciseNumber ep = e.ToPreciseNumber();
		PreciseNumber nu0 = trueAnomaly.ToPreciseNumber();
		PreciseNumber two = 2.ToPreciseNumber();

		// E₀ from ν₀, then M₀, then M at the requested time, then E and ν back again.
		PreciseNumber factor = PreciseNumber.Sqrt((PreciseNumber.One - ep) / (PreciseNumber.One + ep), 60);
		PreciseNumber halfNu = nu0 / two;
		PreciseNumber eccentric0 = two * math.Atan2(factor * PreciseNumber.Sin(halfNu, 60), PreciseNumber.Cos(halfNu, 60));
		PreciseNumber mean0 = eccentric0 - (ep * PreciseNumber.Sin(eccentric0, 60));
		PreciseNumber a3 = a.ToPreciseNumber() * a.ToPreciseNumber() * a.ToPreciseNumber();
		PreciseNumber meanMotion = PreciseNumber.Sqrt(Mu.ToPreciseNumber() / a3, 60);
		PreciseNumber twoPi = two * PreciseNumber.Pi;
		PreciseNumber mean = mean0 + (meanMotion * seconds.ToPreciseNumber());
		PreciseNumber turns = Math.Floor((mean / twoPi).To<double>()).ToPreciseNumber();
		mean -= turns * twoPi;

		KeplerSolution<PreciseNumber> solved = KeplerSolvers<PreciseNumber>.NaiveNewton(mean, ep, math);
		PreciseNumber eccentric = solved.Anomaly;
		PreciseNumber inverse = PreciseNumber.Sqrt((PreciseNumber.One + ep) / (PreciseNumber.One - ep), 60);
		PreciseNumber nu = two * math.Atan2(inverse * PreciseNumber.Sin(eccentric / two, 60), PreciseNumber.Cos(eccentric / two, 60));

		PreciseNumber p = a.ToPreciseNumber() * (PreciseNumber.One - (ep * ep));
		TwoBodyState<PreciseNumber> state = Keplerian<PreciseNumber>.FromElements(
			p, ep, inclination.ToPreciseNumber(), node.ToPreciseNumber(), perigee.ToPreciseNumber(), nu, Mu.ToPreciseNumber(), math);
		return new(state.X.To<double>(), state.Y.To<double>(), state.Z.To<double>(), state.VelocityX.To<double>(), state.VelocityY.To<double>(), state.VelocityZ.To<double>());
	}

	private static double Distance<T>(TwoBodyState<T> state, double x, double y, double z)
		where T : struct, INumber<T>
	{
		double dx = double.CreateChecked(state.X) - x;
		double dy = double.CreateChecked(state.Y) - y;
		double dz = double.CreateChecked(state.Z) - z;
		return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
	}

	private static double Energy(TwoBodyState<double> s) =>
		(((s.VelocityX * s.VelocityX) + (s.VelocityY * s.VelocityY) + (s.VelocityZ * s.VelocityZ)) / 2.0)
		- (Mu / Math.Sqrt((s.X * s.X) + (s.Y * s.Y) + (s.Z * s.Z)));

	private static (double X, double Y, double Z) AngularMomentum(TwoBodyState<double> s) =>
		((s.Y * s.VelocityZ) - (s.Z * s.VelocityY), (s.Z * s.VelocityX) - (s.X * s.VelocityZ), (s.X * s.VelocityY) - (s.Y * s.VelocityX));
}
