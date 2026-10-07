// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Spec demonstration 3: Kepler's equation near perigee of a Molniya orbit, solved by the naive
/// Newton iteration and by universal variables, in every storage type, with the digits each loses
/// measured against a sixty-digit reference.
/// </summary>
/// <remarks>
/// <para>
/// The spec calls this "the textbook catastrophic-cancellation case". At e = 0.74 it is not, and
/// this suite says so with numbers. Every figure below is one it measured.
/// </para>
/// <para>
/// In <see langword="float"/>, <see langword="double"/> and a thirty-digit
/// <see cref="PreciseNumber"/>, <b>neither formulation loses so much as a digit</b>: the worst
/// position loss over six mean anomalies from 0.5 down to 1e-6 rad was 0.73 digits naive and 0.57
/// universal. <c>E − e·sin E</c> near perigee is a difference of two terms in the ratio 1 : 0.74,
/// which costs about half a digit, not the whole word. Cancellation becomes catastrophic as
/// <c>1 − e</c> goes to zero, and a Molniya orbit is a long way from that.
/// </para>
/// <para>
/// <see langword="decimal"/> is where digits go, and it is the same trap as SGP4's drag terms
/// (domain trap 10): its precision is absolute, not relative. The naive solver's eccentric anomaly
/// loses <b>5.3 of 28 digits</b> at M = 1e-6, because E there is about 4e-6 and a
/// <see langword="decimal"/> holds 4e-6 to 22 significant digits whatever the arithmetic does. The
/// universal propagation loses up to <b>6.5 digits of position</b>, because its intermediates — α is
/// 3.8e-5 per km, its products with χ² smaller still — live in the same range. More digits on paper,
/// fewer where the answer is computed.
/// </para>
/// <para>
/// What the naive solver does get wrong is not precision but the starting guess. At e = 0.99,
/// <c>E₀ = M</c> sends <see langword="float"/>'s iteration at M = 0.1 off to 8.7e10 and it never
/// returns, while the universal solver converges in every type at every mean anomaly. That is the
/// failure the universal formulation actually exists to avoid, and
/// <see cref="NaiveStartingGuess_IsWhatFailsFirst_AtHigherEccentricity"/> pins it.
/// </para>
/// <para>
/// The cancellation is real, and it scales the way it should. At e = 0.99, where <c>1 − e</c> is a
/// hundredth, the naive solve loses up to 1.7 digits of position near perigee in
/// <see langword="double"/> and in thirty-digit <see cref="PreciseNumber"/> alike — two digits of
/// cancellation, give or take, in every type that carries relative precision. More digits move the
/// floor; they do not remove the loss.
/// </para>
/// </remarks>
[TestClass]
public sealed class KeplerEquationDigitLossTests
{
	/// <summary>The reference arithmetic's working precision, in significant digits.</summary>
	private const int ReferenceDigits = 60;

	/// <summary>The probe precision for <see cref="PreciseNumber"/>, in significant digits.</summary>
	private const int ProbeDigits = 30;

	/// <summary>A Molniya semi-major axis, in km.</summary>
	private const double SemiMajorAxis = 26554.0;

	/// <summary>A Molniya eccentricity.</summary>
	private const double MolniyaEccentricity = 0.74;

	/// <summary>An eccentricity past Molniya's, where the naive starting guess gives out.</summary>
	private const double HighEccentricity = 0.99;

	/// <summary>The Earth's gravitational parameter, EGM96, in km³/s².</summary>
	private const double Mu = 398600.4418;

	/// <summary>Mean anomalies past perigee, in radians, approaching it.</summary>
	private static readonly double[] MeanAnomalies = [0.5, 1e-1, 1e-2, 1e-3, 1e-4, 1e-6];

	private static readonly Lazy<List<Row>> Molniya = new(() => Measure(MolniyaEccentricity));

	private static readonly Lazy<List<Row>> High = new(() => Measure(HighEccentricity));

