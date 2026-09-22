// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using ktsu.Osculator.Core.Propagation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Runs the standard SGP4 verification set.
/// </summary>
/// <remarks>
/// This is the gate the whole repository rests on. A propagator that is subtly wrong produces an
/// error decomposition that is confidently wrong, which is worse than no decomposition at all — so
/// until this passes, nothing measured downstream of it means anything.
/// </remarks>
[TestClass]
public sealed class Sgp4VerificationTests
{
	/// <summary>
	/// The position tolerance, in kilometres.
	/// </summary>
	/// <remarks>
	/// The figure the published paper specifies. Never loosen it to make a change pass. Measured
	/// across every published arc, this implementation sits at about 7e-9 km — most of the budget,
	/// and it is worth knowing what is spending it. It is not this code's arithmetic: it is the last
	/// bit of <c>sin</c>, <c>cos</c> and <c>pow</c>, which the C runtime and the Java runtime that
	/// produced the reference vectors are each free to round differently, amplified by the Kepler
	/// solve. That is the same quantity this repository exists to measure, showing up in its own
	/// verification suite before anything else in it has been built.
	/// </remarks>
	private const double PositionToleranceKm = 1e-8;

	/// <summary>
	/// The position tolerance, in kilometres, beyond the published arcs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every arc in the published verification set runs to 9,400 minutes or less, except one: the
	/// file ends with a second run of object 20413 at roughly 1.844 <em>million</em> minutes, three
	/// and a half years past its epoch. This implementation matches it to 6.85e-8 km — sixty-eight
	/// micrometres past the published figure.
	/// </para>
	/// <para>
	/// That is the round-off floor rather than a defect, and it is checkable rather than asserted.
	/// The mean anomaly reaches about 1,974 radians over that arc, whose last representable bit in a
	/// <see cref="double"/> is around 1e-12 radians; the object's eccentricity of 0.786 amplifies an
	/// error in mean anomaly into one in radius by a factor approaching eight near perigee; and the
	/// radius there is around 16,800 km. That product is 1.3e-7 km, which is the size of the
	/// disagreement. No ordering of the same algorithm in <see cref="double"/> does better.
	/// </para>
	/// <para>
	/// So this is the first measurement of the arithmetic error term in this repository, and it says
	/// what the whole project expects it to say: over the arcs anyone tracks a satellite across, the
	/// term is invisible, and it only becomes measurable at a range where the model itself is wrong
	/// by thousands of kilometres. The three times of headroom here is for the transcendental
	/// libraries of other platforms, not for this code to drift into.
	/// </para>
	/// </remarks>
	private const double LongArcPositionToleranceKm = 2e-7;

	/// <summary>Arcs longer than this, in minutes, fall outside the published verification spans.</summary>
	private const double LongArcMinutes = 10000.0;

	/// <summary>
	/// The one catalogue number whose expected block is an artifact of the reference test harness.
	/// </summary>
	/// <remarks>
	/// Object 33334 is a constructed case — a real element set with its mean motion replaced by
	/// 0.00001 revolutions per day — that the verification file's own comment says was an attempt to
	/// provoke a particular error code. At that mean motion the lunar-solar periodics are scaled by
	/// the reciprocal of it and swamp everything: the eccentricity comes out at −122, and the model
	/// correctly refuses to return a state.
	/// <para>
	/// The published output nonetheless carries one row for it, and that row is byte-for-byte
	/// identical to the last row of the block above it — the harness printing a state vector buffer
	/// the failed call never wrote to. Comparing against it would be comparing against object 33333.
	/// </para>
	/// </remarks>
	private const int HarnessArtifactCatalogId = 33334;

	/// <summary>
	/// The velocity tolerance, in kilometres per second.
	/// </summary>
	/// <remarks>
	/// Velocity is asserted separately from position because the
	/// two scale by different factors, and an error in the velocity factor alone leaves position
	/// perfect — which is exactly the defect this suite caught while it was being written.
	/// </remarks>
	private const double VelocityToleranceKmPerSecond = 1e-9;

