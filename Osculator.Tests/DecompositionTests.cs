// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Residuals;
using ktsu.Osculator.Data.CelesTrak;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// The M5 gate: Δ_model, Δ_data and Δ_arith for one object and one horizon, together.
/// </summary>
/// <remarks>
/// <para>
/// Runs on the same three real ISS element sets <see cref="DivergenceTests"/> uses, read through
/// <see cref="SnapshotStore"/>, against the 30-digit reference the rest of the suite uses. The week's
/// pair is the one decomposed, because it is the horizon the spec's projections are quoted at.
/// </para>
/// <para>
/// Measured, 2026-09-15 21:14 → 2026-09-22 20:26 (6.97 days), 256 samples, seed 44:
/// </para>
/// <code>
///   Δ_model              10.915 km        (R +0.154  S −10.810  W +1.498)
///   Δ_data   rms          0.0098 km       (along-track 0.0092)
///   Δ_arith  float        0.020 km
///   Δ_arith  double       1.7e-9 km
///   Δ_arith  decimal      6.4e-7 km
///   Δ_arith  precise      0, every digit
/// </code>
/// </remarks>
[TestClass]
public sealed class DecompositionTests
{
	private const int Iss = 25544;

	private const int ReferenceDigits = 30;

	/// <summary>The week-long ISS decomposition with the default options, computed once for the class.</summary>
	/// <remarks>
	/// 256 perturbed propagations in thirty-digit arithmetic plus the four storage types; a few
	/// seconds, which is the one expensive thing here, so it is shared.
	/// </remarks>
	private static readonly Lazy<DecompositionResult<PreciseNumber>> Week = new(() =>
	{
		IReadOnlyList<ElementSet> history = IssHistory();
		return Decomposition<PreciseNumber>.Compute(history[1], history[2], new PreciseStorageMath(ReferenceDigits));
	});

	[TestMethod]
	public void TheReferenceAgainstItselfIsZeroToEveryDigit()
	{
		// Gate 5 in the form M5 states it: the reference arithmetic run through exactly the path every
		// other storage type takes, a second time, independently, and compared. Not small — zero, in
		// all six components. Anything else would mean the harness is not holding everything but the
		// storage type fixed, and every other term below would be measuring that instead.
		DecompositionResult<PreciseNumber> week = Week.Value;
		Assert.IsTrue(week.IsSuccess, week.Error.ToString());

		ArithmeticTerm<PreciseNumber> self = week.ReferenceAgainstItself;
		Assert.AreEqual(typeof(PreciseNumber), self.StorageType);
		Assert.IsTrue(self.IsExactlyZero, $"Δ_arith(PreciseNumber) should be exactly zero; read {Describe(self.Residual)}.");
	}

	[TestMethod]
	public void TheZeroIsNotVacuous()
	{
		// The guard gate 5 learned to need: a harness that never ran the other types, or quietly ran
		// the reference for all of them, would also report zero. Every fixed-width type must differ
		// from the reference, and must say which type it is.
		DecompositionResult<PreciseNumber> week = Week.Value;

		CollectionAssert.AreEqual(
			new[] { typeof(float), typeof(double), typeof(decimal), typeof(PreciseNumber) },
			ToTypes(week.Arithmetic));

		for (int i = 0; i < 3; i++)
		{
			ArithmeticTerm<PreciseNumber> term = week.Arithmetic[i];
			Assert.IsTrue(term.IsSuccess, $"{term.StorageType.Name}: {term.Error}");
			Assert.IsFalse(term.IsExactlyZero, $"Δ_arith({term.StorageType.Name}) read exactly zero, which means it was never measured.");
			Assert.IsGreaterThan(TimeSpan.Zero, term.PropagationTime);
		}
	}

	[TestMethod]
	public void TheModelTermIsTheM2Divergence()
	{
		// Δ_model goes through Divergence rather than beside it, so the decomposition and the M2 number
		// can never disagree. Pinned against the same figures DivergenceTests pins in double.
		DecompositionResult<PreciseNumber> week = Week.Value;
		IReadOnlyList<ElementSet> history = IssHistory();
		ElementSetDivergence<PreciseNumber> divergence = Divergence<PreciseNumber>.Between(history[1], history[2], new PreciseStorageMath(ReferenceDigits));

		Assert.AreEqual(divergence.Residual, week.Model);
		Assert.AreEqual(divergence.HorizonMinutes, week.HorizonMinutes);
		Assert.AreEqual(history[1].Epoch, week.PredictionEpoch);
		Assert.AreEqual(history[2].Epoch, week.TruthEpoch);

		Assert.AreEqual(6.9668, Km(week.HorizonMinutes) / 1440.0, 1e-4);
		Assert.AreEqual(0.1543, Km(week.Model.Radial), 1e-3);
		Assert.AreEqual(-10.8103, Km(week.Model.AlongTrack), 1e-3);
		Assert.AreEqual(1.4979, Km(week.Model.CrossTrack), 1e-3);
	}

