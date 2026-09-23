// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;

using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Storage;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Runs the whole verification set in every storage type and measures what changes.
/// </summary>
/// <remarks>
/// <para>
/// This is validation gate 2, and it is the first thing in the repository that measures the
/// arithmetic error term rather than projecting it. Everything asserted here is a figure this suite
/// computed; the numbers quoted in the comments are what it measured, and they should be updated
/// when they move rather than left as decoration.
/// </para>
/// <para>
/// All four storage types run here. <see langword="decimal"/> is the one whose transcendental
/// functions this repository had to write, so the error it shows is partly its own arithmetic and
/// partly ours — which is stated rather than hidden, because it is the interesting half.
/// </para>
/// </remarks>
[TestClass]
public sealed class StorageComparisonTests
{
	/// <summary>The working precision of the reference arithmetic, in significant digits.</summary>
	/// <remarks>
	/// Fourteen digits beyond <see langword="double"/>, which is enough that the reference's own
	/// round-off is invisible in every difference measured against it.
	/// </remarks>
	private const int ReferenceDigits = 30;

	/// <summary>Arcs longer than this, in minutes, fall outside the published verification spans.</summary>
	private const double LongArcMinutes = 10000.0;

	/// <summary>What one run over the whole set measured.</summary>
	/// <param name="Rows">Rows compared.</param>
	/// <param name="FloatRefusals">Rows where the single-precision run reported an error.</param>
	/// <param name="WorstFloat">Worst single-precision position error against the published vectors, in km.</param>
	/// <param name="WorstFloatCatalogId">The object that worst error was on.</param>
	/// <param name="WorstDouble">Worst double position error against the published vectors, in km, over the published arcs.</param>
	/// <param name="WorstPrecise">Worst reference position error against the published vectors, in km, over the published arcs.</param>
	/// <param name="WorstDoubleVsPrecise">Worst distance between the double and reference states, in km, over the published arcs.</param>
	/// <param name="MedianDoubleVsPrecise">Median of that distance over every row, in km.</param>
	/// <param name="WorstDecimal">Worst decimal position error against the published vectors, in km, over the published arcs.</param>
	/// <param name="WorstDecimalVsPrecise">Worst distance between the decimal and reference states, in km, over the published arcs.</param>
	/// <param name="MedianDecimalVsPrecise">Median of that distance over every row, in km.</param>
	private sealed record Measurement(
		int Rows,
		int FloatRefusals,
		double WorstFloat,
		int WorstFloatCatalogId,
		double WorstDouble,
		double WorstPrecise,
		double WorstDoubleVsPrecise,
		double MedianDoubleVsPrecise,
		double WorstDecimal,
		double WorstDecimalVsPrecise,
		double MedianDecimalVsPrecise);

	/// <summary>The measurement, computed once for the whole class.</summary>
	/// <remarks>
	/// The reference arithmetic is some four orders of magnitude slower than <see langword="double"/>,
	/// so running the set once and asserting three things about it costs a third of running it three
	/// times. It is still only about five seconds.
	/// </remarks>
	private static readonly System.Lazy<Measurement> Measured = new(Measure);

	[TestMethod]
	public void SingleWidthCannotCarryTheModel_AndDoesNotSaySo()
	{
		Measurement m = Measured.Value;

		Console.WriteLine($"float: worst {m.WorstFloat:E3} km on object {m.WorstFloatCatalogId}, refusals {m.FloatRefusals} of {m.Rows}");

		// Seven significant digits cannot express a metre at geostationary radius, so this was never
		// going to pass, and recording where it lands is the result rather than the failure.
		Assert.IsGreaterThan(1.0, m.WorstFloat, "Single precision is expected to be kilometres out; if it is not, the measurement is not running.");

		// The part worth pinning is the second half of the sentence. Every row still came back as a
		// successful propagation: the model has error codes for an eccentricity or a mean motion that
		// has left the range it is defined over, and single precision never trips one. It returns a
		// state that is tens of kilometres wrong and reports success, which is the failure mode that
		// costs someone a conjunction assessment rather than a stack trace.
		Assert.AreEqual(0, m.FloatRefusals, "Single precision is expected to fail silently; a refusal here would be new behaviour worth reading.");
	}

