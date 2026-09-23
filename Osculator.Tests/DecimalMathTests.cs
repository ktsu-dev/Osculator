// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Globalization;
using System.Numerics;
using ktsu.Osculator.Core.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the hand-written transcendental functions for <see langword="decimal"/>.
/// </summary>
/// <remarks>
/// Two kinds of check, because neither alone is enough. Agreement with <see cref="System.Math"/>
/// proves the functions compute the right quantity, but only to the sixteen digits
/// <see langword="double"/> has — it cannot see the twelve digits that are the entire reason this
/// module exists. Identities and independently computed references cover those.
/// </remarks>
[TestClass]
public sealed class DecimalMathTests
{
	/// <summary>The scale the <see cref="BigInteger"/> references are computed at.</summary>
	/// <remarks>Forty digits, comfortably past the twenty-nine a <see langword="decimal"/> holds.</remarks>
	private const int ReferenceDigits = 40;

	/// <summary>
	/// The largest disagreement allowed between a constant and its reference, in units of the last
	/// place of the reference.
	/// </summary>
	/// <remarks>
	/// One unit in the last place of a <see langword="decimal"/> constant is 1e-28, which is 1e12 at
	/// the reference's scale. Half of that is the most a correctly rounded constant can be out, so
	/// the bound is set just above it: a constant whose final digit is wrong by one is 1e12 out and
	/// fails.
	/// </remarks>
	private static BigInteger ConstantTolerance => 6 * BigInteger.Pow(10, 11);

	/// <summary>The reference scale as a <see cref="BigInteger"/>.</summary>
	private static BigInteger Scale => BigInteger.Pow(10, ReferenceDigits);

	[TestMethod]
	public void EveryConstantMatchesAValueComputedIndependently()
	{
		// The one class of error in a file of series expansions that no self-consistency check would
		// catch: a constant typed wrong. Every function here would still satisfy every identity it is
		// asked about, because they would all be wrong together. So the constants are checked against
		// values computed from scratch in integer arithmetic — Machin's formula for pi, the inverse
		// hyperbolic tangent of a third for the logarithm of two, and Newton's iteration on integers
		// for the square root — rather than against anything typed in.
		BigInteger pi = 4 * ((4 * ArcTangentOfReciprocal(5)) - ArcTangentOfReciprocal(239));
		BigInteger ln2 = 2 * ArcTanhOfOneThird();
		BigInteger sqrt2 = IntegerSquareRoot(2 * Scale * Scale);

		AssertMatches(DecimalMath.Pi, pi, nameof(DecimalMath.Pi));
		AssertMatches(DecimalMath.HalfPi, pi / 2, nameof(DecimalMath.HalfPi));
		AssertMatches(DecimalMath.TwoPi, 2 * pi, nameof(DecimalMath.TwoPi));
		AssertMatches(DecimalMath.Ln2, ln2, nameof(DecimalMath.Ln2));
		AssertMatches(DecimalMath.Sqrt2, sqrt2, nameof(DecimalMath.Sqrt2));
		AssertMatches(DecimalMath.InvSqrt2, Scale * Scale / sqrt2, nameof(DecimalMath.InvSqrt2));
	}

	[TestMethod]
	public void TheSquareRootCarriesMoreDigitsThanADoubleCould()
	{
		// The claim the whole module rests on. Rounding Math.Sqrt into a decimal gives the sixteen
		// digits a double had; this gives the twenty-eight a decimal holds, and the difference is
		// visible against a reference computed in integer arithmetic.
		BigInteger reference = IntegerSquareRoot(2 * Scale * Scale);
		BigInteger fromDecimalMath = Scaled(DecimalMath.Sqrt(2m));
		BigInteger fromDouble = Scaled((decimal)System.Math.Sqrt(2.0));

		BigInteger errorHere = BigInteger.Abs(fromDecimalMath - reference);
		BigInteger errorFromDouble = BigInteger.Abs(fromDouble - reference);

		Console.WriteLine($"sqrt(2): this module is out by {errorHere}, a double round-trip by {errorFromDouble}, at 1e-{ReferenceDigits}");

		// Under one unit in the decimal's last place.
		Assert.IsLessThan(BigInteger.Pow(10, 12), errorHere);

		// And at least a million times worse the other way, which is the twelve digits at stake.
		Assert.IsGreaterThan(errorHere * 1000000, errorFromDouble);
	}

	[TestMethod]
	public void SineAndCosineSatisfyThePythagoreanIdentityToTheTypesPrecision()
	{
		decimal worst = 0m;

		foreach (decimal angle in Angles())
		{
			decimal sin = DecimalMath.Sin(angle);
			decimal cos = DecimalMath.Cos(angle);
			worst = System.Math.Max(worst, System.Math.Abs((sin * sin) + (cos * cos) - 1m));
		}

		Console.WriteLine($"worst |sin^2 + cos^2 - 1| = {worst:E3}");

		// Measured 3e-28, which is the type's own last place. The identity holds just as tightly at a
		// thousand radians as near zero, because it is insensitive to the reduction's own error — the
		// sine and the cosine are both taken of the same reduced argument, whatever that argument is.
		Assert.IsLessThan(1e-27m, worst);
	}

