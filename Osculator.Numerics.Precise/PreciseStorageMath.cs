// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Numerics.Precise;

using ktsu.Osculator.Core.Propagation;
using ktsu.PreciseNumber;

/// <summary>
/// <see cref="IStorageMath{T}"/> over <see cref="PreciseNumber"/>, at a chosen working precision.
/// </summary>
/// <remarks>
/// <para>
/// This is the reference arithmetic the other three storage types are measured against. It is not
/// exact — no finite computation of a sine is — but it is exact enough that its own error is far
/// below the difference it is being used to measure, which is the only property a reference needs.
/// </para>
/// <para>
/// The working precision is a constructor parameter rather than a constant because it is the
/// experiment's independent variable. Measuring the arithmetic error term means computing the same
/// propagation at several precisions and watching the answer stop moving; a fixed precision would
/// give one number with nothing to compare it against.
/// </para>
/// </remarks>
/// <param name="significantDigits">The working precision, in significant digits.</param>
public sealed class PreciseStorageMath(int significantDigits) : IStorageMath<PreciseNumber>
{
	/// <summary>
	/// Digits carried beyond <see cref="SignificantDigits"/> through the logarithm in
	/// <see cref="Pow"/>, so its rounding sits below the last digit reported.
	/// </summary>
	private const int PowGuardDigits = 10;

	private static readonly PreciseNumber Ten = 10.ToPreciseNumber();

	/// <summary>
	/// The working precision used when none is chosen, in significant digits.
	/// </summary>
	/// <remarks>
	/// Fifty digits is about thirty-four beyond <see langword="double"/>, so a disagreement between
	/// the two is attributable to <see langword="double"/> with the reference's own error some
	/// thirty orders of magnitude below it. It is also
	/// <see cref="PreciseNumber.MinimumDivisionPrecision"/>, so a result built from a division and a
	/// transcendental carries one precision rather than two.
	/// </remarks>
	public static int DefaultSignificantDigits => PreciseNumber.MinimumDivisionPrecision;

	/// <summary>Gets an instance at <see cref="DefaultSignificantDigits"/>.</summary>
	public static PreciseStorageMath Instance { get; } = new(DefaultSignificantDigits);

	/// <summary>Gets the working precision, in significant digits.</summary>
	public int SignificantDigits { get; } = significantDigits;

	/// <inheritdoc />
	/// <remarks>
	/// Returned at its full 150 digits rather than truncated to
	/// <see cref="SignificantDigits"/>. A constant carrying more precision than the computation
	/// around it costs nothing and removes one thing that could be limiting the result; a constant
	/// carrying less silently caps everything downstream of it, which is what argument reduction
	/// modulo two pi does to a large angle.
	/// </remarks>
	public PreciseNumber Pi => PreciseNumber.Pi;

	/// <inheritdoc />
	public PreciseNumber ToWorkingPrecision(PreciseNumber value) => value.ReduceSignificance(SignificantDigits);

	/// <inheritdoc />
	public PreciseNumber Sqrt(PreciseNumber value) => PreciseNumber.Sqrt(value, SignificantDigits);

	/// <inheritdoc />
	public PreciseNumber Sin(PreciseNumber radians) => PreciseNumber.Sin(radians, SignificantDigits);

	/// <inheritdoc />
	public PreciseNumber Cos(PreciseNumber radians) => PreciseNumber.Cos(radians, SignificantDigits);

	/// <inheritdoc />
	public PreciseNumber Atan2(PreciseNumber y, PreciseNumber x) => PreciseNumber.Atan2(y, x, SignificantDigits);

	/// <inheritdoc />
	/// <remarks>
	/// <para>
	/// An integer exponent goes to <see cref="PreciseNumber.Pow(PreciseNumber, PreciseNumber)"/>,
	/// which squares repeatedly and is exact. Anything else is <c>exp(y · ln x)</c> computed here at
	/// <see cref="SignificantDigits"/>, because the public two-argument <c>Pow</c> has no precision
	/// parameter and its fractional path runs at a fixed fifty digits. Delegating to it capped every
	/// fractional power in the propagator — <c>ao</c>, the semi-major axis, <c>coef1</c>,
	/// <c>am</c> — at fifty digits whatever precision was asked for, so a sweep above fifty
	/// converged on the cap rather than on the answer (#67).
	/// </para>
	/// <para>
	/// The logarithm is carried wider by the integer digits of <c>y · ln x</c>, because the
	/// exponential's range reduction cancels those digits and they have to have been there to
	/// cancel. That is the same construction PreciseNumber uses internally, with the precision
	/// exposed.
	/// </para>
	/// </remarks>
	public PreciseNumber Pow(PreciseNumber value, PreciseNumber exponent)
	{
		if (PreciseNumber.IsInteger(exponent) || value <= PreciseNumber.Zero)
		{
			// The integer path is exact, and a non-positive base has no real logarithm: both are
			// PreciseNumber's to answer, including its exceptions for the undefined cases.
			return PreciseNumber.Pow(value, exponent);
		}

		int working = SignificantDigits + PowGuardDigits;
		PreciseNumber product = exponent * PreciseNumber.Log(value, working);
		int consumed = IntegerDigits(product);

		if (consumed > 0)
		{
			product = exponent * PreciseNumber.Log(value, working + consumed);
		}

		return PreciseNumber.Exp(product, SignificantDigits + consumed).ReduceSignificance(SignificantDigits);
	}

	/// <inheritdoc />
	/// <remarks>
	/// The operator <c>/</c> on <see cref="PreciseNumber"/> rounds a non-terminating quotient to the
	/// wider operand's precision or <see cref="PreciseNumber.MinimumDivisionPrecision"/>, whichever is
	/// more. For two short operands — <c>2 / 3</c>, <c>78 / 6378.135</c> — that is fifty digits
	/// whatever precision was asked for, which put a floor of about 1e-50 under every run above
	/// fifty (#42). This rounds to <see cref="SignificantDigits"/> instead, and a terminating
	/// quotient is still exact.
	/// </remarks>
	public PreciseNumber Divide(PreciseNumber dividend, PreciseNumber divisor)
	{
		// Never narrower than the operator would have been, so nothing at or below fifty digits
		// moves: the operator's rule is the wider operand or fifty, and the working precision only
		// ever raises that.
		int digits = System.Math.Max(
			System.Math.Max(dividend.SignificantDigits, divisor.SignificantDigits),
			System.Math.Max(SignificantDigits, PreciseNumber.MinimumDivisionPrecision));

		return PreciseNumber.Divide(dividend, divisor, digits);
	}

	/// <summary>Counts the digits before the decimal point of a value's magnitude.</summary>
	/// <param name="value">The value.</param>
	/// <returns>Zero for a magnitude below one, otherwise the number of integer digits.</returns>
	private static int IntegerDigits(PreciseNumber value)
	{
		PreciseNumber magnitude = PreciseNumber.Abs(value);
		int digits = 0;

		while (magnitude >= PreciseNumber.One)
		{
			magnitude /= Ten;
			digits++;
		}

		return digits;
	}
}
