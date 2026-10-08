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
/// Demonstration 4: round-off accumulated over a thirty-day numerically integrated arc, measured in
/// every storage type against a thirty-digit run of the same integration.
/// </summary>
/// <remarks>
/// <para>
/// Everything but the storage type is held fixed, which takes more care here than for SGP4. Three
/// things would otherwise leak into the difference:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>The step sequence.</b> An adaptive controller in two storage types chooses two sequences of
/// steps, and the difference between the trajectories would then include truncation error. The arc
/// is integrated in fixed 120-second steps, about what the controller chooses at the default
/// tolerance for this orbit (it measured 107 to 128 s).
/// </description></item>
/// <item><description>
/// <b>The inputs.</b> μ is 398600.4375 rather than EGM96's 398600.4418, because the published
/// value has no exact <see langword="float"/> and the float run would otherwise integrate a
/// different orbit — an input error of 8e-9 relative, which moves the along-track position by
/// hundreds of metres over a month and would be blamed on the arithmetic. 398600.4375 is
/// 398600 + 7/16, exact in all four types. The initial state is chosen the same way.
/// </description></item>
/// <item><description>
/// <b>The force model's time argument.</b> Two-body gravity ignores it, so integrating the arc in
/// segments, to read off checkpoints, cannot change the answer.
/// </description></item>
/// </list>
/// </remarks>
[TestClass]
public sealed class CowellRoundOffTests
{
	/// <summary>The working precision of the reference, in significant digits.</summary>
	private const int ReferenceDigits = 30;

	/// <summary>The precision the reference is checked against for convergence.</summary>
	private const int ConvergenceDigits = 40;

	/// <summary>The fixed step, in seconds.</summary>
	private const int StepSeconds = 120;

	/// <summary>Steps per day at that step.</summary>
	private const int StepsPerDay = 86400 / StepSeconds;

	/// <summary>The checkpoints, in days. The last is the arc's length.</summary>
	private static readonly int[] CheckpointDays = [1, 3, 10, 30];

	/// <summary>The measurement, computed once for the class; the reference run is the cost.</summary>
	private static readonly Lazy<Measurement> Measured = new(Measure);

	/// <summary>What the thirty-day arc measured.</summary>
	/// <param name="Float">Distance from the reference at each checkpoint, in km.</param>
	/// <param name="Double">Distance from the reference at each checkpoint, in km.</param>
	/// <param name="Decimal">Distance from the reference at each checkpoint, in km.</param>
	/// <param name="ReferenceConvergence">Distance between 30- and 40-digit runs after one day, in km.</param>
	private sealed record Measurement(double[] Float, double[] Double, double[] Decimal, double ReferenceConvergence);

	[TestMethod]
	public void DoubleAccumulatesRoundOff_ThatIsReal_AndSmall()
	{
		Measurement m = Measured.Value;
		Print(m);

		double[] d = m.Double;

		// Measured: 3.3e-9 km after one day and 2.4e-6 km after thirty — 2.4 mm. The spec projected
		// "about 2.5 mm" for this arc, and that figure survives measurement. How it gets there does
		// not; see RoundOffGrows_AsTheSquareOfTheArc_NotItsRoot.
		Assert.IsGreaterThan(1e-7, d[^1], "A month of double round-off was measured at 2.4 mm; far less would mean the arithmetic stopped reaching the state.");
		Assert.IsLessThan(1e-5, d[^1], "A month of double round-off was measured at 2.4 mm; a centimetre would be a regression.");

		// Small: against the 0.06 km Δ_data this repository measured for element-set quantization,
		// a month of integrated double round-off is still four orders of magnitude down.
		Assert.IsLessThan(0.056 / 1e4, d[^1]);
	}

	[TestMethod]
	public void RoundOffGrows_AsTheSquareOfTheArc_NotItsRoot()
	{
		Measurement m = Measured.Value;

		// The thing an analytic propagator cannot show: SGP4 evaluates each instant from scratch, so
		// its round-off does not depend on how far ahead it looks. An integrator reaches day thirty
		// through every step before it, and each rounds.
		//
		// The spec projected that the error would random-walk, growing as √N · ε · r and so as √t.
		// Measured, double grows as t^1.94 and float as t^1.96. A rounding error in a position
		// or a velocity is also an error in the orbit's energy, and so in its period: the satellite
		// runs early or late at a rate that is itself accumulating, and the along-track error that
		// results grows as the square of the arc. The spec's thirty-day figure happened to land on
		// the right millimetre; its one-day figure, read back along a square root, would be out by a factor of 138.
		Assert.IsGreaterThan(1.5, Exponent(m.Double), "double round-off was measured growing as t^1.94.");
		Assert.IsGreaterThan(1.5, Exponent(m.Float), "float round-off was measured growing as t^1.96.");
		Assert.IsLessThan(2.5, Exponent(m.Double));
	}

	[TestMethod]
	public void FloatAccumulates_ModelSizedError()
	{
		Measurement m = Measured.Value;

		// Spec §1, demonstration 4, projected that float would accumulate about 1.3 km over a month:
		// the size of SGP4's model error, and confusing for that reason. Measured, it is worse than
		// confusing. Ten kilometres after one day — already model-sized — and 8160 km after thirty,
		// which is the satellite most of the way to the other side of its orbit. It does not fail;
		// it returns a state on a plausible orbit at the wrong place, as float SGP4 does.
		Assert.IsGreaterThan(1.0, m.Float[0], "float was measured 10 km off after one day.");
		Assert.IsGreaterThan(1000.0, m.Float[^1], "float was measured 8160 km off after thirty days.");
	}