	[TestMethod]
	public void SineAndCosineAgreeWithTheBaseLibraryWhereItCanBeAsked()
	{
		double worst = 0.0;

		foreach (decimal angle in Angles())
		{
			worst = System.Math.Max(worst, System.Math.Abs((double)DecimalMath.Sin(angle) - System.Math.Sin((double)angle)));
			worst = System.Math.Max(worst, System.Math.Abs((double)DecimalMath.Cos(angle) - System.Math.Cos((double)angle)));
		}

		Console.WriteLine($"worst disagreement with System.Math over {ReferenceDigits}-digit arguments: {worst:E3}");

		// A check that the right quantity is being computed, not a check of precision — 2.5e-16
		// measured is double's own last place, which is as close as this comparison can ever get.
		Assert.IsLessThan(1e-14, worst);
	}

	[TestMethod]
	public void TheExponentialAndTheLogarithmInvertEachOther()
	{
		decimal worst = 0m;

		foreach (decimal value in new[] { 1e-20m, 0.001m, 0.5m, 1m, 1.0000001m, 2m, 7m, 1000m, 1e20m })
		{
			decimal round = DecimalMath.Exp(DecimalMath.Log(value));
			worst = System.Math.Max(worst, System.Math.Abs((round - value) / value));
		}

		Console.WriteLine($"worst relative error of exp(log(x)) = {worst:E3}");

		// Measured 4e-28 across twenty orders of magnitude of argument.
		Assert.IsLessThan(1e-26m, worst);
	}

	[TestMethod]
	public void TheLogarithmOfTwoAgreesWithTheConstantItIsBuiltOn()
	{
		// Log reduces against Ln2 and then adds it back, so agreeing here is not circular only
		// because the series path and the constant are independent: the series computes ln of a
		// mantissa near one, and the constant supplies the power of two.
		BigInteger reference = 2 * ArcTanhOfOneThird();
		BigInteger computed = Scaled(DecimalMath.Log(2m));

		Console.WriteLine($"log(2) is out by {BigInteger.Abs(computed - reference)} at 1e-{ReferenceDigits}");

		// Measured 4.2e11, which is under half a unit in the decimal's last place.
		Assert.IsLessThan(BigInteger.Pow(10, 12), BigInteger.Abs(computed - reference));
	}

	[TestMethod]
	public void TheArcTangentInvertsTheTangent()
	{
		decimal worst = 0m;

		foreach (decimal angle in new[] { -1.5m, -0.9m, -0.4m, -0.01m, 0.01m, 0.4m, 0.9m, 1.5m })
		{
			decimal tangent = DecimalMath.Sin(angle) / DecimalMath.Cos(angle);
			worst = System.Math.Max(worst, System.Math.Abs(DecimalMath.Atan(tangent) - angle));
		}

		Console.WriteLine($"worst |atan(tan(a)) - a| = {worst:E3}");

		// Measured 1.6e-27, over a round trip through three separate series.
		Assert.IsLessThan(1e-26m, worst);
	}

	[TestMethod]
	public void TheArcTangentOfTwoArgumentsPutsTheAngleInTheRightQuadrant()
	{
		// The quadrant logic is the half of Atan2 that a magnitude check would never exercise, and
		// getting it wrong is silent: the answer stays plausible and points the wrong way.
		//
		// The diagonals are not enough, and the first version of this test proved it by passing while
		// the function was wrong. Atan2 takes one of two branches depending on which of |y| and |x|
		// is larger, and every case below with |y| == |x| takes the same one. The steep cases — |y|
		// greater than |x|, in all four quadrants — are the other branch, and it was half a turn out.
		(decimal Y, decimal X)[] directions =
		[
			(1m, 1m), (1m, -1m), (-1m, -1m), (-1m, 1m),
			(0m, -1m), (3m, 0m), (-3m, 0m), (0m, 2m),
			(1m, 0.0001m), (1m, -0.0001m), (-1m, -0.0001m), (-1m, 0.0001m),
			(5m, 2m), (5m, -2m), (-5m, -2m), (-5m, 2m),
			(0.0001m, 1m), (0.0001m, -1m), (-0.0001m, -1m), (-0.0001m, 1m),
		];

		foreach ((decimal y, decimal x) in directions)
		{
			decimal mine = DecimalMath.Atan2(y, x);
			double theirs = System.Math.Atan2((double)y, (double)x);

			Assert.AreEqual(theirs, (double)mine, 1e-15, $"atan2({y}, {x})");
		}
	}

