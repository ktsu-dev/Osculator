// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System.Numerics;

/// <summary>
/// The transcendental functions a propagator needs and <see cref="INumber{TSelf}"/> does not declare.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// This is the seam that lets one propagator run over four storage types. <see langword="double"/>,
/// <see langword="float"/> and <see langword="decimal"/> are served by <see cref="System.Math"/>;
/// <c>PreciseNumber</c> implements <c>INumber&lt;T&gt;</c> and nothing else, so it needs an
/// implementation of its own — see
/// <see href="https://github.com/ktsu-dev/PreciseNumber/issues/78">ktsu-dev/PreciseNumber#78</see>.
/// </para>
/// <para>
/// Taking the functions as an interface rather than constraining on
/// <c>ITrigonometricFunctions&lt;T&gt;</c> is what makes that possible: a type that cannot satisfy
/// the constraint can still be handed an implementation.
/// </para>
/// <para>
/// It also carries <see cref="ToWorkingPrecision"/>, which is not a function at all but the answer
/// to a problem only an arbitrary-precision type has. See that member.
/// </para>
/// </remarks>
public interface IStorageMath<T>
	where T : struct, INumber<T>
{
	/// <summary>Gets the ratio of a circle's circumference to its diameter.</summary>
	public T Pi { get; }

	/// <summary>
	/// Discards any precision beyond what the storage type is being worked to.
	/// </summary>
	/// <param name="value">The value.</param>
	/// <returns>
	/// The value at the working precision — which for every fixed-width type is the value itself.
	/// </returns>
	/// <remarks>
	/// <para>
	/// A fixed-width type rounds every product back into its own width, so this is the identity for
	/// <see langword="float"/>, <see langword="double"/> and <see langword="decimal"/>. An
	/// arbitrary-precision type does not: its multiplication is <em>exact</em>, so the product of two
	/// fifty-digit values carries a hundred digits of which fifty are meaningful, and the next
	/// product carries a hundred and fifty. Nothing in that sequence is wrong, and all of it is
	/// waste.
	/// </para>
	/// <para>
	/// Measured before this member existed: one <c>Initialize</c> over <c>PreciseNumber</c> at a
	/// twenty-digit working precision took 18.8 seconds and left <c>T5cof</c> holding 167,104
	/// significant digits. Division was never the problem — a quotient is truncated to the requested
	/// precision on the way out, which is why the coefficients ending in one were still short. It is
	/// multiplication, and the growth is linear in the length of the chain, so it compounds through
	/// coefficients defined in terms of each other.
	/// </para>
	/// <para>
	/// It is called where a value is stored or accumulated rather than after every operation, which
	/// bounds the growth to the length of a single expression while keeping the propagator's
	/// arithmetic written the way the published equations are. It is on this interface rather than
	/// applied inside a wrapper numeric type so that the fixed-width paths pay one inlined identity
	/// call rather than a wrapper's worth of indirection on every operator.
	/// </para>
	/// </remarks>
	public T ToWorkingPrecision(T value);

	/// <summary>Computes the square root of a non-negative value.</summary>
	/// <param name="value">The value.</param>
	/// <returns>The square root.</returns>
	public T Sqrt(T value);

	/// <summary>Computes the sine of an angle in radians.</summary>
	/// <param name="radians">The angle.</param>
	/// <returns>The sine.</returns>
	public T Sin(T radians);

	/// <summary>Computes the cosine of an angle in radians.</summary>
	/// <param name="radians">The angle.</param>
	/// <returns>The cosine.</returns>
	public T Cos(T radians);

	/// <summary>Computes the angle whose tangent is the quotient of two values.</summary>
	/// <param name="y">The ordinate.</param>
	/// <param name="x">The abscissa.</param>
	/// <returns>The angle in radians, in the range -pi to pi.</returns>
	public T Atan2(T y, T x);

	/// <summary>Raises a value to a power.</summary>
	/// <param name="value">The base.</param>
	/// <param name="exponent">The exponent.</param>
	/// <returns>The result.</returns>
	public T Pow(T value, T exponent);
}