	[TestMethod]
	public void TheThreeTermsSeparateByOrdersOfMagnitude()
	{
		// The headline result. On the ISS over a week:
		//
		//   Δ_model           10.9 km
		//   Δ_data            0.0098 km     a thousand times smaller
		//   Δ_arith(double)   1.7e-9 km     six and a half orders below that
		//
		// So of the eleven kilometres between this prediction and the next element set, essentially
		// all is the model and the fit; the digits on the page are worth ten metres; and double's
		// round-off is worth under two micrometres. More digits cannot buy back any of the first two.
		DecompositionResult<PreciseNumber> week = Week.Value;

		double model = Magnitude(week.Model);
		double data = Km(week.Data.RmsMagnitude);
		double arithDouble = Magnitude(week.Arithmetic[1].Residual);

		Console.WriteLine($"Δ_model {model:E4} km, Δ_data {data:E4} km, Δ_arith(double) {arithDouble:E4} km");

		Assert.AreEqual(0, week.Data.Refusals);
		Assert.AreEqual(DecompositionOptions.Default.Samples, week.Data.Samples);

		Assert.IsGreaterThan(data * 100.0, model, "Δ_model should dwarf Δ_data on a week-long ISS arc.");
		Assert.IsGreaterThan(arithDouble * 1e6, data, "Δ_arith(double) should sit at least six orders below Δ_data.");

		// Bands with an order of magnitude of headroom around what was measured: these are not targets.
		Assert.IsGreaterThan(1e-3, data);
		Assert.IsLessThan(1e-1, data);
		Assert.IsLessThan(1e-7, arithDouble);
		Assert.IsGreaterThan(1e-11, arithDouble);

		// And it is along-track, as every orbital error is.
		Assert.IsGreaterThan(Km(week.Data.RmsRadial), Km(week.Data.RmsAlongTrack));
		Assert.IsGreaterThan(Km(week.Data.RmsCrossTrack), Km(week.Data.RmsAlongTrack));
		Assert.IsLessThanOrEqualTo(Km(week.Data.MaxMagnitude), data);
	}

	[TestMethod]
	public void SinglePrecisionCostsMoreThanTheDataTerm()
	{
		// The other end of the table. float's arithmetic error on this arc is 0.020 km, twice the
		// whole data term: storing the propagation in seven digits throws away more than the element
		// set's last decimal place is worth. decimal sits between, at 6.4e-7 km, worse than double for
		// the reason domain trap 10 gives.
		DecompositionResult<PreciseNumber> week = Week.Value;

		double data = Km(week.Data.RmsMagnitude);
		double arithFloat = Magnitude(week.Arithmetic[0].Residual);
		double arithDouble = Magnitude(week.Arithmetic[1].Residual);
		double arithDecimal = Magnitude(week.Arithmetic[2].Residual);

		Console.WriteLine($"Δ_arith float {arithFloat:E4} km, double {arithDouble:E4} km, decimal {arithDecimal:E4} km, against Δ_data {data:E4} km");

		Assert.IsGreaterThan(data / 10.0, arithFloat, "float's round-off should be the same order as the data term.");
		Assert.IsLessThan(data * 10.0, arithFloat, "float's round-off should be the same order as the data term.");
		Assert.IsGreaterThan(arithDouble, arithDecimal, "decimal is expected to be further from the reference than double here.");
		Assert.IsLessThan(arithFloat, arithDecimal);
	}

	[TestMethod]
	public void TheMonteCarloAgreesWithTheFieldByFieldTerm()
	{
		// An independent check of the ensemble against DataTerm, which measures the same band a
		// different way: one field at a time, half a step, combined in quadrature. A value rounded to a
		// step is uniform within half a step of it, whose root-mean-square is the half step over √3, so
		// if the response is linear over so small a band — and it is — the Monte Carlo RMS should be
		// DataTerm's figure over √3 exactly, in expectation.
		//
		// DataTerm does not perturb the epoch; the ensemble does. Its contribution is measured here the
		// same way DataTerm measures the others and added in quadrature before comparing.
		//
		// Measured: 16384 samples read 9.866e-3 km against 9.843e-3 predicted, 0.2% apart. Run in double
		// because a converged ensemble is sixteen thousand propagations, and Δ_data is ten orders
		// above double's arithmetic, so the reference type cannot matter to this comparison.
		IReadOnlyList<ElementSet> history = IssHistory();
		DoubleStorageMath math = DoubleStorageMath.Instance;

		DecompositionResult<double> converged = Decomposition<double>.Compute(history[1], history[2], math, new DecompositionOptions(16384, 1));
		double minutes = converged.HorizonMinutes;

		double fieldByField = DataTerm.CombineInQuadrature(DataTerm.Measure(history[1], minutes, math));
		double epoch = Separation(
			Propagate(history[1], minutes, math),
			Propagate(history[1], minutes - (ElementFieldQuantization.EpochDays / 2.0 * 1440.0), math));
		double predicted = Math.Sqrt(((fieldByField * fieldByField) + (epoch * epoch)) / 3.0);

		Console.WriteLine($"Monte Carlo {converged.Data.RmsMagnitude:E4} km, field by field / √3 {predicted:E4} km");

		Assert.AreEqual(predicted, converged.Data.RmsMagnitude, predicted * 0.01);
	}

