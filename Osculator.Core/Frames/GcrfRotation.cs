// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Frames;

using System.Numerics;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// A 3×3 rotation matrix in the storage type, as the reduction between TEME and the GCRF composes it.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// Frame rotations in Vallado's convention: <c>R3(a)</c> turns the axes, not the vector, by
/// <c>a</c> about z, so <c>R3(a)·(1, 0, 0) = (cos a, −sin a, 0)</c>. The inverse of every one of
/// these is its transpose, which is the whole of <see cref="ApplyTranspose"/>.
/// </remarks>
internal readonly record struct GcrfRotation<T>(T M11, T M12, T M13, T M21, T M22, T M23, T M31, T M32, T M33)
	where T : struct, INumber<T>
{
	/// <summary>The rotation of the axes about x, <c>R1(a)</c>.</summary>
	/// <param name="radians">The angle, in radians.</param>
	/// <param name="math">The transcendental functions.</param>
	/// <returns>The matrix.</returns>
	public static GcrfRotation<T> AboutX(T radians, IStorageMath<T> math)
	{
		T c = math.Cos(radians);
		T s = math.Sin(radians);
		return new(T.One, T.Zero, T.Zero, T.Zero, c, s, T.Zero, -s, c);
	}

	/// <summary>The rotation of the axes about y, <c>R2(a)</c>.</summary>
	/// <param name="radians">The angle, in radians.</param>
	/// <param name="math">The transcendental functions.</param>
	/// <returns>The matrix.</returns>
	public static GcrfRotation<T> AboutY(T radians, IStorageMath<T> math)
	{
		T c = math.Cos(radians);
		T s = math.Sin(radians);
		return new(c, T.Zero, -s, T.Zero, T.One, T.Zero, s, T.Zero, c);
	}

	/// <summary>The rotation of the axes about z, <c>R3(a)</c>.</summary>
	/// <param name="radians">The angle, in radians.</param>
	/// <param name="math">The transcendental functions.</param>
	/// <returns>The matrix.</returns>
	public static GcrfRotation<T> AboutZ(T radians, IStorageMath<T> math)
	{
		T c = math.Cos(radians);
		T s = math.Sin(radians);
		return new(c, s, T.Zero, -s, c, T.Zero, T.Zero, T.Zero, T.One);
	}

	/// <summary>The product <c>this · other</c>, each element reduced to working precision.</summary>
	/// <param name="other">The right-hand matrix.</param>
	/// <param name="math">The storage type's arithmetic, for its working precision.</param>
	/// <returns>The product.</returns>
	public GcrfRotation<T> Times(GcrfRotation<T> other, IStorageMath<T> math) => new(
		Dot(M11, M12, M13, other.M11, other.M21, other.M31, math),
		Dot(M11, M12, M13, other.M12, other.M22, other.M32, math),
		Dot(M11, M12, M13, other.M13, other.M23, other.M33, math),
		Dot(M21, M22, M23, other.M11, other.M21, other.M31, math),
		Dot(M21, M22, M23, other.M12, other.M22, other.M32, math),
		Dot(M21, M22, M23, other.M13, other.M23, other.M33, math),
		Dot(M31, M32, M33, other.M11, other.M21, other.M31, math),
		Dot(M31, M32, M33, other.M12, other.M22, other.M32, math),
		Dot(M31, M32, M33, other.M13, other.M23, other.M33, math));

	/// <summary>The transpose, which for a rotation is its inverse.</summary>
	/// <returns>The transposed matrix.</returns>
	public GcrfRotation<T> Transposed() => new(M11, M21, M31, M12, M22, M32, M13, M23, M33);

	/// <summary>Applies the matrix to a vector.</summary>
	/// <param name="x">The vector's x component.</param>
	/// <param name="y">The vector's y component.</param>
	/// <param name="z">The vector's z component.</param>
	/// <param name="math">The storage type's arithmetic, for its working precision.</param>
	/// <returns>The rotated vector.</returns>
	public (T X, T Y, T Z) Apply(T x, T y, T z, IStorageMath<T> math) => (
		Dot(M11, M12, M13, x, y, z, math),
		Dot(M21, M22, M23, x, y, z, math),
		Dot(M31, M32, M33, x, y, z, math));

	/// <summary>Applies the transpose of the matrix to a vector, which undoes <see cref="Apply"/>.</summary>
	/// <param name="x">The vector's x component.</param>
	/// <param name="y">The vector's y component.</param>
	/// <param name="z">The vector's z component.</param>
	/// <param name="math">The storage type's arithmetic, for its working precision.</param>
	/// <returns>The rotated vector.</returns>
	public (T X, T Y, T Z) ApplyTranspose(T x, T y, T z, IStorageMath<T> math) => (
		Dot(M11, M21, M31, x, y, z, math),
		Dot(M12, M22, M32, x, y, z, math),
		Dot(M13, M23, M33, x, y, z, math));

	/// <summary>A three-term dot product, reduced to working precision.</summary>
	/// <param name="a1">First term of the left vector.</param>
	/// <param name="a2">Second term of the left vector.</param>
	/// <param name="a3">Third term of the left vector.</param>
	/// <param name="b1">First term of the right vector.</param>
	/// <param name="b2">Second term of the right vector.</param>
	/// <param name="b3">Third term of the right vector.</param>
	/// <param name="math">The storage type's arithmetic.</param>
	/// <returns>The dot product.</returns>
	private static T Dot(T a1, T a2, T a3, T b1, T b2, T b3, IStorageMath<T> math) =>
		math.ToWorkingPrecision((a1 * b1) + (a2 * b2) + (a3 * b3));
}
