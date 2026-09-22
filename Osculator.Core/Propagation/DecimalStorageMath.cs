// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using ktsu.Osculator.Core.Numerics;

/// <summary>
/// <see cref="IStorageMath{T}"/> over <see langword="decimal"/>, computed by <see cref="DecimalMath"/>.
/// </summary>
/// <remarks>
/// <para>
/// The only one of the four that is not a delegation to somebody else's library. The base library
/// gives <see langword="decimal"/> no square root, no trigonometry and no power, so every function
/// below is written in this repository — which makes this the storage type whose arithmetic error
/// is partly of our own making, and the interesting one for that reason.
/// </para>
/// <para>
/// It is also the type with the narrowest range: nothing between zero and 1e-28 exists, and there
/// are no subnormals to degrade into, so a value that would merely lose precision in a binary float
/// becomes exactly zero here. Where SGP4 then divides by it, the propagation throws rather than
/// returning an infinity.
/// </para>
/// </remarks>
public sealed class DecimalStorageMath : IStorageMath<decimal>
{
	/// <summary>Gets the shared instance.</summary>
	public static DecimalStorageMath Instance { get; } = new();

	/// <inheritdoc />
	public decimal Pi => DecimalMath.Pi;

	/// <inheritdoc />
	/// <remarks>The identity: decimal rounds every result into its own width already.</remarks>
	public decimal ToWorkingPrecision(decimal value) => value;

	/// <inheritdoc />
	public decimal Sqrt(decimal value) => DecimalMath.Sqrt(value);

	/// <inheritdoc />
	public decimal Sin(decimal radians) => DecimalMath.Sin(radians);

	/// <inheritdoc />
	public decimal Cos(decimal radians) => DecimalMath.Cos(radians);

	/// <inheritdoc />
	public decimal Atan2(decimal y, decimal x) => DecimalMath.Atan2(y, x);

	/// <inheritdoc />
	public decimal Pow(decimal value, decimal exponent) => DecimalMath.Pow(value, exponent);
}
