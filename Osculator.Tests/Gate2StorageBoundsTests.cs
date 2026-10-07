// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Validation gate 2: the verification set in every storage type, against the published vectors,
/// with a stated tolerance per type.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StorageComparisonTests"/> measures the storage types against each other and against a
/// thirty-digit reference. What it does not do is put an upper bound on how far
/// <see langword="decimal"/> or <see cref="PreciseNumber"/> may stray from the published vectors,
/// and its decimal assertions are medians or comparisons with <see langword="double"/>, neither of
/// which moves when one row of six hundred goes bad. So a regression confined to one storage type's
/// branch — a resonance term computed through <c>DecimalMath</c>, or a <c>PreciseStorageMath</c>
/// transcendental — could put kilometres on a single row and pass. This class closes that: every
/// row, in each of those two types, is held to the type's tolerance on its own.
/// </para>
/// <para>
/// Mutation-checked: adding one kilometre to the position of the half-day resonance cases in the
/// <see langword="decimal"/> path alone fails the decimal test on every row of object 8195 and
/// leaves every test in <see cref="StorageComparisonTests"/> passing; the same kilometre in the
/// <see cref="PreciseNumber"/> path alone fails the PreciseNumber test.
/// </para>
/// <para>
/// The tolerances are not 1e-8 km, and cannot be. The published vectors were computed in
/// <see langword="double"/>, so they carry <see langword="double"/>'s own rounding, and a run that
/// rounds differently — even one that rounds less — disagrees with them by about that much. The
/// thirty-digit run is the clearest case: it is the more correct of the two and still sits 7.3e-8 km
/// from the vectors. What "passes" means for these types is therefore "agrees with the published
/// vectors to within the vectors' own precision, with margin", and the margin is what stops the
/// tolerance meaning anything else.
/// </para>
/// <para>
/// <see langword="float"/> has no tolerance. It was never expected to meet one, and
/// <see cref="StorageComparisonTests"/> records where it lands and that it does so silently.
/// </para>
/// </remarks>
[TestClass]
public sealed class Gate2StorageBoundsTests
{
	/// <summary>The gate-2 position tolerance for <see langword="decimal"/>, in kilometres.</summary>
	/// <remarks>
	/// Measured worst over the published arcs: 4.2e-7 km, and 1.3e-7 km on the long arc — the second
	/// run of 20413, three and a half years out, which gate 1 holds to a looser bound because the
	/// vectors' own <see langword="double"/> round-off is visible there. One bound covers both here:
	/// a little over twice the worst row, so a regression of a metre on any one row fails it, and loose
	/// enough not to be a coin toss on another platform's rounding.
	/// </remarks>
	private const double DecimalToleranceKm = 1e-6;

	/// <summary>The gate-2 position tolerance for <see cref="PreciseNumber"/>, in kilometres.</summary>
	/// <remarks>
	/// Measured worst over the published arcs: 7.3e-8 km, and 9.3e-8 km on the long arc. The same
	/// figure as <see langword="decimal"/>, because what both are measured against is the vectors'
	/// own <see langword="double"/> rounding, not either type's arithmetic.
	/// </remarks>
	private const double PreciseToleranceKm = 1e-6;

	/// <summary>The gate-2 velocity tolerance for both types, in kilometres per second.</summary>
	/// <remarks>
	/// Velocity is asserted separately for the reason gate 1 does: its conversion factor differs from
	/// position's, and an error in that factor alone leaves position untouched. Measured worst:
	/// 8.5e-10 km/s in both types, which is <see langword="double"/>'s own figure to the digit — the
	/// published velocities carry nine decimal places, and that is what is being measured.
	/// </remarks>
	private const double VelocityToleranceKmPerSecond = 1e-8;

	/// <summary>Arcs longer than this, in minutes, fall outside the published verification spans.</summary>
	private const double LongArcMinutes = 10000.0;

	/// <summary>The working precision of the arbitrary-precision run, in significant digits.</summary>
	private const int PreciseDigits = 30;

	/// <summary>The constructed case whose one published row is a harness artifact.</summary>
	private const int HarnessArtifactCatalogId = 33334;

	private static readonly Lazy<List<Row>> MeasuredDecimal = new(() => Measure(
		e => Sgp4<decimal>.Initialize(e, DecimalStorageMath.Instance),
		(s, minutes) => Sgp4<decimal>.Propagate(s, (decimal)minutes, DecimalStorageMath.Instance),
		v => (double)v));

	private static readonly Lazy<List<Row>> MeasuredPrecise = new(() =>
	{
		PreciseStorageMath precise = new(PreciseDigits);

		return Measure(
			e => Sgp4<PreciseNumber>.Initialize(e, precise),
			(s, minutes) => Sgp4<PreciseNumber>.Propagate(s, minutes.ToPreciseNumber(), precise),
			v => v.To<double>());
	});