	/// <summary>What one storage type measured at one mean anomaly.</summary>
	/// <param name="Type">The storage type's name.</param>
	/// <param name="MeanAnomaly">The mean anomaly, in radians.</param>
	/// <param name="Available">The storage type's own digits: minus log10 of its unit round-off.</param>
	/// <param name="NaiveAnomalyLost">Digits of the eccentric anomaly the naive solver lost.</param>
	/// <param name="NaivePositionLost">Digits of position the naive solver lost.</param>
	/// <param name="UniversalPositionLost">Digits of position the universal-variable propagator lost.</param>
	/// <param name="NaiveIterations">Steps the naive solver took.</param>
	/// <param name="UniversalIterations">Steps the universal solver took.</param>
	/// <param name="NaiveConverged">Whether the naive solver converged.</param>
	/// <param name="UniversalConverged">Whether the universal solver converged.</param>
	private sealed record Row(
		string Type,
		double MeanAnomaly,
		double Available,
		double NaiveAnomalyLost,
		double NaivePositionLost,
		double UniversalPositionLost,
		int NaiveIterations,
		int UniversalIterations,
		bool NaiveConverged,
		bool UniversalConverged);

	/// <summary>One solve in a storage type's own arithmetic, lifted to the reference type.</summary>
	/// <param name="Anomaly">The naive solver's eccentric anomaly.</param>
	/// <param name="NaiveX">Perifocal x from the naive solve, in km.</param>
	/// <param name="NaiveY">Perifocal y from the naive solve, in km.</param>
	/// <param name="UniversalX">Perifocal x from the universal-variable propagation, in km.</param>
	/// <param name="UniversalY">Perifocal y from the universal-variable propagation, in km.</param>
	/// <param name="Naive">How the naive solve went.</param>
	/// <param name="Universal">How the universal solve went.</param>
	private sealed record Solve(
		PreciseNumber Anomaly,
		PreciseNumber NaiveX,
		PreciseNumber NaiveY,
		PreciseNumber UniversalX,
		PreciseNumber UniversalY,
		(int Iterations, bool Converged) Naive,
		(int Iterations, bool Converged) Universal);

	[TestMethod]
	public void TheTwoReferences_AgreeWithEachOther()
	{
		PreciseStorageMath reference = new(ReferenceDigits);
		foreach (double eccentricity in new[] { MolniyaEccentricity, HighEccentricity })
		{
			foreach (double meanAnomaly in MeanAnomalies)
			{
				Solve solve = Run(meanAnomaly, eccentricity, reference);
				Assert.IsTrue(solve.Naive.Converged && solve.Universal.Converged, $"The reference failed to converge at e = {eccentricity}, M = {meanAnomaly}.");

				// Two unrelated equations for the same physical point. Agreeing at sixty digits to far
				// beyond anything a probe is measured to is what makes either of them a reference.
				double agreement = RelativeDistance(solve.NaiveX, solve.NaiveY, solve.UniversalX, solve.UniversalY);
				Assert.IsLessThan(1e-45, agreement, $"The two sixty-digit formulations disagree at e = {eccentricity}, M = {meanAnomaly}.");
			}
		}
	}

	[TestMethod]
	public void AtMolniyaEccentricity_NeitherFormulationLosesADigit_InBinaryOrPrecise()
	{
		List<Row> rows = Molniya.Value;
		Print(MolniyaEccentricity, rows);

		Assert.IsTrue(rows.All(r => r.NaiveConverged && r.UniversalConverged), "Every solve at e = 0.74 is expected to converge.");

		foreach (Row row in rows.Where(r => r.Type != "decimal"))
		{
			// Measured: worst 0.73 digits naive, 0.57 universal, over float, double and precise.
			Assert.IsLessThan(1.0, row.NaivePositionLost, $"{row.Type} naive at M = {row.MeanAnomaly}");
			Assert.IsLessThan(1.0, row.NaiveAnomalyLost, $"{row.Type} naive E at M = {row.MeanAnomaly}");
			Assert.IsLessThan(1.0, row.UniversalPositionLost, $"{row.Type} universal at M = {row.MeanAnomaly}");
		}
	}