	[TestMethod]
	public void TheArithmeticErrorOfDoubleIsOrdersBelowTheDataTerm()
	{
		Measurement m = Measured.Value;

		Console.WriteLine($"double against a {ReferenceDigits}-digit reference: median {m.MedianDoubleVsPrecise:E3} km, worst {m.WorstDoubleVsPrecise:E3} km over {m.Rows} rows");

		// Measured: median 1.6e-10 km — a sixth of a millimetre — and 7.0e-8 km at worst over the
		// published arcs. The element set's own quantization puts 0.3 to 3 km on the same
		// propagations. So the arithmetic term is around ten orders of magnitude below the data term,
		// which is the claim this whole repository exists to check, and it checks out.
		//
		// The bounds below carry roughly an order of magnitude of headroom for the transcendental
		// libraries of other platforms. They are not targets: if they start being approached, that is
		// a regression to investigate rather than a number to raise.
		Assert.IsLessThan(1e-6, m.WorstDoubleVsPrecise);
		Assert.IsLessThan(1e-8, m.MedianDoubleVsPrecise);

		// And the statement in the form that matters: against the smallest figure anyone quotes for
		// the data term, the arithmetic term is smaller by at least six orders of magnitude.
		const double SmallestDataTermKm = 0.3;
		Assert.IsLessThan(SmallestDataTermKm / 1e6, m.WorstDoubleVsPrecise);
	}

	[TestMethod]
	public void TwelveExtraDigitsBuyAFactorOfThree_AndCostAFactorOfFive()
	{
		Measurement m = Measured.Value;

		Console.WriteLine($"decimal vs the {ReferenceDigits}-digit reference: median {m.MedianDecimalVsPrecise:E3} km, worst {m.WorstDecimalVsPrecise:E3} km");
		Console.WriteLine($"double  vs the {ReferenceDigits}-digit reference: median {m.MedianDoubleVsPrecise:E3} km, worst {m.WorstDoubleVsPrecise:E3} km");

		// decimal carries twenty-eight significant digits against double's sixteen. If the arithmetic
		// error scaled with the digit count, it would be better by twelve orders of magnitude.
		//
		// Measured: better by a factor of 2.8 at the median (5.9e-11 km against 1.6e-10) and worse by
		// a factor of 5.6 at the extreme (4.0e-7 km against 7.0e-8). Twelve digits bought under half
		// an order of magnitude, and lost most of one.
		//
		// TheExtraDigitsAreNotWhereTheyAreNeeded, below, is why.
		Assert.IsLessThan(m.MedianDoubleVsPrecise, m.MedianDecimalVsPrecise, "decimal is expected to be typically closer to the reference than double.");
		Assert.IsGreaterThan(m.WorstDoubleVsPrecise, m.WorstDecimalVsPrecise, "decimal is expected to be further from the reference than double at its worst.");

		// The claim that matters, in a form noise cannot flip: the gain is nothing like the twelve
		// orders of magnitude the digit counts would predict, or even three.
		Assert.IsGreaterThan(m.MedianDoubleVsPrecise / 1000.0, m.MedianDecimalVsPrecise);
	}

