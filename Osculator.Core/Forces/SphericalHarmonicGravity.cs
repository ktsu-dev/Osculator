// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Forces;

using System;
using System.Globalization;
using System.Numerics;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// The non-spherical part of a body's gravity, from a spherical-harmonic model truncated to a
/// chosen degree and order.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// Degrees 2 and up only. The central term is <see cref="TwoBody{T}"/>, so a full Earth field is
/// the two summed in a <see cref="CombinedForceModel{T}"/>, and each can be switched on its own.
/// Degree 2, order 0 alone is the J₂ model.
/// </para>
/// <para>
/// Evaluated with Cunningham's V/W recursion (Montenbruck &amp; Gill, <em>Satellite Orbits</em>,
/// §3.2.4), which works in Cartesian coordinates and so has no singularity at the poles, rewritten
/// for fully normalized coefficients. The normalization is not optional: the unnormalized
/// coefficients at degree 70 span hundreds of orders of magnitude, which overflows a
/// <see langword="float"/> and a <see langword="decimal"/> outright. Every recursion and summation
/// factor is the square root of a ratio of integers, and each is formed in
/// <typeparamref name="T"/> from those integers rather than converted from a <see langword="double"/>,
/// so a 30-digit run carries 30-digit factors.
/// </para>
/// <para>
/// The field is fixed to the Earth, so the position is rotated into the Earth-fixed frame by the
/// <see cref="EarthRotation{T}"/>, the acceleration computed there, and rotated back.
/// </para>
/// </remarks>
public sealed class SphericalHarmonicGravity<T> : IForceModel<T>
	where T : struct, INumber<T>
{
	private readonly IStorageMath<T> math;
	private readonly EarthRotation<T> rotation;
	private readonly T scale;
	private readonly T radius;

	// C̄ and S̄, indexed by GravityField.Index, up to (Degree, Order).
	private readonly T[] cosine;
	private readonly T[] sine;

	// V̄/W̄ recursion factors, indexed by GravityField.Index up to (Degree + 1, Order + 1).
	private readonly T[] diagonal;
	private readonly T[] first;
	private readonly T[] second;

	// Summation factors, indexed by GravityField.Index up to (Degree, Order).
	private readonly T[] zonalX;
	private readonly T[] vertical;
	private readonly T[] raised;
	private readonly T[] lowered;

	/// <summary>Initializes a new instance of the <see cref="SphericalHarmonicGravity{T}"/> class.</summary>
	/// <param name="field">The model.</param>
	/// <param name="degree">The highest degree to sum, from 2 to the model's maximum.</param>
	/// <param name="order">The highest order to sum, from 0 to <paramref name="degree"/>.</param>
	/// <param name="earthRotation">The Earth's orientation during the integration.</param>
	/// <param name="storageMath">The transcendental functions and working precision for <typeparamref name="T"/>.</param>
	/// <exception cref="ArgumentNullException"><paramref name="field"/> or <paramref name="storageMath"/> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The degree or order is outside the model.</exception>
	public SphericalHarmonicGravity(GravityField field, int degree, int order, EarthRotation<T> earthRotation, IStorageMath<T> storageMath)
	{
		Ensure.NotNull(field);
		Ensure.NotNull(storageMath);
		if (degree < 2 || degree > field.MaximumDegree)
		{
			throw new ArgumentOutOfRangeException(nameof(degree), $"The degree must run from 2 to the model's {field.MaximumDegree}.");
		}

		if (order < 0 || order > degree)
		{
			throw new ArgumentOutOfRangeException(nameof(order), "The order must run from 0 to the degree.");
		}

		math = storageMath;
		rotation = earthRotation;
		Degree = degree;
		Order = order;
		radius = Parse(field.ReferenceRadius);
		scale = math.ToWorkingPrecision(Parse(field.GravitationalParameter) / (radius * radius));

		int count = GravityField.Index(degree, degree) + 1;
		cosine = new T[count];
		sine = new T[count];
		zonalX = new T[count];
		vertical = new T[count];
		raised = new T[count];
		lowered = new T[count];
		for (int n = 2; n <= degree; n++)
		{
			for (int m = 0; m <= Math.Min(n, order); m++)
			{
				int i = GravityField.Index(n, m);
				cosine[i] = Parse(field.Cosine(n, m));
				sine[i] = Parse(field.Sine(n, m));

				// Each is N̄ₙₘ / N̄ₙ₊₁,ₖ for the k the term reads, times the unnormalized formula's own
				// factor, squared and reduced to integers. See the class remarks.
				long twoN1 = (2L * n) + 1;
				long twoN3 = (2L * n) + 3;
				zonalX[i] = Root(twoN1 * (n + 1) * (n + 2), 2 * twoN3);
				vertical[i] = Root(twoN1 * (n + m + 1) * (n - m + 1), twoN3);
				raised[i] = Half(Root(twoN1 * (n + m + 1) * (n + m + 2), twoN3));
				if (m > 0)
				{
					lowered[i] = Half(Root((m == 1 ? 2 : 1) * twoN1 * (n - m + 2) * (n - m + 1), twoN3));
				}
			}
		}

		int recursionCount = GravityField.Index(degree + 1, degree + 1) + 1;
		diagonal = new T[recursionCount];
		first = new T[recursionCount];
		second = new T[recursionCount];
		for (int n = 1; n <= degree + 1; n++)
		{
			for (int m = 0; m <= Math.Min(n, order + 1); m++)
			{
				int i = GravityField.Index(n, m);
				if (m == n)
				{
					diagonal[i] = m == 1 ? Root(3, 1) : Root((2L * m) + 1, 2L * m);
					continue;
				}

				first[i] = Root(((2L * n) + 1) * ((2L * n) - 1), (long)(n - m) * (n + m));
				if (n >= m + 2)
				{
					second[i] = Root(((2L * n) + 1) * (n + m - 1) * (n - m - 1), ((2L * n) - 3) * (n + m) * (n - m));
				}
			}
		}
	}

	/// <summary>Gets the highest degree summed.</summary>
	public int Degree { get; }

	/// <summary>Gets the highest order summed.</summary>
	public int Order { get; }

	/// <inheritdoc />
	public CartesianAcceleration<T> Acceleration(T secondsSinceEpoch, CartesianState<T> state)
	{
		T angle = rotation.AngleAt(secondsSinceEpoch, math);
		T cos = math.Cos(angle);
		T sin = math.Sin(angle);

		T x = math.ToWorkingPrecision((cos * state.X) + (sin * state.Y));
		T y = math.ToWorkingPrecision((cos * state.Y) - (sin * state.X));
		T z = state.Z;

		(T ax, T ay, T az) = BodyFixed(x, y, z);

		return new(
			math.ToWorkingPrecision((cos * ax) - (sin * ay)),
			math.ToWorkingPrecision((sin * ax) + (cos * ay)),
			az);
	}

	/// <summary>The acceleration at a position already in the Earth-fixed frame.</summary>
	/// <param name="x">x, in kilometres.</param>
	/// <param name="y">y, in kilometres.</param>
	/// <param name="z">z, in kilometres.</param>
	/// <returns>The acceleration in the same frame, in kilometres per second squared.</returns>
	public (T X, T Y, T Z) BodyFixed(T x, T y, T z)
	{
		int top = Degree + 1;
		int count = GravityField.Index(top, top) + 1;
		T[] v = new T[count];
		T[] w = new T[count];

		T r2 = (x * x) + (y * y) + (z * z);
		T rho = math.ToWorkingPrecision(radius / r2);
		T x0 = math.ToWorkingPrecision(x * rho);
		T y0 = math.ToWorkingPrecision(y * rho);
		T z0 = math.ToWorkingPrecision(z * rho);
		T rr = math.ToWorkingPrecision(radius * rho);

		// V̄₀₀ = R/r, W̄₀₀ = 0; then each column m from its diagonal down through the degrees.
		v[0] = math.ToWorkingPrecision(radius / math.Sqrt(r2));
		w[0] = T.Zero;
		int lastOrder = Math.Min(top, Order + 1);
		for (int m = 0; m <= lastOrder; m++)
		{
			if (m > 0)
			{
				int previous = GravityField.Index(m - 1, m - 1);
				int here = GravityField.Index(m, m);
				T d = diagonal[here];
				v[here] = math.ToWorkingPrecision(d * ((x0 * v[previous]) - (y0 * w[previous])));
				w[here] = math.ToWorkingPrecision(d * ((x0 * w[previous]) + (y0 * v[previous])));
			}

			for (int n = m + 1; n <= top; n++)
			{
				int here = GravityField.Index(n, m);
				int below = GravityField.Index(n - 1, m);
				T a = first[here];
				T vn = a * z0 * v[below];
				T wn = a * z0 * w[below];
				if (n >= m + 2)
				{
					int twoBelow = GravityField.Index(n - 2, m);
					T b = second[here] * rr;
					vn -= b * v[twoBelow];
					wn -= b * w[twoBelow];
				}

				v[here] = math.ToWorkingPrecision(vn);
				w[here] = math.ToWorkingPrecision(wn);
			}
		}

		// Summed from the highest degree down, so the small terms meet each other before they meet
		// the large ones.
		T sx = T.Zero;
		T sy = T.Zero;
		T sz = T.Zero;
		for (int n = Degree; n >= 2; n--)
		{
			for (int m = Math.Min(n, Order); m >= 0; m--)
			{
				int i = GravityField.Index(n, m);
				T c = cosine[i];
				T s = sine[i];
				int up = GravityField.Index(n + 1, m);
				sz -= vertical[i] * ((c * v[up]) + (s * w[up]));

				int upRaised = GravityField.Index(n + 1, m + 1);
				if (m == 0)
				{
					sx -= zonalX[i] * c * v[upRaised];
					sy -= zonalX[i] * c * w[upRaised];
				}
				else
				{
					int upLowered = GravityField.Index(n + 1, m - 1);
					sx += (raised[i] * ((-c * v[upRaised]) - (s * w[upRaised])))
						+ (lowered[i] * ((c * v[upLowered]) + (s * w[upLowered])));
					sy += (raised[i] * ((-c * w[upRaised]) + (s * v[upRaised])))
						+ (lowered[i] * ((-c * w[upLowered]) + (s * v[upLowered])));
				}

				sx = math.ToWorkingPrecision(sx);
				sy = math.ToWorkingPrecision(sy);
				sz = math.ToWorkingPrecision(sz);
			}
		}

		return (
			math.ToWorkingPrecision(scale * sx),
			math.ToWorkingPrecision(scale * sy),
			math.ToWorkingPrecision(scale * sz));
	}

	private static T Parse(string literal) => T.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture);

	private T Root(long numerator, long denominator) =>
		math.ToWorkingPrecision(math.Sqrt(T.CreateChecked(numerator) / T.CreateChecked(denominator)));

	private T Half(T value) => math.ToWorkingPrecision(value / T.CreateChecked(2));
}
