// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Numerics;

using System;
using ktsu.Semantics.Quantities;

/// <summary>
/// The transcendental functions for <see langword="decimal"/>, which the base library does not have.
/// </summary>
/// <remarks>
/// <para>
/// <see langword="decimal"/> is the one storage type in this repository that carries more digits
/// than <see langword="double"/> and supplies none of the functions a propagator needs:
/// <see cref="System.Math"/> has no <c>Sqrt</c>, <c>Sin</c> or <c>Pow</c> for it, and it implements
/// neither <c>IRootFunctions</c> nor <c>ITrigonometricFunctions</c>. So these are written here, and
/// that is the point rather than an inconvenience: the arithmetic error a <see langword="decimal"/>
/// propagation shows is partly the error of <em>this file</em> rather than of the language, which
/// makes it the honest illustration of what "more digits" costs.
/// </para>
/// <para>
/// Three rules hold throughout, and each is forced by the type rather than chosen:
/// </para>
/// <list type="number">
/// <item>
/// <strong>Every series terminates when a term stops changing the sum</strong>, not after a fixed
/// count. For a fixed-precision type that is the exact condition — one more term would be discarded
/// by the addition — and it also absorbs the fact that <see langword="decimal"/> underflows to zero
/// below 1e-28 rather than continuing into subnormals.
/// </item>
/// <item>
/// <strong>Terms are built from their predecessor, never from a factorial.</strong> 29! is 8.8e30
/// and <see cref="decimal.MaxValue"/> is 7.9e28, so a series written with a factorial denominator
/// overflows before it converges. Multiplying the previous term by a small ratio keeps every
/// intermediate near the size of the term itself.
/// </item>
/// <item>
/// <strong>Every series index is multiplied out in <see langword="decimal"/>, not in
/// <see langword="int"/>.</strong> None of these loops can reach an index where <c>2n(2n+1)</c>
/// would overflow a 32-bit integer — that needs n near 23,000 and the terms die by n of about
/// twenty — but the bound is a property of the mathematics rather than of a guard in the code, and
/// an integer denominator that wrapped would come back negative and flip a term's sign silently.
/// Small integers convert to <see langword="decimal"/> exactly, so writing it this way costs
/// nothing and the results are unchanged to the last digit.
/// </item>
/// <item>
/// <strong>A power of two is formed once and applied once.</strong> Halving a
/// <see langword="decimal"/> is not exact — the result may need a 29th decimal place — so scaling by
/// 2^k in a loop of k halvings accumulates k roundings. Building 2^k by repeated doubling is exact
/// up to 2^95, and one division at the end costs one rounding instead of ninety.
/// </item>
/// </list>
/// <para>
/// <strong>Accuracy.</strong> Around 27 to 28 significant digits for arguments of ordinary size.
/// Trigonometric arguments are the exception: reduction modulo a 29-digit pi loses digits in
/// proportion to the argument, so a sine of 2,000 radians is good to about 25 digits rather than 28.
/// That is inherent to holding pi in the same type, and is why <see cref="Sin"/> says so.
/// </para>
/// </remarks>
public static class DecimalMath
{
	/// <summary>The ratio of a circle's circumference to its diameter.</summary>
	/// <remarks>
	/// Correct to every digit <see langword="decimal"/> can hold. <c>DecimalMathTests</c> checks this
	/// and every other constant here against a value computed independently in
	/// <see cref="System.Numerics.BigInteger"/> arithmetic, because a mistyped constant is the one
	/// error in a file like this that no self-consistency check would catch.
	/// </remarks>
	public const decimal Pi = 3.1415926535897932384626433833m;

	/// <summary>Half of <see cref="Pi"/>.</summary>
	public const decimal HalfPi = 1.5707963267948966192313216916m;

	/// <summary>Twice <see cref="Pi"/>.</summary>
	public const decimal TwoPi = 6.2831853071795864769252867666m;

	/// <summary>The natural logarithm of two.</summary>
	public const decimal Ln2 = 0.6931471805599453094172321215m;

	/// <summary>The square root of two.</summary>
	public const decimal Sqrt2 = 1.4142135623730950488016887242m;

	/// <summary>The reciprocal of the square root of two.</summary>
	public const decimal InvSqrt2 = 0.7071067811865475244008443621m;

	/// <summary>The largest power of two a <see langword="decimal"/> holds exactly.</summary>
	/// <remarks>
	/// 2^96 is 79,228,162,514,264,337,593,543,950,336, which is exactly one more than
	/// <see cref="decimal.MaxValue"/>. The type is a 96-bit integer with a decimal scale, so it comes
	/// within a single unit of representing the power that would overflow it.
	/// </remarks>
	private const int MaxPowerOfTwo = 95;