	[TestMethod]
	public void TheEnsembleReplaysFromItsSeed()
	{
		// A Monte Carlo figure that moved between two identical calls could be pinned by nothing.
		IReadOnlyList<ElementSet> history = IssHistory();
		DoubleStorageMath math = DoubleStorageMath.Instance;

		DecompositionResult<double> first = Decomposition<double>.Compute(history[0], history[1], math, new DecompositionOptions(32, 7));
		DecompositionResult<double> again = Decomposition<double>.Compute(history[0], history[1], math, new DecompositionOptions(32, 7));
		DecompositionResult<double> other = Decomposition<double>.Compute(history[0], history[1], math, new DecompositionOptions(32, 8));

		Assert.AreEqual(first.Data, again.Data);
		Assert.AreNotEqual(first.Data.RmsMagnitude, other.Data.RmsMagnitude);
	}

	[TestMethod]
	public void TheInvariantHoldsInAnyReference()
	{
		// The zero is a property of the harness, not of PreciseNumber: with double as the reference,
		// double against itself is zero too, and float and decimal are measured against it.
		IReadOnlyList<ElementSet> history = IssHistory();
		DecompositionResult<double> halfDay = Decomposition<double>.Compute(history[0], history[1], DoubleStorageMath.Instance, new DecompositionOptions(8));

		Assert.IsTrue(halfDay.IsSuccess, halfDay.Error.ToString());
		Assert.IsTrue(halfDay.ReferenceAgainstItself.IsExactlyZero);
		Assert.IsTrue(halfDay.Arithmetic[1].IsExactlyZero, "double against a double reference is the same computation twice.");
		Assert.IsFalse(halfDay.Arithmetic[0].IsExactlyZero);
	}

	[TestMethod]
	public void RefusesWhatItCannotDecompose()
	{
		IReadOnlyList<ElementSet> history = IssHistory();
		DoubleStorageMath math = DoubleStorageMath.Instance;

		Assert.ThrowsExactly<ArgumentException>(() => Decomposition<double>.Compute(history[0], history[1], math, new DecompositionOptions(0)));
		Assert.ThrowsExactly<ArgumentException>(() => Decomposition<double>.Compute(history[1], history[0], math));
		Assert.ThrowsExactly<ArgumentException>(() => Decomposition<double>.Compute(history[0], history[1] with { NoradCatalogId = 1 }, math));
		Assert.ThrowsExactly<ArgumentNullException>(() => Decomposition<double>.Compute(history[0], history[1], null!));
	}

	private static TemeState<double> Propagate(ElementSet elements, double minutes, DoubleStorageMath math)
	{
		Sgp4Result<double> result = Sgp4<double>.Propagate(Sgp4<double>.Initialize(elements, math), minutes, math);
		Assert.IsTrue(result.IsSuccess, result.Error.ToString());
		return result.State;
	}

	private static double Separation(TemeState<double> a, TemeState<double> b) =>
		Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)) + ((b.Z - a.Z) * (b.Z - a.Z)));

	private static Type[] ToTypes(IReadOnlyList<ArithmeticTerm<PreciseNumber>> terms)
	{
		Type[] types = new Type[terms.Count];

		for (int i = 0; i < terms.Count; i++)
		{
			types[i] = terms[i].StorageType;
		}

		return types;
	}

	private static double Km(PreciseNumber value) => value.To<double>();

	private static double Magnitude(RswResidual<PreciseNumber> residual) =>
		Math.Sqrt((Km(residual.Radial) * Km(residual.Radial)) + (Km(residual.AlongTrack) * Km(residual.AlongTrack)) + (Km(residual.CrossTrack) * Km(residual.CrossTrack)));

	private static string Describe(RswResidual<PreciseNumber> residual) =>
		$"R {residual.Radial} S {residual.AlongTrack} W {residual.CrossTrack} Ṙ {residual.RadialRate} Ṡ {residual.AlongTrackRate} Ẇ {residual.CrossTrackRate}";

	private static IReadOnlyList<ElementSet> IssHistory() =>
		new SnapshotStore(Path.Join(VerificationSet.DataDirectory, "iss-snapshots")).History(Iss);
}