	[TestMethod]
	public void DecimalAgreesWithThePublishedVectorsOnEveryRow() =>
		AssertWithinTolerance("decimal", MeasuredDecimal.Value, DecimalToleranceKm);

	[TestMethod]
	public void PreciseNumberAgreesWithThePublishedVectorsOnEveryRow() =>
		AssertWithinTolerance($"precise({PreciseDigits})", MeasuredPrecise.Value, PreciseToleranceKm);

	private static void AssertWithinTolerance(string type, List<Row> rows, double positionTolerance)
	{
		List<string> failures = [];
		double worstPosition = 0.0;
		double worstVelocity = 0.0;
		double worstLongArc = 0.0;
		int compared = 0;

		foreach (Row row in rows)
		{
			if (row.Error != Sgp4Error.None)
			{
				// The model in double produced a state here; refusing it in another type is a defect,
				// not a row to skip.
				failures.Add(string.Create(CultureInfo.InvariantCulture, $"{row.CatalogId} at {row.Minutes} min: {row.Error}"));
				continue;
			}

			compared++;
			if (row.IsLongArc)
			{
				worstLongArc = System.Math.Max(worstLongArc, row.PositionKm);
			}
			else
			{
				worstPosition = System.Math.Max(worstPosition, row.PositionKm);
			}

			worstVelocity = System.Math.Max(worstVelocity, row.VelocityKmPerSecond);

			if (!(row.PositionKm <= positionTolerance))
			{
				failures.Add(string.Create(CultureInfo.InvariantCulture, $"{row.CatalogId} at {row.Minutes} min: position off by {row.PositionKm:E3} km"));
			}

			if (!(row.VelocityKmPerSecond <= VelocityToleranceKmPerSecond))
			{
				failures.Add(string.Create(CultureInfo.InvariantCulture, $"{row.CatalogId} at {row.Minutes} min: velocity off by {row.VelocityKmPerSecond:E3} km/s"));
			}
		}

		Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{type}: {compared} rows, worst position {worstPosition:E3} km over the published arcs, {worstLongArc:E3} km on the long arc, worst velocity {worstVelocity:E3} km/s"));

		Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
		Assert.IsGreaterThan(600, compared, "Far fewer rows were compared than the verification set holds; the measurement is not running over it.");
	}

	private static List<Row> Measure<TSatellite, TState>(
		Func<Core.Elements.ElementSet, TSatellite> initialize,
		Func<TSatellite, double, Sgp4Result<TState>> propagate,
		Func<TState, double> toDouble)
		where TState : struct, System.Numerics.INumber<TState>
	{
		List<VerificationSet.Case> cases = VerificationSet.ReadCases();
		List<IReadOnlyList<VerificationSet.Expected>> blocks = VerificationSet.ReadExpected();
		List<Row> rows = [];

		for (int i = 0; i < cases.Count; i++)
		{
			int catalogId = cases[i].Elements.NoradCatalogId;

			if (catalogId == HarnessArtifactCatalogId)
			{
				continue;
			}

			Sgp4Satellite<double> inDouble = Sgp4<double>.Initialize(cases[i].Elements, DoubleStorageMath.Instance);
			TSatellite satellite = initialize(cases[i].Elements);

			foreach (VerificationSet.Expected expected in blocks[i])
			{
				// Gate 1 decides which rows are the model's to answer; a row double refuses is not a
				// storage-type comparison.
				if (!Sgp4<double>.Propagate(inDouble, expected.Minutes, DoubleStorageMath.Instance).IsSuccess)
				{
					continue;
				}

				Sgp4Result<TState> result = propagate(satellite, expected.Minutes);
				TemeState<TState> state = result.State;

				double position = Distance(toDouble(state.X) - expected.X, toDouble(state.Y) - expected.Y, toDouble(state.Z) - expected.Z);
				double velocity = Distance(toDouble(state.VelocityX) - expected.VelocityX, toDouble(state.VelocityY) - expected.VelocityY, toDouble(state.VelocityZ) - expected.VelocityZ);

				rows.Add(new Row(catalogId, expected.Minutes, result.Error, position, velocity, System.Math.Abs(expected.Minutes) > LongArcMinutes));
			}
		}

		return rows;
	}

	private static double Distance(double x, double y, double z) => System.Math.Sqrt((x * x) + (y * y) + (z * z));

	/// <summary>One published row, propagated in the storage type under test.</summary>
	/// <param name="CatalogId">The catalogue number.</param>
	/// <param name="Minutes">Minutes since epoch.</param>
	/// <param name="Error">The error the storage type returned.</param>
	/// <param name="PositionKm">Distance from the published position, in km.</param>
	/// <param name="VelocityKmPerSecond">Distance from the published velocity, in km/s.</param>
	/// <param name="IsLongArc">Whether the row is on the long arc beyond the published spans.</param>
	private sealed record Row(int CatalogId, double Minutes, Sgp4Error Error, double PositionKm, double VelocityKmPerSecond, bool IsLongArc);
}
