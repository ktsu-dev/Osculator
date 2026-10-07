// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System.Globalization;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Every function <see cref="PreciseStorageMath"/> supplies, checked against digits computed
/// somewhere else.
/// </summary>
/// <remarks>
/// <para>
/// Gate 5 checks the reference by running it against itself — thirty digits against forty, and
/// thirty against thirty — and neither comparison can see precision lost in a way both runs share.
/// Routing <c>Sin</c>, <c>Cos</c>, <c>Sqrt</c> and <c>Atan2</c> through <see langword="double"/>
/// passed the whole suite and made the convergence gate read <em>better</em> (#25). The only thing
/// that catches a reference quietly computing in <see langword="double"/> is a reference value
/// that was not computed by it.
/// </para>
/// <para>
/// The expected digits come from mpmath at 130 significant digits, which shares no code with
/// PreciseNumber, and are written out here rather than recomputed so that the test has nothing to
/// agree with but the published value. Each function is checked at the gate's thirty digits and at
/// a hundred, because two of the defects this file pins (#67 and #42) only exist above fifty.
/// </para>
/// </remarks>
[TestClass]
public sealed class PreciseStorageMathTests
{
	private const string Sqrt2 = "1.4142135623730950488016887242096980785696718753769480731766797379907324784621070388503875343276415727350138462309122970249248";
	private const string Sin1 = "0.84147098480789650665250232163029899962256306079837106567275170999191040439123966894863974354305269585434903790792067429325912";
	private const string Cos1 = "0.54030230586813971740093660744297660373231042061792222767009725538110039477447176451795185608718308934357173116003008909786063";
	private const string QuarterPi = "0.78539816339744830961566084581987572104929234984377645524373614807695410157155224965700870633552926699553702162832057666177346";
	private const string SinMillion = "-0.34999350217129295211765248678077146906140660532871627385705905464464122639545050506566689766889400811273316905679106496957094";
	private const string FifthRoot2 = "1.1486983549970350067986269467779275894438508890977975055137111184936032062535130568114731130115084739145757178282528087299002";
	private const string SixAndAHalfToSevenTenths = "3.7071430917105852278447676590765223829469176879841163917764381147114131757039148810289065761780727490009549403750370714340234";
	private const string SevenAndAQuarterToOneAndAHalf = "19.521222425862577113283825531833694641569810585962359536591408928810341615250262761677797458589403631017804979003053382202871";
	private const string NineTenthsToThreeAndAHalf = "0.69159012427882455990815801816743554332446670897042492003373631130574040373040139900798706130255297455747285104422612006098763";
	private const string TwoThirds = "0.66666666666666666666666666666666666666666666666666666666666666666666666666666666666666666666666666666666666666666666666666667";

	/// <summary>The gate's reference precision, which has to be good to all but its last two digits.</summary>
	private const int Reference = 30;

	/// <summary>A precision above <see cref="PreciseNumber.MinimumDivisionPrecision"/>, where #67 and #42 lived.</summary>
	private const int High = 100;

	[TestMethod]
	[DataRow(Reference)]
	[DataRow(High)]
	public void SqrtMatchesPublishedDigits(int digits)
	{
		PreciseStorageMath math = new(digits);
		AssertAgrees(math.Sqrt(N(2)), Sqrt2, digits, "sqrt(2)");
	}

	[TestMethod]
	[DataRow(Reference)]
	[DataRow(High)]
	public void SinMatchesPublishedDigits(int digits)
	{
		PreciseStorageMath math = new(digits);
		AssertAgrees(math.Sin(N(1)), Sin1, digits, "sin(1)");
	}

	[TestMethod]
	[DataRow(Reference)]
	[DataRow(High)]
	public void SinOfALargeArgumentSurvivesRangeReduction(int digits)
	{
		// A million radians is 159,155 turns, so six digits of pi are spent before the answer gets
		// any. A double-precision reduction has nothing left by then.
		PreciseStorageMath math = new(digits);
		AssertAgrees(math.Sin(N(1_000_000)), SinMillion, digits, "sin(1e6)");
	}

	[TestMethod]
	[DataRow(Reference)]
	[DataRow(High)]
	public void CosMatchesPublishedDigits(int digits)
	{
		PreciseStorageMath math = new(digits);
		AssertAgrees(math.Cos(N(1)), Cos1, digits, "cos(1)");
	}

	[TestMethod]
	[DataRow(Reference)]
	[DataRow(High)]
	public void Atan2MatchesPublishedDigits(int digits)
	{
		PreciseStorageMath math = new(digits);
		AssertAgrees(math.Atan2(N(1), N(1)), QuarterPi, digits, "atan2(1, 1)");
	}