	[TestMethod]
	public void RaisingToAnIntegerPowerAgreesWithMultiplyingItOut()
	{
		Assert.AreEqual(8m, DecimalMath.Pow(2m, 3m));
		Assert.AreEqual(0.125m, DecimalMath.Pow(2m, -3m));
		Assert.AreEqual(1m, DecimalMath.Pow(12345m, 0m));

		// Negative bases are defined only at integer exponents, and the integer path is what makes
		// them work at all — the exponential route would need the logarithm of a negative number.
		Assert.AreEqual(-27m, DecimalMath.Pow(-3m, 3m));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DecimalMath.Pow(-3m, 0.5m));
	}

	[TestMethod]
	public void RaisingToAFractionalPowerInvertsUnderTheReciprocalExponent()
	{
		// The exponents SGP4 actually uses. Each is checked by raising and then undoing it, which
		// tests the exp and log pair against nothing but themselves — but at a precision no
		// comparison with double could reach.
		decimal worst = 0m;

		foreach (decimal exponent in new[] { 2m / 3m, -2m / 3m, 1.5m, 3.5m })
		{
			foreach (decimal value in new[] { 0.0001m, 0.5m, 1.000001m, 3m, 1000m })
			{
				decimal round = DecimalMath.Pow(DecimalMath.Pow(value, exponent), 1m / exponent);
				worst = System.Math.Max(worst, System.Math.Abs((round - value) / value));
			}
		}

		Console.WriteLine($"worst relative error of (x^y)^(1/y) = {worst:E3}");

		// Measured 5e-28. Part of what this measures is the rounding of 1/exponent into the type
		// rather than the functions themselves, which is why it is not tighter still.
		Assert.IsLessThan(1e-26m, worst);
	}

	[TestMethod]
	public void TheSquareRootRefusesANegativeValue() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DecimalMath.Sqrt(-1m));

	[TestMethod]
	public void TheLogarithmRefusesZeroAndBelow()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DecimalMath.Log(0m));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DecimalMath.Log(-1m));
	}

	/// <summary>Angles spanning several turns, including ones far enough out to strain the reduction.</summary>
	/// <returns>The angles, in radians.</returns>
	private static decimal[] Angles() =>
	[
		0m, 0.0000001m, 0.5m, 1m, -1m, 1.5707m, 3.14159m, -3.14159m, 4.7m, 6.28m,
		10m, -10m, 100m, 1000m, -1000m, 1974m,
	];

	/// <summary>Scales a decimal up to the reference precision.</summary>
	/// <param name="value">The value.</param>
	/// <returns>The value times ten to the reference precision, truncated.</returns>
	private static BigInteger Scaled(decimal value)
	{
		// Two steps, because the scale factor itself leaves the range of a decimal.
		BigInteger units = new(decimal.Truncate(value));
		BigInteger fraction = new(decimal.Truncate((value - decimal.Truncate(value)) * 1e28m));

		return (units * Scale) + (fraction * BigInteger.Pow(10, ReferenceDigits - 28));
	}

	/// <summary>Asserts a constant matches a reference computed at <see cref="ReferenceDigits"/>.</summary>
	/// <param name="constant">The constant.</param>
	/// <param name="reference">The reference, scaled.</param>
	/// <param name="name">The constant's name, for the failure message.</param>
	private static void AssertMatches(decimal constant, BigInteger reference, string name)
	{
		BigInteger difference = BigInteger.Abs(Scaled(constant) - reference);

		Assert.IsLessThan(
			ConstantTolerance,
			difference,
			string.Create(CultureInfo.InvariantCulture, $"{name} is out by {difference} at 1e-{ReferenceDigits}, which is more than half a unit in its last place."));
	}

	/// <summary>The arc tangent of the reciprocal of an integer, scaled.</summary>
	/// <param name="n">The integer.</param>
	/// <returns>The arc tangent times ten to the reference precision.</returns>
	private static BigInteger ArcTangentOfReciprocal(int n)
	{
		BigInteger squared = (BigInteger)n * n;
		BigInteger power = Scale / n;
		BigInteger sum = power;

		for (int k = 1; ; k++)
		{
			power /= squared;

			if (power.IsZero)
			{
				break;
			}

			BigInteger term = power / ((2 * k) + 1);

			if (term.IsZero)
			{
				break;
			}

			sum += k % 2 == 0 ? term : -term;
		}

		return sum;
	}

	/// <summary>The inverse hyperbolic tangent of one third, scaled.</summary>
	/// <returns>The value times ten to the reference precision.</returns>
	private static BigInteger ArcTanhOfOneThird()
	{
		BigInteger power = Scale / 3;
		BigInteger sum = power;

		for (int k = 1; ; k++)
		{
			power /= 9;

			if (power.IsZero)
			{
				break;
			}

			BigInteger term = power / ((2 * k) + 1);

			if (term.IsZero)
			{
				break;
			}

			sum += term;
		}

		return sum;
	}

	/// <summary>The integer square root, by Newton's iteration.</summary>
	/// <param name="value">The value.</param>
	/// <returns>The largest integer whose square does not exceed <paramref name="value"/>.</returns>
	private static BigInteger IntegerSquareRoot(BigInteger value)
	{
		if (value < 2)
		{
			return value;
		}

		BigInteger root = value;
		BigInteger next = (root + 1) / 2;

		while (next < root)
		{
			root = next;
			next = (root + (value / root)) / 2;
		}

		return root;
	}
}
