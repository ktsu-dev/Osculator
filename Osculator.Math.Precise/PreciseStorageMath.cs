// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Math.Precise;

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
	public PreciseNumber Sqrt(PreciseNumber value) => PreciseNumber.Sqrt(value, SignificantDigits);

	/// <inheritdoc />
	public PreciseNumber Sin(PreciseNumber radians) => PreciseNumber.Sin(radians, SignificantDigits);

	/// <inheritdoc />
	public PreciseNumber Cos(PreciseNumber radians) => PreciseNumber.Cos(radians, SignificantDigits);

	/// <inheritdoc />
	public PreciseNumber Atan2(PreciseNumber y, PreciseNumber x) => PreciseNumber.Atan2(y, x, SignificantDigits);

	/// <inheritdoc />
	public PreciseNumber Pow(PreciseNumber value, PreciseNumber exponent) => PreciseNumber.Pow(value, exponent);
}
