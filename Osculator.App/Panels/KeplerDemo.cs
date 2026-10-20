// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Panels;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;

/// <summary>
/// How one storage type did at one point near perigee, measured against the reference.
/// </summary>
/// <param name="StorageName">The storage type's name.</param>
/// <param name="AvailableDigits">The type's own digits: minus log10 of its unit round-off, or its declared precision.</param>
/// <param name="NaiveAnomaly">The naive solver's eccentric anomaly, in radians, rounded to <see langword="double"/> for display.</param>
/// <param name="NaiveAnomalyLost">Digits of the naive eccentric anomaly lost, or NaN if the solve did not converge.</param>
/// <param name="NaivePositionLost">Digits of the naive solve's position lost, or NaN if it did not converge.</param>
/// <param name="NaiveIterations">Steps the naive solver took.</param>
/// <param name="NaiveConverged">Whether the naive solver converged.</param>
/// <param name="UniversalAnomaly">The eccentric anomaly the universal solve implies, <c>χ·√α</c>, in radians.</param>
/// <param name="UniversalAnomalyLost">Digits of that eccentric anomaly lost, or NaN if the solve did not converge.</param>
/// <param name="UniversalPositionLost">Digits of the universal propagation's position lost, or NaN if it did not converge.</param>
/// <param name="UniversalIterations">Steps the universal solver took.</param>
/// <param name="UniversalConverged">Whether the universal solver converged.</param>
/// <param name="Seconds">Wall-clock time for both solves in this type.</param>
internal sealed record KeplerRun(
	string StorageName,
	double AvailableDigits,
	double NaiveAnomaly,
	double NaiveAnomalyLost,
	double NaivePositionLost,
	int NaiveIterations,
	bool NaiveConverged,
	double UniversalAnomaly,
	double UniversalAnomalyLost,
	double UniversalPositionLost,
	int UniversalIterations,
	bool UniversalConverged,
	double Seconds);

/// <summary>One run of demonstration 3: every storage type at one eccentricity and mean anomaly.</summary>
/// <param name="Eccentricity">The eccentricity.</param>
/// <param name="MeanAnomaly">The mean anomaly past perigee, in radians.</param>
/// <param name="Runs">float, double, decimal and the thirty-digit PreciseNumber probe, in that order.</param>
internal sealed record KeplerDemoResult(double Eccentricity, double MeanAnomaly, IReadOnlyList<KeplerRun> Runs);

/// <summary>
/// Spec demonstration 3: Kepler's equation near perigee, solved by the naive Newton iteration and by
/// universal variables in every storage type, with the digits each loses measured against a
/// sixty-digit reference.
/// </summary>
/// <remarks>
/// <para>
/// The arrangement is the one <c>KeplerEquationDigitLossTests</c> measures, so the panel and the
/// suite report the same numbers. The naive path solves for E and places the point in the perifocal
/// frame directly; the universal path starts at perigee and propagates the mean anomaly's worth of
/// time, reaching the same point through an unrelated equation. Every input is a
/// <see langword="double"/> literal converted once into the storage type, so the reference starts from
/// exactly the values the <see langword="double"/> run does.
/// </para>
/// <para>
/// At a Molniya eccentricity float, double and thirty digits lose under a digit either way: near
/// perigee <c>E − e·sin E</c> cancels terms in the ratio 1 : 0.74, which costs half a digit. decimal
/// loses several, because its precision is absolute and E near perigee is small (domain trap 10).
/// Raising the eccentricity towards one is what makes the cancellation grow.
/// </para>
/// </remarks>
internal static class KeplerDemo
{
	/// <summary>The Molniya eccentricity the panel starts at.</summary>
	internal const double MolniyaEccentricity = 0.74;

	/// <summary>The mean anomaly past perigee the panel starts at, in radians.</summary>
	internal const double DefaultMeanAnomaly = 1e-3;