	[TestMethod]
	public void TheReference_IsConverged()
	{
		Measurement m = Measured.Value;

		// The reference is thirty-digit arithmetic, not exact arithmetic. Its own error has to be far
		// below the smallest difference it is used to measure, or the table measures the reference.
		Assert.IsGreaterThan(0.0, m.ReferenceConvergence, "Thirty and forty digits agreeing exactly would mean the precision never reached the arithmetic.");
		// Measured: 6.0e-23 km, fourteen orders below double after one day and four below
		// decimal, the smallest figure in the table.
		Assert.IsLessThan(m.Double[0] * 1e-6, m.ReferenceConvergence, "The reference's own round-off should be at least six orders below double's.");
		Assert.IsLessThan(m.Decimal[0] / 1e3, m.ReferenceConvergence, "The reference's own round-off should be at least three orders below decimal's.");
	}

	[TestMethod]
	public void DecimalCarriesItsDigits_ThroughTheWholeArc()
	{
		Measurement m = Measured.Value;

		// Unlike SGP4, where decimal's absolute precision runs out on the drag coefficients (domain
		// trap 10), every quantity in a two-body integration is between about 1e-3 and 1e4. That is
		// where decimal has all twenty-eight digits, and it shows: 2.4e-17 km after a month, eleven
		// orders below double.
		Assert.IsLessThan(m.Double[^1] / 1e9, m.Decimal[^1]);
	}

	private static void Print(Measurement m)
	{
		Console.WriteLine($"Two-body LEO, {StepSeconds} s fixed steps, distance from the {ReferenceDigits}-digit reference (km):");
		Console.WriteLine("  day       float          double         decimal");
		for (int i = 0; i < CheckpointDays.Length; i++)
		{
			Console.WriteLine($"  {CheckpointDays[i],3}   {m.Float[i],12:E3}   {m.Double[i],12:E3}   {m.Decimal[i],12:E3}");
		}

		Console.WriteLine($"reference convergence ({ReferenceDigits} vs {ConvergenceDigits} digits, day 1): {m.ReferenceConvergence:E3} km");
		Console.WriteLine($"growth exponent day 1 -> 30: float {Exponent(m.Float):F2}, double {Exponent(m.Double):F2}, decimal {Exponent(m.Decimal):F2}");
	}

	private static double Exponent(double[] distances) =>
		Math.Log(distances[^1] / distances[0]) / Math.Log((double)CheckpointDays[^1] / CheckpointDays[0]);

	private static Measurement Measure()
	{
		PreciseStorageMath reference = new(ReferenceDigits);
		CartesianState<PreciseNumber>[] truth = Run(reference);
		CartesianState<float>[] single = Run(FloatStorageMath.Instance);
		CartesianState<double>[] twice = Run(DoubleStorageMath.Instance);
		CartesianState<decimal>[] dec = Run(DecimalStorageMath.Instance);

		CartesianState<PreciseNumber> oneDay = Arc(new PreciseStorageMath(ConvergenceDigits), StepsPerDay);

		return new Measurement(
			Distances(truth, single),
			Distances(truth, twice),
			Distances(truth, dec),
			Distance(truth[0], oneDay));
	}

	private static CartesianState<T>[] Run<T>(IStorageMath<T> math)
		where T : struct, INumber<T>
	{
		Cowell<T> cowell = new(new TwoBody<T>(Parse<T>("398600.4375"), math), math);
		T step = T.CreateChecked(StepSeconds);
		CartesianState<T> state = Start<T>();
		CartesianState<T>[] checkpoints = new CartesianState<T>[CheckpointDays.Length];

		int previousDay = 0;
		for (int i = 0; i < CheckpointDays.Length; i++)
		{
			state = cowell.PropagateFixedStep(state, step, (CheckpointDays[i] - previousDay) * StepsPerDay).State;
			checkpoints[i] = state;
			previousDay = CheckpointDays[i];
		}

		return checkpoints;
	}

	private static CartesianState<T> Arc<T>(IStorageMath<T> math, int steps)
		where T : struct, INumber<T>
	{
		Cowell<T> cowell = new(new TwoBody<T>(Parse<T>("398600.4375"), math), math);
		return cowell.PropagateFixedStep(Start<T>(), T.CreateChecked(StepSeconds), steps).State;
	}

	/// <summary>
	/// A low Earth orbit at 7000 km, inclined about 52°, every component exact in all four types.
	/// </summary>
	private static CartesianState<T> Start<T>()
		where T : struct, INumber<T> =>
		new(Parse<T>("7000"), T.Zero, T.Zero, T.Zero, Parse<T>("4.6875"), Parse<T>("5.90625"));

	private static T Parse<T>(string literal)
		where T : struct, INumber<T> =>
		T.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture);

	private static double[] Distances<T>(CartesianState<PreciseNumber>[] truth, CartesianState<T>[] other)
		where T : struct, INumber<T>
	{
		double[] result = new double[truth.Length];
		for (int i = 0; i < truth.Length; i++)
		{
			result[i] = Distance(truth[i], other[i]);
		}

		return result;
	}

	/// <summary>The distance between two positions, differenced in thirty digits and reported in km.</summary>
	private static double Distance<T>(CartesianState<PreciseNumber> truth, CartesianState<T> other)
		where T : struct, INumber<T>
	{
		double dx = double.CreateChecked(truth.X - Convert<T, PreciseNumber>(other.X));
		double dy = double.CreateChecked(truth.Y - Convert<T, PreciseNumber>(other.Y));
		double dz = double.CreateChecked(truth.Z - Convert<T, PreciseNumber>(other.Z));
		return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
	}

	private static TTo Convert<TFrom, TTo>(TFrom value)
		where TFrom : struct, INumber<TFrom>
		where TTo : struct, INumber<TTo> =>
		TTo.CreateChecked(value);
}