	/// <summary>
	/// Computes the square root of a non-negative value.
	/// </summary>
	/// <param name="value">The value.</param>
	/// <returns>The square root.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
	/// <remarks>
	/// <para>
	/// The one function here that is <em>not</em> written in this file, and the exception is worth
	/// being precise about: of the seven functions this module supplies, six had to be written
	/// because nothing available computes them for a <see langword="decimal"/>. The square root is
	/// the seventh, and <see cref="StorageMath.Sqrt{T}"/> in <c>ktsu.Semantics.Quantities</c> — which
	/// this project already depends on — has it. Its answers were measured against this file's own
	/// Newton iteration across seven magnitudes from 1e-10 to 1e20 and were identical in every bit,
	/// so keeping a second copy bought nothing.
	/// </para>
	/// <para>
	/// The negative guard stays, because it is the one thing the delegation would have lost:
	/// <see cref="StorageMath.Sqrt{T}"/> answers a negative radicand with an
	/// <see cref="OverflowException"/>, which is not what went wrong. A domain error should say which
	/// argument was out of its domain.
	/// </para>
	/// </remarks>
	public static decimal Sqrt(decimal value)
	{
		if (value < 0m)
		{
			throw new ArgumentOutOfRangeException(nameof(value), value, "A square root is not defined for a negative value.");
		}

		return StorageMath.Sqrt(value);
	}

	/// <summary>
	/// Computes e raised to a power.
	/// </summary>
	/// <param name="value">The exponent.</param>
	/// <returns>The result.</returns>
	/// <exception cref="OverflowException">The result is outside the range of a <see langword="decimal"/>.</exception>
	/// <remarks>
	/// The exponent is split as <c>k ln2 + r</c> with <c>|r|</c> at most <c>ln2/2</c>, so the series
	/// only ever runs over a small argument and the rest is a power of two.
	/// </remarks>
	public static decimal Exp(decimal value)
	{
		if (value == 0m)
		{
			return 1m;
		}

		int halvings = (int)decimal.Round(value / Ln2, 0, MidpointRounding.ToEven);
		decimal remainder = value - (halvings * Ln2);

		decimal sum = 1m;
		decimal term = 1m;

		for (int n = 1; ; n++)
		{
			term = term * remainder / n;
			decimal next = sum + term;

			if (next == sum)
			{
				break;
			}

			sum = next;
		}

		return ScaleByPowerOfTwo(sum, halvings);
	}

	/// <summary>
	/// Computes the natural logarithm of a positive value.
	/// </summary>
	/// <param name="value">The value.</param>
	/// <returns>The logarithm.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is zero or negative.</exception>
	/// <remarks>
	/// The value is split as <c>m * 2^k</c> with <c>m</c> between <c>1/sqrt(2)</c> and
	/// <c>sqrt(2)</c>, and <c>ln m</c> is computed from the inverse hyperbolic tangent of
	/// <c>(m-1)/(m+1)</c>, whose magnitude is then at most 0.172. Its square is under 0.03, so each
	/// term of the series is a thirtieth of the one before it.
	/// </remarks>
	public static decimal Log(decimal value)
	{
		if (value <= 0m)
		{
			throw new ArgumentOutOfRangeException(nameof(value), value, "A logarithm is not defined at or below zero.");
		}

		int exponent = (int)System.Math.Floor(System.Math.Log2((double)value));
		exponent = System.Math.Clamp(exponent, -MaxPowerOfTwo, MaxPowerOfTwo);
		decimal mantissa = ScaleByPowerOfTwo(value, -exponent);

		// The seed comes from a double and can be a place out either way at the boundaries.
		while (mantissa >= Sqrt2)
		{
			mantissa /= 2m;
			exponent++;
		}

		while (mantissa < InvSqrt2)
		{
			mantissa *= 2m;
			exponent--;
		}

		decimal z = (mantissa - 1m) / (mantissa + 1m);
		decimal zSquared = z * z;
		decimal power = z;
		decimal sum = z;

		for (int n = 1; ; n++)
		{
			power *= zSquared;

			if (power == 0m)
			{
				break;
			}

			decimal next = sum + (power / ((2m * n) + 1m));

			if (next == sum)
			{
				break;
			}

			sum = next;
		}

		return (2m * sum) + (exponent * Ln2);
	}