	[TestMethod]
	public void AtMolniyaEccentricity_DecimalLosesDigitsToItsAbsolutePrecision()
	{
		List<Row> decimals = [.. Molniya.Value.Where(r => r.Type == "decimal")];
		Row nearest = decimals.Single(r => r.MeanAnomaly == MeanAnomalies[^1]);
		Row farthest = decimals.Single(r => r.MeanAnomaly == MeanAnomalies[0]);

		// The naive eccentric anomaly loses digits in step with how small it is: E ≈ M / (1 − e), and
		// a decimal holds a number of that size to 28 + log10(E) significant digits. Measured 0.12
		// at M = 0.5 and 5.26 at M = 1e-6.
		Assert.IsGreaterThan(4.5, nearest.NaiveAnomalyLost, "decimal is expected to lose digits of a small eccentric anomaly.");
		Assert.IsLessThan(6.0, nearest.NaiveAnomalyLost);
		Assert.IsLessThan(1.0, farthest.NaiveAnomalyLost, "an eccentric anomaly near one costs decimal nothing.");

		// The universal propagation's intermediates are small at every mean anomaly. Measured worst
		// 6.51 digits of position, against 0.57 for any other type.
		double worstUniversal = decimals.Max(r => r.UniversalPositionLost);
		Assert.IsGreaterThan(5.5, worstUniversal, "decimal is expected to lose digits in the universal intermediates.");
		Assert.IsLessThan(7.5, worstUniversal);
	}

	[TestMethod]
	public void NaiveStartingGuess_IsWhatFailsFirst_AtHigherEccentricity()
	{
		List<Row> rows = High.Value;
		Print(HighEccentricity, rows);

		Assert.IsTrue(rows.All(r => r.UniversalConverged), "The universal solver is expected to converge everywhere at e = 0.99.");

		// E₀ = M at M = 0.1 sends single precision's Newton iteration away and it does not return.
		// Measured: 8.7e10 after the iteration cap.
		Row diverged = rows.Single(r => r.Type == "float" && r.MeanAnomaly == 0.1);
		Assert.IsFalse(diverged.NaiveConverged, "The naive float solve at e = 0.99, M = 0.1 is expected to diverge.");

		// And the cancellation starts to show: 1 − e = 0.01 is two digits of it. Measured worst 1.67
		// digits of double position at M <= 0.01, against 0.29 at e = 0.74.
		double worstDouble = rows.Where(r => r.Type == "double" && r.MeanAnomaly <= 1e-2).Max(r => r.NaivePositionLost);
		Assert.IsGreaterThan(1.2, worstDouble, "At e = 0.99 the naive solve is expected to lose more than a digit near perigee.");
		Assert.IsLessThan(2.5, worstDouble);
	}