	[TestMethod]
	[DataRow(Reference)]
	[DataRow(High)]
	public void FractionalPowMatchesPublishedDigits(int digits)
	{
		// Short operands on purpose. PreciseNumber's own Pow works to the wider operand or fifty
		// digits, so a hundred-digit exponent would have hidden #67 by bringing its own precision;
		// a base and exponent written in a few digits are what SGP4's literals look like, and they
		// leave the working precision as the only thing that can ask for more than fifty.
		PreciseStorageMath math = new(digits);

		AssertAgrees(math.Pow(N(2), Parse("0.2")), FifthRoot2, digits, "2^0.2");
		AssertAgrees(math.Pow(Parse("6.5"), Parse("0.7")), SixAndAHalfToSevenTenths, digits, "6.5^0.7");
	}

	[TestMethod]
	[DataRow(Reference)]
	[DataRow(High)]
	public void HalfIntegerPowersMatchPublishedDigits(int digits)
	{
		// The two shapes SGP4 actually raises to: am^1.5 and psisq^3.5, one base above one and one
		// below it.
		PreciseStorageMath math = new(digits);

		AssertAgrees(math.Pow(Parse("7.25"), Parse("1.5")), SevenAndAQuarterToOneAndAHalf, digits, "7.25^1.5");
		AssertAgrees(math.Pow(Parse("0.9"), Parse("3.5")), NineTenthsToThreeAndAHalf, digits, "0.9^3.5");
	}

	[TestMethod]
	public void PowCarriesTheWorkingPrecisionRatherThanFifty()
	{
		// #67 as a property rather than a tolerance: the public two-argument Pow stops at fifty
		// digits for operands this short, so a result with more than that came from somewhere that honours the setting.
		PreciseStorageMath math = new(High);

		Assert.IsGreaterThan(PreciseNumber.MinimumDivisionPrecision + 40, math.Pow(N(2), Parse("0.2")).SignificantDigits);
	}

	[TestMethod]
	public void IntegerPowersStayExact()
	{
		PreciseStorageMath math = new(Reference);

		Assert.AreEqual(PreciseNumber.Pow(Parse("1.000000000000000000000000000000000000000000000000000000001"), N(5)),
			math.Pow(Parse("1.000000000000000000000000000000000000000000000000000000001"), N(5)));
		Assert.AreEqual(N(1024), math.Pow(N(2), N(10)));
	}

	[TestMethod]
	public void DivisionHonoursAWorkingPrecisionAboveFifty()
	{
		// #42: the operator rounds 2 / 3 to fifty digits whatever was asked for. This is the seam
		// a propagator's literal-over-literal quotients go through instead.
		PreciseStorageMath math = new(High);

		AssertAgrees(math.Divide(N(2), N(3)), TwoThirds, High, "2 / 3");
		Assert.IsLessThan(High - 40, (N(2) / N(3)).SignificantDigits,
			"If the operator now honours more than fifty digits on its own, this seam can go.");
	}

	[TestMethod]
	public void DivisionAtOrBelowFiftyIsTheOperator()
	{
		// Nothing gate 5 or the storage comparison measured may move: at the reference precision
		// the seam has to give exactly what the operator gave, including for a wide operand.
		PreciseStorageMath math = new(Reference);

		Assert.AreEqual(N(2) / N(3), math.Divide(N(2), N(3)));
		Assert.AreEqual(PreciseNumber.Pi / N(7), math.Divide(PreciseNumber.Pi, N(7)));
		Assert.AreEqual(Parse("0.125"), math.Divide(N(1), N(8)), "A terminating quotient is exact.");
	}

	private static PreciseNumber N(int value) => value.ToPreciseNumber();

	private static PreciseNumber Parse(string value) =>
		PreciseNumber.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

	/// <summary>
	/// Asserts agreement to all but the last two of <paramref name="digits"/> significant digits.
	/// </summary>
	private static void AssertAgrees(PreciseNumber actual, string expected, int digits, string what)
	{
		PreciseNumber reference = Parse(expected);
		PreciseNumber error = PreciseNumber.Abs(actual - reference);
		PreciseNumber bound = PreciseNumber.Abs(reference) * PreciseNumber.Pow(N(10), N(2 - digits));

		Assert.IsTrue(error < bound,
			$"{what} at {digits} digits is off by {error.To<double>():E3}, more than the last two digits allow. Got {actual}.");
	}
}