	/// <summary>
	/// Raises a value to a power.
	/// </summary>
	/// <param name="value">The base.</param>
	/// <param name="exponent">The exponent.</param>
	/// <returns>The result.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="value"/> is negative with a non-integer <paramref name="exponent"/>, or is
	/// zero with a non-positive one.
	/// </exception>
	/// <remarks>
	/// An integer exponent goes through repeated squaring, which is both faster and more accurate
	/// than the exponential route because it never leaves the type. Everything else is
	/// <c>exp(y ln x)</c>, and inherits the error of both.
	/// </remarks>
	public static decimal Pow(decimal value, decimal exponent)
	{
		if (exponent == 0m)
		{
			return 1m;
		}

		if (exponent == decimal.Truncate(exponent) && System.Math.Abs(exponent) <= 64m)
		{
			return IntegerPower(value, (int)exponent);
		}

		if (value < 0m)
		{
			throw new ArgumentOutOfRangeException(nameof(value), value, "A negative base has no real power at a non-integer exponent.");
		}

		if (value == 0m)
		{
			return exponent > 0m
				? 0m
				: throw new ArgumentOutOfRangeException(nameof(exponent), exponent, "Zero has no real power at a non-positive exponent.");
		}

		return Exp(exponent * Log(value));
	}

	/// <summary>
	/// Computes the sine of an angle in radians.
	/// </summary>
	/// <param name="radians">The angle.</param>
	/// <returns>The sine.</returns>
	/// <remarks>
	/// Reduced to a quarter turn and then evaluated by series. The reduction subtracts a multiple of
	/// a stored <see cref="HalfPi"/>, so an angle of magnitude <c>a</c> carries an absolute error of
	/// roughly <c>a * 1e-29</c> into the reduced argument — about 25 good digits at a thousand
	/// radians, against 28 near zero. Holding the constant in the same type is what costs that, and
	/// no rearrangement of this function recovers it.
	/// </remarks>
	public static decimal Sin(decimal radians)
	{
		(decimal reduced, int quadrant) = ReduceToQuarterTurn(radians);

		return quadrant switch
		{
			0 => SinCore(reduced),
			1 => CosCore(reduced),
			2 => -SinCore(reduced),
			_ => -CosCore(reduced),
		};
	}

	/// <summary>
	/// Computes the cosine of an angle in radians.
	/// </summary>
	/// <param name="radians">The angle.</param>
	/// <returns>The cosine.</returns>
	/// <remarks>Reduced the same way as <see cref="Sin"/>, and subject to the same loss.</remarks>
	public static decimal Cos(decimal radians)
	{
		(decimal reduced, int quadrant) = ReduceToQuarterTurn(radians);

		return quadrant switch
		{
			0 => CosCore(reduced),
			1 => -SinCore(reduced),
			2 => -CosCore(reduced),
			_ => SinCore(reduced),
		};
	}

	/// <summary>
	/// Computes the angle whose tangent is the quotient of two values.
	/// </summary>
	/// <param name="y">The ordinate.</param>
	/// <param name="x">The abscissa.</param>
	/// <returns>The angle in radians, between -pi and pi.</returns>
	/// <remarks>
	/// The quotient is always formed with the larger magnitude underneath, so it never exceeds one
	/// and can never overflow — which matters here, because a <see langword="decimal"/> division that
	/// leaves the type throws rather than returning an infinity to be sorted out afterwards.
	/// </remarks>
	public static decimal Atan2(decimal y, decimal x)
	{
		if (x == 0m)
		{
			return y == 0m ? 0m : y > 0m ? HalfPi : -HalfPi;
		}

		if (y == 0m)
		{
			return x > 0m ? 0m : Pi;
		}

		if (System.Math.Abs(y) > System.Math.Abs(x))
		{
			// Within a quarter turn of the vertical. This form already accounts for the sign of x —
			// a negative abscissa makes the arc tangent negative and pushes the angle the right way
			// past the vertical — so it must NOT then be shifted by a half turn as the branch below
			// is. Writing it as though it needed the same shift put the answer half a turn out for
			// every steep direction to the left, which is a position reflected through the origin:
			// about 84,000 km at geosynchronous radius, and the error that found this.
			return (y > 0m ? HalfPi : -HalfPi) - Atan(x / y);
		}

		decimal angle = Atan(y / x);

		if (x > 0m)
		{
			return angle;
		}

		return y > 0m ? angle + Pi : angle - Pi;
	}

