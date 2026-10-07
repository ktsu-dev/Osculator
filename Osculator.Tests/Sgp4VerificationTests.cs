// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
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

	/// <summary>The first case in the published set, a near-earth object.</summary>
	private const int NearEarthCatalogId = 5;

	/// <summary>The half-day-resonant case a quantized epoch hurt most.</summary>
	private const int HalfDayResonantCatalogId = 22674;

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

	[TestMethod]
	[DataRow(1.0, 15.5, Sgp4Error.EccentricityOutOfRange, DisplayName = "e = 1")]
	[DataRow(1.2, 15.5, Sgp4Error.EccentricityOutOfRange, DisplayName = "e > 1")]
	[DataRow(0.0005, 0.0, Sgp4Error.MeanMotionNotPositive, DisplayName = "mean motion 0")]
	[DataRow(0.0005, -1.0, Sgp4Error.MeanMotionNotPositive, DisplayName = "mean motion negative")]
	public void AnElementSetOutsideTheModelIsRefusedTheSameWayInEveryStorageType(double eccentricity, double meanMotion, Sgp4Error expected)
	{
		// A two-line set cannot write an eccentricity of one or more, but OMM JSON, the nudged sets the
		// data term builds and any `with` expression can. double used to report e = 1 as a success
		// holding NaN, while decimal and PreciseNumber threw on the same input — so a sweep that
		// skipped the set in one type crashed in another, and the harness's promise that only the
		// storage type differs did not hold. Each of these must come back as the same error, and none
		// may throw. A near-earth and a deep-space base are both run, since the two part ways early.
		foreach (int catalogId in new[] { NearEarthCatalogId, HalfDayResonantCatalogId })
		{
			ElementSet outside = CaseFor(catalogId).Elements with { Eccentricity = eccentricity, MeanMotion = meanMotion };

			Assert.AreEqual(expected, ErrorAtEpoch(outside, DoubleStorageMath.Instance), $"double, base {catalogId}");
			Assert.AreEqual(expected, ErrorAtEpoch(outside, FloatStorageMath.Instance), $"float, base {catalogId}");
			Assert.AreEqual(expected, ErrorAtEpoch(outside, DecimalStorageMath.Instance), $"decimal, base {catalogId}");
			Assert.AreEqual(expected, ErrorAtEpoch(outside, new PreciseStorageMath(30)), $"PreciseNumber, base {catalogId}");
		}
	}

	[TestMethod]
	[DataRow(0.97, 0.2, DisplayName = "e = 0.97, 0.2 rev/day")]
	[DataRow(0.7, 0.05, DisplayName = "e = 0.7, 0.05 rev/day")]
	public void AnEccentricityTheSecularTermsCarryOutOfRangeIsRefusedInEveryStorageType(double eccentricity, double meanMotion)
	{
		// The other eccentricity guard, the one in Propagate rather than Initialize. Nothing in the
		// published set drives the secular eccentricity out of range, so without this an edit to
		// that guard keeps every other test passing. A slow, eccentric deep-space orbit does it given
		// time: the lunar-solar secular rates carry the eccentricity steadily, and a thousand days is
		// enough to carry these two past one. Deleting the `em >= 1` half of that guard turns both into
		// a later, different error code, which is what this pins.
		ElementSet carried = CaseFor(HalfDayResonantCatalogId).Elements with { Eccentricity = eccentricity, MeanMotion = meanMotion };
		const double ThousandDays = 1440000.0;

		Assert.AreEqual(Sgp4Error.EccentricityOutOfRange, ErrorAt(carried, ThousandDays, DoubleStorageMath.Instance), "double");
		Assert.AreEqual(Sgp4Error.EccentricityOutOfRange, ErrorAt(carried, (float)ThousandDays, FloatStorageMath.Instance), "float");
		Assert.AreEqual(Sgp4Error.EccentricityOutOfRange, ErrorAt(carried, (decimal)ThousandDays, DecimalStorageMath.Instance), "decimal");
		Assert.AreEqual(Sgp4Error.EccentricityOutOfRange, ErrorAt(carried, ThousandDays.ToPreciseNumber(), new PreciseStorageMath(30)), "PreciseNumber");

		// At a day the same element set is fine, so it is the elapsed time doing it.
		Assert.AreEqual(Sgp4Error.None, ErrorAt(carried, 1440.0, DoubleStorageMath.Instance));
	}

	[TestMethod]
	public void FloatOnAHalfDayResonantCaseIsLimitedByItsArithmetic_NotByAQuantizedEpoch()
	{
		// The deep-space model's epoch sidereal time sets the phase of the resonance forcing and does
		// not cancel out of it. Evaluated in float from one Julian date, the epoch was first snapped to
		// the nearest quarter of a day, and this case — the worst of the half-day resonances — came
		// out 20.47 km from the published vectors. Sidereal time is now an input shared by every
		// storage type, and the same arc measures 0.09 km.
		VerificationSet.Case resonant = CaseFor(HalfDayResonantCatalogId);
		IReadOnlyList<VerificationSet.Expected> expected = VerificationSet.ReadExpected()[VerificationSet.ReadCases().FindIndex(c => c.Elements.NoradCatalogId == HalfDayResonantCatalogId)];
		Sgp4Satellite<float> satellite = Sgp4<float>.Initialize(resonant.Elements, FloatStorageMath.Instance);

		Assert.AreEqual(2, satellite.Resonance, "Object 22674 is expected to be the half-day resonance.");

		double worst = 0.0;

		foreach (VerificationSet.Expected row in expected)
		{
			Sgp4Result<float> result = Sgp4<float>.Propagate(satellite, (float)row.Minutes, FloatStorageMath.Instance);
			Assert.IsTrue(result.IsSuccess, $"float refused {HalfDayResonantCatalogId} at {row.Minutes} min: {result.Error}");
			worst = System.Math.Max(worst, Distance(result.State.X - row.X, result.State.Y - row.Y, result.State.Z - row.Z));
		}

		Console.WriteLine($"float on {HalfDayResonantCatalogId}: worst {worst:E3} km over {expected.Count} rows");

		// Measured 0.090 km. The bound is a little over twice that, and twenty times below where a
		// quantized epoch put it.
		Assert.IsLessThan(0.2, worst);
	}

	[TestMethod]
	public void TheWgs72ConstantsAreTheirPublishedLiteralsInEveryStorageType()
	{
		// Parsed from the literal per storage type rather than converted from double, so a wide type
		// starts from the published value and not from double's binary approximation of it.
		Assert.AreEqual(6378.135m, Wgs72<decimal>.RadiusEarthKm);
		Assert.AreEqual(398600.8m, Wgs72<decimal>.Mu);
		Assert.AreEqual(0.001082616m, Wgs72<decimal>.J2);
		Assert.AreEqual(-0.00000253881m, Wgs72<decimal>.J3);
		Assert.AreEqual(-0.00000165597m, Wgs72<decimal>.J4);

		Assert.AreEqual(PreciseNumber.Parse("6378.135", CultureInfo.InvariantCulture), Wgs72<PreciseNumber>.RadiusEarthKm);
		Assert.AreEqual(PreciseNumber.Parse("398600.8", CultureInfo.InvariantCulture), Wgs72<PreciseNumber>.Mu);
		Assert.AreEqual(PreciseNumber.Parse("0.001082616", CultureInfo.InvariantCulture), Wgs72<PreciseNumber>.J2);

		// float is the literal rounded once to single precision, not rounded to double and again.
		Assert.AreEqual(float.Parse("0.001082616", CultureInfo.InvariantCulture), Wgs72<float>.J2);
		Assert.AreEqual(6378.135, Wgs72<double>.RadiusEarthKm);
	}

	private static VerificationSet.Case CaseFor(int catalogId) =>
		VerificationSet.ReadCases().Find(c => c.Elements.NoradCatalogId == catalogId)
			?? throw new InvalidOperationException($"Object {catalogId} is no longer in the verification file.");

	private static Sgp4Error ErrorAtEpoch<T>(ElementSet elements, IStorageMath<T> math)
		where T : struct, System.Numerics.INumber<T> =>
		ErrorAt(elements, T.Zero, math);

	private static Sgp4Error ErrorAt<T>(ElementSet elements, T minutes, IStorageMath<T> math)
		where T : struct, System.Numerics.INumber<T> =>
		Sgp4<T>.Propagate(Sgp4<T>.Initialize(elements, math), minutes, math).Error;

	private static double Distance(double x, double y, double z) => System.Math.Sqrt((x * x) + (y * y) + (z * z));
}