	/// <summary>The reference's working precision, in significant digits.</summary>
	internal const int ReferenceDigits = 60;

	/// <summary>The PreciseNumber probe's working precision, in significant digits.</summary>
	internal const int ProbeDigits = 30;

	/// <summary>A Molniya semi-major axis, in km.</summary>
	internal const double SemiMajorAxis = 26554.0;

	/// <summary>The Earth's gravitational parameter, EGM96, in km³/s².</summary>
	private const double Mu = 398600.4418;

	/// <summary>The largest eccentricity accepted; at one the ellipse stops being one.</summary>
	private const double MaximumEccentricity = 0.999999;

	/// <summary>
	/// Checks whether an eccentricity and a mean anomaly can be run.
	/// </summary>
	/// <param name="eccentricity">The eccentricity.</param>
	/// <param name="meanAnomaly">The mean anomaly past perigee, in radians.</param>
	/// <returns>Whether both are in range: e in [0, 0.999999] and M in (0, π].</returns>
	internal static bool IsValid(double eccentricity, double meanAnomaly) =>
		double.IsFinite(eccentricity) && eccentricity >= 0.0 && eccentricity <= MaximumEccentricity
		&& double.IsFinite(meanAnomaly) && meanAnomaly > 0.0 && meanAnomaly <= Math.PI;

	/// <summary>
	/// Runs both solvers in every storage type and measures each against the reference.
	/// </summary>
	/// <param name="eccentricity">The eccentricity.</param>
	/// <param name="meanAnomaly">The mean anomaly past perigee, in radians.</param>
	/// <param name="token">Cancels the run between storage types.</param>
	/// <returns>The result.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The inputs fail <see cref="IsValid"/>.</exception>
	internal static KeplerDemoResult Compute(double eccentricity, double meanAnomaly, CancellationToken token)
	{
		if (!IsValid(eccentricity, meanAnomaly))
		{
			throw new ArgumentOutOfRangeException(nameof(eccentricity), "The eccentricity must be in [0, 0.999999] and the mean anomaly in (0, π].");
		}

		Solve truth = Run(meanAnomaly, eccentricity, new PreciseStorageMath(ReferenceDigits));
		token.ThrowIfCancellationRequested();

		List<KeplerRun> runs = [];
		runs.Add(Measure("float", -Math.Log10(Math.Pow(2, -24)), () => Run(meanAnomaly, eccentricity, FloatStorageMath.Instance), truth));
		token.ThrowIfCancellationRequested();
		runs.Add(Measure("double", -Math.Log10(Math.Pow(2, -53)), () => Run(meanAnomaly, eccentricity, DoubleStorageMath.Instance), truth));
		token.ThrowIfCancellationRequested();
		runs.Add(Measure("decimal", 28.0, () => Run(meanAnomaly, eccentricity, DecimalStorageMath.Instance), truth));
		token.ThrowIfCancellationRequested();
		runs.Add(Measure($"PreciseNumber ({ProbeDigits} digits)", ProbeDigits, () => Run(meanAnomaly, eccentricity, new PreciseStorageMath(ProbeDigits)), truth));

		return new(eccentricity, meanAnomaly, runs);
	}

	/// <summary>Digits lost for a relative error: what the type has, less what survived.</summary>
	/// <param name="relativeError">The relative error.</param>
	/// <param name="available">The type's own digits.</param>
	/// <returns>Digits lost, never negative.</returns>
	internal static double Lost(double relativeError, double available) =>
		relativeError <= 0.0 ? 0.0 : Math.Max(0.0, available + Math.Log10(relativeError));