	/// <summary>
	/// Computes the arc tangent of a value.
	/// </summary>
	/// <param name="value">The value.</param>
	/// <returns>The angle in radians, between -pi/2 and pi/2.</returns>
	/// <remarks>
	/// The series for the arc tangent converges ruinously slowly near one — at <c>z = 1</c> it is the
	/// Leibniz series, which needs 1e28 terms — so the half-angle identity
	/// <c>atan(z) = 2 atan(z / (1 + sqrt(1 + z²)))</c> is applied four times first. That takes any
	/// argument of magnitude one down below 0.05, where the terms fall by a factor of four hundred
	/// each and a dozen of them suffice.
	/// </remarks>
	public static decimal Atan(decimal value)
	{
		if (value == 0m)
		{
			return 0m;
		}

		if (System.Math.Abs(value) > 1m)
		{
			decimal complement = Atan(1m / value);
			return value > 0m ? HalfPi - complement : -HalfPi - complement;
		}

		const int Halvings = 4;
		decimal z = value;

		for (int step = 0; step < Halvings; step++)
		{
			z /= 1m + Sqrt(1m + (z * z));
		}

		decimal zSquared = z * z;
		decimal power = z;
		decimal sum = z;

		for (int n = 1; ; n++)
		{
			power *= -zSquared;

			if (power == 0m)
			{
				break;
			}

			decimal next = sum + (power / ((2m * n) + 1m));

			if (next == sum)
			{
				break;
			}

			sum = next;
		}

		return sum * (1 << Halvings);
	}

	/// <summary>Sine of an argument already reduced to a quarter turn.</summary>
	/// <param name="radians">The reduced angle.</param>
	/// <returns>The sine.</returns>
	private static decimal SinCore(decimal radians)
	{
		decimal squared = radians * radians;
		decimal term = radians;
		decimal sum = radians;

		for (int n = 1; ; n++)
		{
			decimal twoN = 2m * n;
			term = -term * squared / (twoN * (twoN + 1m));
			decimal next = sum + term;

			if (next == sum)
			{
				break;
			}

			sum = next;
		}

		return sum;
	}

	/// <summary>Cosine of an argument already reduced to a quarter turn.</summary>
	/// <param name="radians">The reduced angle.</param>
	/// <returns>The cosine.</returns>
	private static decimal CosCore(decimal radians)
	{
		decimal squared = radians * radians;
		decimal term = 1m;
		decimal sum = 1m;

		for (int n = 1; ; n++)
		{
			decimal twoN = 2m * n;
			term = -term * squared / ((twoN - 1m) * twoN);
			decimal next = sum + term;

			if (next == sum)
			{
				break;
			}

			sum = next;
		}

		return sum;
	}

	/// <summary>
	/// Reduces an angle to a quarter turn either side of zero, and says which quadrant it came from.
	/// </summary>
	/// <param name="radians">The angle.</param>
	/// <returns>The reduced angle and the quadrant, counted in quarter turns from zero.</returns>
	private static (decimal Reduced, int Quadrant) ReduceToQuarterTurn(decimal radians)
	{
		decimal quarters = decimal.Round(radians / HalfPi, 0, MidpointRounding.ToEven);
		decimal reduced = radians - (quarters * HalfPi);

		// Taken modulo four before the cast, because the count of quarter turns in a large angle
		// overflows an int long before the angle itself leaves the type.
		int quadrant = (int)(quarters % 4m);

		return (reduced, (quadrant + 4) % 4);
	}

	/// <summary>Raises a value to an integer power by repeated squaring.</summary>
	/// <param name="value">The base.</param>
	/// <param name="exponent">The exponent.</param>
	/// <returns>The result.</returns>
	private static decimal IntegerPower(decimal value, int exponent)
	{
		if (exponent < 0)
		{
			return 1m / IntegerPower(value, -exponent);
		}

		decimal result = 1m;
		decimal square = value;

		for (int remaining = exponent; remaining > 0; remaining >>= 1)
		{
			if ((remaining & 1) != 0)
			{
				result *= square;
			}

			if (remaining > 1)
			{
				square *= square;
			}
		}

		return result;
	}

	/// <summary>
	/// Multiplies a value by a power of two, forming the power exactly and applying it once.
	/// </summary>
	/// <param name="value">The value.</param>
	/// <param name="exponent">The power of two, which may be negative.</param>
	/// <returns>The scaled value.</returns>
	/// <exception cref="OverflowException">The result is outside the range of a <see langword="decimal"/>.</exception>
	private static decimal ScaleByPowerOfTwo(decimal value, int exponent)
	{
		if (exponent == 0)
		{
			return value;
		}

		int remaining = System.Math.Abs(exponent);
		decimal scaled = value;

		while (remaining > 0)
		{
			int step = System.Math.Min(remaining, MaxPowerOfTwo);
			decimal power = 1m;

			for (int i = 0; i < step; i++)
			{
				power *= 2m;
			}

			scaled = exponent > 0 ? scaled * power : scaled / power;
			remaining -= step;
		}

		return scaled;
	}
}
