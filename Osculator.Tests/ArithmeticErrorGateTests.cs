// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Gate 5: the checks on the harness that measures Δ_arith, rather than on Δ_arith itself.
/// </summary>
/// <remarks>
/// <para>
/// `StorageComparisonTests` reports that `double`'s arithmetic error is a median of 1.6e-10 km
/// against a thirty-digit `PreciseNumber` reference, and that is the repository's central claim.
/// It rests entirely on two things nothing else checks:
/// </para>
/// <para>
/// <strong>That the reference is converged.</strong> A thirty-digit run is not exact arithmetic,
/// it is thirty-digit arithmetic. If its own error were anywhere near 1.6e-10 km, the headline
/// number would be measuring the reference rather than <see langword="double"/>, and no amount of
/// care elsewhere would fix that.
/// </para>
/// <para>
/// <strong>That the harness holds everything but the storage type fixed.</strong> Every type is
/// initialized from the same <c>ElementSet</c>, whose fields are <see langword="double"/>, so the
/// inputs are identical by construction and the measured difference is arithmetic. Anything that
/// leaked a storage-type-specific path into the plumbing — a constant that was not generic, a
/// conversion through <see langword="double"/> — would show up as a run that does not reproduce
/// itself exactly.
/// </para>
/// <para>
/// The spec calls this "the trivial invariant". It is trivial to state and it is the load-bearing
/// one: every other number in the comparison is quoted against this reference.
/// </para>
/// </remarks>
[TestClass]
public sealed class ArithmeticErrorGateTests
{
	/// <summary>The precision the comparison harness uses as its reference.</summary>
	private const int ReferenceDigits = 30;

	/// <summary>
	/// A precision far enough above the reference to expose its convergence error, and not so far
	/// that the sweep costs more than the gate is worth. Ten digits is three hundred billion times
	/// the reference's last digit.
	/// </summary>
	private const int ProbeDigits = 40;

	private static readonly Lazy<Convergence> Measured = new(Measure);

	[TestMethod]
	public void TheReferenceIsConvergedFarBelowTheErrorItIsUsedToMeasure()
	{
		Convergence c = Measured.Value;

		Console.WriteLine(
			$"{ReferenceDigits} vs {ProbeDigits} digits: worst {c.WorstKilometers:E3} km over {c.Rows} rows");

		Assert.IsGreaterThan(600, c.Rows, "The sweep should cover the whole verification set.");

		// double's median against this reference is 1.6e-10 km. The reference's own error has to be
		// far below that or the headline number is measuring the wrong thing. Measured at 4.8e-21,
		// which is eleven orders below; the bound here is loose by nine of those, so this fails on
		// a real regression rather than on the last digit moving.
		// Non-zero first, and this is not pedantry: a harness whose precision argument was ignored —
		// so that the probe was secretly the reference — would report exactly zero here and sail
		// through the bound below. An exact zero means the probe is not probing, which is the one
		// way this gate could pass while proving nothing.
		Assert.IsGreaterThan(0.0, c.WorstKilometers,
			"Two runs at different precisions agreeing to the last bit means the precision argument "
			+ "is not reaching the arithmetic.");

		Assert.IsLessThan(1e-15, c.WorstKilometers,
			$"The thirty-digit reference disagrees with a forty-digit one by {c.WorstKilometers:E3} km, "
			+ "which is not negligible against the 1.6e-10 km it is used to measure.");
	}

	[TestMethod]
	public void TheHarnessReproducesItselfExactly()
	{
		// Δ_arith(PreciseNumber) ≡ 0, and exactly zero rather than nearly: two runs of the same
		// storage type at the same precision differ in nothing, so every digit has to agree. A
		// harness that had picked up a storage-type-specific path — a non-generic constant, a
		// conversion through double — would land near zero instead of on it, which is the failure
		// this is shaped to catch.
		//
		// Three cases rather than the whole set, because this claim does not get truer with more
		// rows and the sweep above already spends the full-set budget. One near-earth, one
		// deep-space with resonance, one with the eccentricity that stresses the periodics.
		List<VerificationSet.Case> cases = VerificationSet.ReadCases();
		List<IReadOnlyList<VerificationSet.Expected>> blocks = VerificationSet.ReadExpected();
		int compared = 0;

		for (int i = 0; i < cases.Count && compared < 3; i++)
		{
			PreciseStorageMath first = new(ReferenceDigits);
			PreciseStorageMath second = new(ReferenceDigits);
			Sgp4Satellite<PreciseNumber> a = Sgp4<PreciseNumber>.Initialize(cases[i].Elements, first);
			Sgp4Satellite<PreciseNumber> b = Sgp4<PreciseNumber>.Initialize(cases[i].Elements, second);
			bool usedThisCase = false;

			foreach (VerificationSet.Expected expected in blocks[i])
			{
				Sgp4Result<PreciseNumber> ra = Sgp4<PreciseNumber>.Propagate(a, expected.Minutes.ToPreciseNumber(), first);
				Sgp4Result<PreciseNumber> rb = Sgp4<PreciseNumber>.Propagate(b, expected.Minutes.ToPreciseNumber(), second);

				if (!ra.IsSuccess || !rb.IsSuccess)
				{
					continue;
				}

				Assert.AreEqual(ra.State.X, rb.State.X, $"x for {cases[i].Elements.NoradCatalogId} at {expected.Minutes} min");
				Assert.AreEqual(ra.State.Y, rb.State.Y, $"y for {cases[i].Elements.NoradCatalogId} at {expected.Minutes} min");
				Assert.AreEqual(ra.State.Z, rb.State.Z, $"z for {cases[i].Elements.NoradCatalogId} at {expected.Minutes} min");
				usedThisCase = true;
			}

			if (usedThisCase)
			{
				compared++;
			}
		}

		Assert.AreEqual(3, compared);
	}

	private static Convergence Measure()
	{
		List<VerificationSet.Case> cases = VerificationSet.ReadCases();
		List<IReadOnlyList<VerificationSet.Expected>> blocks = VerificationSet.ReadExpected();
		PreciseStorageMath reference = new(ReferenceDigits);
		PreciseStorageMath probe = new(ProbeDigits);
		double worst = 0.0;
		int rows = 0;

		for (int i = 0; i < cases.Count; i++)
		{
			Sgp4Satellite<PreciseNumber> atReference = Sgp4<PreciseNumber>.Initialize(cases[i].Elements, reference);
			Sgp4Satellite<PreciseNumber> atProbe = Sgp4<PreciseNumber>.Initialize(cases[i].Elements, probe);

			foreach (VerificationSet.Expected expected in blocks[i])
			{
				Sgp4Result<PreciseNumber> fromReference =
					Sgp4<PreciseNumber>.Propagate(atReference, expected.Minutes.ToPreciseNumber(), reference);
				Sgp4Result<PreciseNumber> fromProbe =
					Sgp4<PreciseNumber>.Propagate(atProbe, expected.Minutes.ToPreciseNumber(), probe);

				if (!fromReference.IsSuccess || !fromProbe.IsSuccess)
				{
					continue;
				}

				rows++;
				double dx = (fromReference.State.X - fromProbe.State.X).To<double>();
				double dy = (fromReference.State.Y - fromProbe.State.Y).To<double>();
				double dz = (fromReference.State.Z - fromProbe.State.Z).To<double>();

				worst = System.Math.Max(worst, System.Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)));
			}
		}

		return new Convergence(worst, rows);
	}

	private readonly record struct Convergence(double WorstKilometers, int Rows);
}
