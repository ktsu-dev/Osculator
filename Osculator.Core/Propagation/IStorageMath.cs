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
/// </remarks>
public interface IStorageMath<T>
	where T : struct, INumber<T>
{
	/// <summary>Gets the ratio of a circle's circumference to its diameter.</summary>
	public T Pi { get; }

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