	[TestMethod]
	public void TheExtraDigitsAreNotWhereTheyAreNeeded()
	{
		// decimal's precision is *absolute* and double's is *relative*, and that single difference
		// explains the row above. decimal is a 96-bit integer with a scale capped at 28 decimal
		// places, so the smaller a value is, the fewer significant digits it has room for; double
		// carries the same sixteen everywhere until it reaches its subnormals.
		//
		// Measured with StorageProbe, which finds the smallest increment each type can still tell
		// apart, in that type's own arithmetic:
		//
		//     magnitude    decimal    double
		//     1e4             28.3      16.0
		//     1e0             28.0      15.7
		//     1e-4            24.0      16.0
		//     1e-9            19.0      16.0
		//     1e-12           16.0      16.0   <- they cross here
		//     1e-16           12.0      16.0
		//     1e-20            8.0      16.0
		//
		// SGP4's drag expansion is exactly where that hurts. Cc1 runs around 1e-12 for a typical
		// object and D2, D3, D4, T4cof and T5cof are powers of it, so the coefficients that decide
		// how an orbit decays sit at and below the crossover — the one place decimal has fewer
		// digits than the type it is supposed to be improving on.
		Assert.IsGreaterThan(SignificantDigits(1.0), SignificantDigits(1m), "At unit magnitude decimal should carry far more digits than double.");
		Assert.IsLessThan(SignificantDigits(1e-20), SignificantDigits(1e-20m), "At 1e-20 decimal should carry far fewer digits than double.");

		// And they meet in the middle, at the magnitude the drag coefficients live at.
		Assert.AreEqual(SignificantDigits(1e-12), SignificantDigits(1e-12m), 0.5, "The crossover is expected at 1e-12.");
	}

	[TestMethod]
	public void MorePrecisionDoesNotBringTheAnswerCloserToAReferenceComputedInDouble()
	{
		Measurement m = Measured.Value;

		Console.WriteLine($"against the published vectors: double {m.WorstDouble:E3} km, precise({ReferenceDigits}) {m.WorstPrecise:E3} km");

		// The sharpest form of the thesis, and the one that surprises people.
		//
		// If arithmetic precision were what limited agreement with the published vectors, a run at
		// thirty significant digits would agree with them to about 1e-30 km. It does not. Measured,
		// it agrees to 7.3e-8 km — the same order as double's own 8.1e-9, and in fact nine times
		// further away.
		//
		// Nothing is wrong with the precise run; it is the more correct of the two. The published
		// vectors were themselves computed in double, so agreeing with them closely is a property of
		// making the same rounding errors, not of being right. Adding digits removes those errors and
		// therefore moves the answer away from the reference. That is what it means for a comparison
		// to be limited by something other than arithmetic.
		Assert.IsGreaterThan(1e-12, m.WorstPrecise, "A thirty-digit run agreeing with the published vectors to better than 1e-12 km would mean the vectors are not double-limited, which would overturn the reasoning above.");
	}