	/// <summary>Writes the measured table to the test output.</summary>
	/// <param name="eccentricity">The eccentricity measured.</param>
	/// <param name="rows">The rows.</param>
	private static void Print(double eccentricity, List<Row> rows)
	{
		Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "e = {0}; digits lost against a {1}-digit reference", eccentricity, ReferenceDigits));
		Console.WriteLine("type          M   avail  naive E  naive r  universal r  steps n/u");
		foreach (Row row in rows)
		{
			Console.WriteLine(string.Format(
				CultureInfo.InvariantCulture,
				"{0,-8} {1,7:G3} {2,7:F2} {3,8} {4,8} {5,12}  {6}/{7}",
				row.Type,
				row.MeanAnomaly,
				row.Available,
				row.NaiveConverged ? row.NaiveAnomalyLost.ToString("F2", CultureInfo.InvariantCulture) : "diverged",
				row.NaiveConverged ? row.NaivePositionLost.ToString("F2", CultureInfo.InvariantCulture) : "diverged",
				row.UniversalConverged ? row.UniversalPositionLost.ToString("F2", CultureInfo.InvariantCulture) : "diverged",
				row.NaiveIterations,
				row.UniversalIterations));
		}
	}

	/// <summary>Runs every storage type at every mean anomaly for one eccentricity.</summary>
	/// <param name="eccentricity">The eccentricity.</param>
	/// <returns>The rows.</returns>
	private static List<Row> Measure(double eccentricity)
	{
		PreciseStorageMath reference = new(ReferenceDigits);
		PreciseStorageMath probe = new(ProbeDigits);
		List<Row> rows = [];

		foreach (double meanAnomaly in MeanAnomalies)
		{
			Solve truth = Run(meanAnomaly, eccentricity, reference);
			rows.Add(Compare("float", meanAnomaly, -Math.Log10(Math.Pow(2, -24)), Run(meanAnomaly, eccentricity, FloatStorageMath.Instance), truth));
			rows.Add(Compare("double", meanAnomaly, -Math.Log10(Math.Pow(2, -53)), Run(meanAnomaly, eccentricity, DoubleStorageMath.Instance), truth));
			rows.Add(Compare("decimal", meanAnomaly, 28.0, Run(meanAnomaly, eccentricity, DecimalStorageMath.Instance), truth));
			rows.Add(Compare("precise", meanAnomaly, ProbeDigits, Run(meanAnomaly, eccentricity, probe), truth));
		}

		return rows;
	}

	/// <summary>Turns a probe solve into a row by measuring it against the reference.</summary>
	/// <param name="type">The storage type's name.</param>
	/// <param name="meanAnomaly">The mean anomaly.</param>
	/// <param name="available">The storage type's own digits.</param>
	/// <param name="probe">The probe solve.</param>
	/// <param name="truth">The reference solve.</param>
	/// <returns>The row.</returns>
	private static Row Compare(string type, double meanAnomaly, double available, Solve probe, Solve truth)
	{
		double anomalyError = (PreciseNumber.Abs(probe.Anomaly - truth.Anomaly) / PreciseNumber.Abs(truth.Anomaly)).To<double>();
		double naive = RelativeDistance(probe.NaiveX, probe.NaiveY, truth.NaiveX, truth.NaiveY);
		double universal = RelativeDistance(probe.UniversalX, probe.UniversalY, truth.UniversalX, truth.UniversalY);

		return new(
			type,
			meanAnomaly,
			available,
			Lost(anomalyError, available),
			Lost(naive, available),
			Lost(universal, available),
			probe.Naive.Iterations,
			probe.Universal.Iterations,
			probe.Naive.Converged,
			probe.Universal.Converged);
	}

	/// <summary>Digits lost for a relative error: what the type has, less what survived.</summary>
	/// <param name="relativeError">The relative error.</param>
	/// <param name="available">The type's own digits.</param>
	/// <returns>Digits lost, never negative.</returns>
	private static double Lost(double relativeError, double available) =>
		relativeError <= 0.0 ? 0.0 : Math.Max(0.0, available + Math.Log10(relativeError));

	/// <summary>The distance between two planar points, relative to the second one's radius.</summary>
	/// <param name="x">The first point's x.</param>
	/// <param name="y">The first point's y.</param>
	/// <param name="referenceX">The reference point's x.</param>
	/// <param name="referenceY">The reference point's y.</param>
	/// <returns>The relative distance.</returns>
	private static double RelativeDistance(PreciseNumber x, PreciseNumber y, PreciseNumber referenceX, PreciseNumber referenceY)
	{
		PreciseNumber dx = x - referenceX;
		PreciseNumber dy = y - referenceY;
		double distance = Math.Sqrt(((dx * dx) + (dy * dy)).To<double>());
		double radius = Math.Sqrt(((referenceX * referenceX) + (referenceY * referenceY)).To<double>());
		return distance / radius;
	}

	/// <summary>Solves the near-perigee point both ways in one storage type.</summary>
	/// <typeparam name="T">The storage type.</typeparam>
	/// <param name="meanAnomaly">The mean anomaly past perigee, in radians.</param>
	/// <param name="eccentricity">The eccentricity.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The solve, lifted to the reference type.</returns>
	/// <remarks>
	/// The naive path solves Kepler's equation for E and places the point in the perifocal frame
	/// directly. The universal path starts at perigee and propagates the mean anomaly's worth of time,
	/// so it reaches the same point through an unrelated equation. Both start at perigee so the two
	/// solve equally conditioned problems: a start at apogee was tried first and lost a further one
	/// and a half digits in every type, because half an orbit of time multiplies a relative error in
	/// the time by v·t / r ≈ 30 at perigee — the problem's conditioning, not either formulation's.
	/// Every input is a double literal converted once into <typeparamref name="T"/>, so the reference
	/// starts from exactly the values the double run does.
	/// </remarks>
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

		return new(
			anomaly.ToPreciseNumber(),
			naiveX.ToPreciseNumber(),
			naiveY.ToPreciseNumber(),
			universal.State.X.ToPreciseNumber(),
			universal.State.Y.ToPreciseNumber(),
			(naive.Iterations, naive.Converged),
			(universal.Solution.Iterations, universal.Solution.Converged));
	}
}