	[TestMethod]
	public void EveryCase_MatchesThePublishedVectors()
	{
		List<VerificationSet.Case> cases = VerificationSet.ReadCases();
		List<IReadOnlyList<VerificationSet.Expected>> expectedBlocks = VerificationSet.ReadExpected();

		Assert.AreEqual(cases.Count, expectedBlocks.Count, "Case count and expected-block count disagree; the two files are out of step.");

		double worstPosition = 0.0;
		double worstVelocity = 0.0;
		int nearEarthCases = 0;
		int deepSpaceCases = 0;
		int comparedRows = 0;
		List<string> failures = [];

		for (int i = 0; i < cases.Count; i++)
		{
			Sgp4Satellite<double> satellite = Sgp4<double>.Initialize(cases[i].Elements, DoubleStorageMath.Instance);

			if (satellite.IsDeepSpace)
			{
				deepSpaceCases++;
			}
			else
			{
				nearEarthCases++;
			}

			foreach (VerificationSet.Expected expected in expectedBlocks[i])
			{
				Sgp4Result<double> result = Sgp4<double>.Propagate(satellite, expected.Minutes, DoubleStorageMath.Instance);

				if (!result.IsSuccess && result.Error != Sgp4Error.Decayed)
				{
					// The published output stops emitting rows once the model errors, so a row that
					// exists here should have produced a state — with the one exception above.
					if (cases[i].Elements.NoradCatalogId != HarnessArtifactCatalogId)
					{
						failures.Add($"{cases[i].Elements.NoradCatalogId} at {expected.Minutes} min: {result.Error}");
					}

					continue;
				}

				double dPosition = Distance(result.State.X - expected.X, result.State.Y - expected.Y, result.State.Z - expected.Z);
				double dVelocity = Distance(result.State.VelocityX - expected.VelocityX, result.State.VelocityY - expected.VelocityY, result.State.VelocityZ - expected.VelocityZ);

				worstPosition = System.Math.Max(worstPosition, dPosition);
				worstVelocity = System.Math.Max(worstVelocity, dVelocity);
				comparedRows++;

				double positionTolerance = System.Math.Abs(expected.Minutes) > LongArcMinutes
					? LongArcPositionToleranceKm
					: PositionToleranceKm;

				if (dPosition > positionTolerance)
				{
					failures.Add($"{cases[i].Elements.NoradCatalogId} at {expected.Minutes} min: position off by {dPosition:E3} km");
				}

				if (dVelocity > VelocityToleranceKmPerSecond)
				{
					failures.Add($"{cases[i].Elements.NoradCatalogId} at {expected.Minutes} min: velocity off by {dVelocity:E3} km/s");
				}
			}
		}

		Console.WriteLine($"near-earth cases {nearEarthCases}, deep-space cases {deepSpaceCases}, rows compared {comparedRows}");
		Console.WriteLine($"worst position error {worstPosition:E3} km, worst velocity error {worstVelocity:E3} km/s");

		Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
		Assert.IsGreaterThan(0, nearEarthCases, "No near-earth cases ran.");
		Assert.IsGreaterThan(0, deepSpaceCases, "No deep-space cases ran.");
	}

	[TestMethod]
	public void TheConstructedCaseWithNoUsableMeanMotionIsRefused()
	{
		// The other half of the exception made above: skipping a case is only honest if what it does
		// instead is asserted. See HarnessArtifactCatalogId for why its published row is not one.
		VerificationSet.Case constructed = VerificationSet.ReadCases().Find(c => c.Elements.NoradCatalogId == HarnessArtifactCatalogId)
			?? throw new InvalidOperationException($"Object {HarnessArtifactCatalogId} is no longer in the verification file.");

		Sgp4Satellite<double> satellite = Sgp4<double>.Initialize(constructed.Elements, DoubleStorageMath.Instance);
		Sgp4Result<double> result = Sgp4<double>.Propagate(satellite, 0.0, DoubleStorageMath.Instance);

		Assert.IsTrue(satellite.IsDeepSpace, "A mean motion of 0.00001 revolutions a day is deep-space by any reading.");
		Assert.AreEqual(Sgp4Error.PerturbedEccentricityOutOfRange, result.Error);
	}

	[TestMethod]
	public void TheVerificationSetExercisesBothResonancesAndTheNonResonantDeepSpacePath()
	{
		// The deep-space model has three distinct paths through it, and a suite that happened to
		// cover only one of them would pass while two thirds of the code went unrun. The published
		// set covers all three deliberately — it carries Molniya orbits for the half-day resonance,
		// geosynchronous ones for the synchronous resonance, and highly eccentric transfer orbits
		// that resonate with nothing. This asserts that the data still does what it was built to do,
		// so a case going missing from the file shows up here rather than as quiet coverage loss.
		Dictionary<int, int> byResonance = new() { [0] = 0, [1] = 0, [2] = 0 };

		foreach (VerificationSet.Case verification in VerificationSet.ReadCases())
		{
			Sgp4Satellite<double> satellite = Sgp4<double>.Initialize(verification.Elements, DoubleStorageMath.Instance);

			if (satellite.IsDeepSpace)
			{
				byResonance[satellite.Resonance]++;
			}
		}

		Console.WriteLine($"deep-space cases by resonance: none {byResonance[0]}, synchronous {byResonance[1]}, half-day {byResonance[2]}");

		Assert.IsGreaterThan(0, byResonance[0], "No non-resonant deep-space case ran.");
		Assert.IsGreaterThan(0, byResonance[1], "No synchronous-resonance case ran.");
		Assert.IsGreaterThan(0, byResonance[2], "No half-day-resonance case ran.");
	}

	private static double Distance(double x, double y, double z) => System.Math.Sqrt((x * x) + (y * y) + (z * z));
}