	private static Measurement Measure()
	{
		List<VerificationSet.Case> cases = VerificationSet.ReadCases();
		List<IReadOnlyList<VerificationSet.Expected>> blocks = VerificationSet.ReadExpected();
		PreciseStorageMath precise = new(ReferenceDigits);

		double worstFloat = 0.0;
		double worstDouble = 0.0;
		double worstPrecise = 0.0;
		double worstDoubleVsPrecise = 0.0;
		double worstDecimal = 0.0;
		double worstDecimalVsPrecise = 0.0;
		List<double> differences = [];
		List<double> decimalDifferences = [];
		int worstFloatCatalogId = 0;
		int floatRefusals = 0;
		int rows = 0;

		for (int i = 0; i < cases.Count; i++)
		{
			Sgp4Satellite<double> inDouble = Sgp4<double>.Initialize(cases[i].Elements, DoubleStorageMath.Instance);
			Sgp4Satellite<float> inFloat = Sgp4<float>.Initialize(cases[i].Elements, FloatStorageMath.Instance);
			Sgp4Satellite<decimal> inDecimal = Sgp4<decimal>.Initialize(cases[i].Elements, DecimalStorageMath.Instance);
			Sgp4Satellite<PreciseNumber> inPrecise = Sgp4<PreciseNumber>.Initialize(cases[i].Elements, precise);

			foreach (VerificationSet.Expected expected in blocks[i])
			{
				Sgp4Result<double> fromDouble = Sgp4<double>.Propagate(inDouble, expected.Minutes, DoubleStorageMath.Instance);

				// A row the model itself refuses in double is not a storage-type comparison.
				if (!fromDouble.IsSuccess && fromDouble.Error != Sgp4Error.Decayed)
				{
					continue;
				}

				Sgp4Result<float> fromFloat = Sgp4<float>.Propagate(inFloat, (float)expected.Minutes, FloatStorageMath.Instance);
				Sgp4Result<decimal> fromDecimal = Sgp4<decimal>.Propagate(inDecimal, (decimal)expected.Minutes, DecimalStorageMath.Instance);
				Sgp4Result<PreciseNumber> fromPrecise = Sgp4<PreciseNumber>.Propagate(inPrecise, expected.Minutes.ToPreciseNumber(), precise);

				rows++;

				double px = fromPrecise.State.X.To<double>();
				double py = fromPrecise.State.Y.To<double>();
				double pz = fromPrecise.State.Z.To<double>();
				double difference = Distance(fromDouble.State.X - px, fromDouble.State.Y - py, fromDouble.State.Z - pz);
				differences.Add(difference);

				double mx = (double)fromDecimal.State.X;
				double my = (double)fromDecimal.State.Y;
				double mz = (double)fromDecimal.State.Z;
				double decimalDifference = Distance(mx - px, my - py, mz - pz);
				decimalDifferences.Add(decimalDifference);

				// The long arc is held apart because it is the one place the round-off floor is
				// visible at all, and mixing it in would hide what the published arcs measure.
				if (System.Math.Abs(expected.Minutes) <= LongArcMinutes)
				{
					worstDouble = System.Math.Max(worstDouble, Distance(fromDouble.State.X - expected.X, fromDouble.State.Y - expected.Y, fromDouble.State.Z - expected.Z));
					worstPrecise = System.Math.Max(worstPrecise, Distance(px - expected.X, py - expected.Y, pz - expected.Z));
					worstDoubleVsPrecise = System.Math.Max(worstDoubleVsPrecise, difference);
					worstDecimal = System.Math.Max(worstDecimal, Distance(mx - expected.X, my - expected.Y, mz - expected.Z));
					worstDecimalVsPrecise = System.Math.Max(worstDecimalVsPrecise, decimalDifference);
				}

				if (!fromFloat.IsSuccess && fromFloat.Error != Sgp4Error.Decayed)
				{
					floatRefusals++;
					continue;
				}

				double floatError = Distance(fromFloat.State.X - expected.X, fromFloat.State.Y - expected.Y, fromFloat.State.Z - expected.Z);

				if (floatError > worstFloat)
				{
					worstFloat = floatError;
					worstFloatCatalogId = cases[i].Elements.NoradCatalogId;
				}
			}
		}

		differences.Sort();
		decimalDifferences.Sort();

		return new Measurement(
			rows,
			floatRefusals,
			worstFloat,
			worstFloatCatalogId,
			worstDouble,
			worstPrecise,
			worstDoubleVsPrecise,
			differences[differences.Count / 2],
			worstDecimal,
			worstDecimalVsPrecise,
			decimalDifferences[decimalDifferences.Count / 2]);
	}

	private static double Distance(double x, double y, double z) => System.Math.Sqrt((x * x) + (y * y) + (z * z));

	/// <summary>How many significant decimal digits a type has room for at a given magnitude.</summary>
	/// <typeparam name="T">The storage type.</typeparam>
	/// <param name="magnitude">The magnitude to probe at.</param>
	/// <returns>The base-ten logarithm of the magnitude over the smallest step that still changes it.</returns>
	private static double SignificantDigits<T>(T magnitude)
		where T : struct, System.Numerics.INumber<T> =>
		System.Math.Log10(double.CreateTruncating(magnitude / StorageProbe.SmallestDistinguishableStep(magnitude)));
}