	private static KeplerRun Measure(string name, double available, Func<Solve> run, Solve truth)
	{
		Stopwatch clock = Stopwatch.StartNew();
		Solve probe = run();
		double seconds = clock.Elapsed.TotalSeconds;

		return new(
			name,
			available,
			probe.NaiveAnomaly.To<double>(),
			probe.NaiveConverged ? Lost(RelativeError(probe.NaiveAnomaly, truth.NaiveAnomaly), available) : double.NaN,
			probe.NaiveConverged ? Lost(RelativeDistance(probe.NaiveX, probe.NaiveY, truth.NaiveX, truth.NaiveY), available) : double.NaN,
			probe.NaiveIterations,
			probe.NaiveConverged,
			probe.UniversalAnomaly.To<double>(),
			probe.UniversalConverged ? Lost(RelativeError(probe.UniversalAnomaly, truth.UniversalAnomaly), available) : double.NaN,
			probe.UniversalConverged ? Lost(RelativeDistance(probe.UniversalX, probe.UniversalY, truth.UniversalX, truth.UniversalY), available) : double.NaN,
			probe.UniversalIterations,
			probe.UniversalConverged,
			seconds);
	}

	private static double RelativeError(PreciseNumber value, PreciseNumber reference) =>
		reference == PreciseNumber.Zero
			? (value == PreciseNumber.Zero ? 0.0 : double.PositiveInfinity)
			: (PreciseNumber.Abs(value - reference) / PreciseNumber.Abs(reference)).To<double>();

	private static double RelativeDistance(PreciseNumber x, PreciseNumber y, PreciseNumber referenceX, PreciseNumber referenceY)
	{
		PreciseNumber dx = x - referenceX;
		PreciseNumber dy = y - referenceY;
		double distance = Math.Sqrt(((dx * dx) + (dy * dy)).To<double>());
		double radius = Math.Sqrt(((referenceX * referenceX) + (referenceY * referenceY)).To<double>());
		return distance / radius;
	}

	/// <summary>Solves the near-perigee point both ways in one storage type, lifted to PreciseNumber.</summary>
	private static Solve Run<T>(double meanAnomaly, double eccentricity, IStorageMath<T> math)
		where T : struct, INumber<T>
	{
		T a = T.CreateChecked(SemiMajorAxis);
		T e = T.CreateChecked(eccentricity);
		T mu = T.CreateChecked(Mu);
		T m = T.CreateChecked(meanAnomaly);

		KeplerSolution<T> naive = KeplerSolvers<T>.NaiveNewton(m, e, math);
		T anomaly = naive.Anomaly;
		T oneLessESquared = T.One - (e * e);
		T naiveX = a * (math.Cos(anomaly) - e);
		T naiveY = a * math.Sqrt(oneLessESquared) * math.Sin(anomaly);

		TwoBodyState<T> perigee = Keplerian<T>.FromElements(a * oneLessESquared, e, T.Zero, T.Zero, T.Zero, T.Zero, mu, math);
		T meanMotion = math.Sqrt(mu / (a * a * a));
		KeplerianResult<T> universal = Keplerian<T>.Propagate(perigee, m / meanMotion, mu, math);

		// From perigee, the change in eccentric anomaly is χ/√a for an ellipse.
		T universalAnomaly = universal.Solution.Anomaly / math.Sqrt(a);

		return new(
			anomaly.ToPreciseNumber(),
			naiveX.ToPreciseNumber(),
			naiveY.ToPreciseNumber(),
			naive.Iterations,
			naive.Converged,
			universalAnomaly.ToPreciseNumber(),
			universal.State.X.ToPreciseNumber(),
			universal.State.Y.ToPreciseNumber(),
			universal.Solution.Iterations,
			universal.Solution.Converged);
	}

	/// <summary>One solve in a storage type's own arithmetic, lifted to the reference type.</summary>
	private sealed record Solve(
		PreciseNumber NaiveAnomaly,
		PreciseNumber NaiveX,
		PreciseNumber NaiveY,
		int NaiveIterations,
		bool NaiveConverged,
		PreciseNumber UniversalAnomaly,
		PreciseNumber UniversalX,
		PreciseNumber UniversalY,
		int UniversalIterations,
		bool UniversalConverged);
}
