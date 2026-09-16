// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using ktsu.Osculator.Core.Elements;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the quantization steps the two-line element format fixes.
/// </summary>
[TestClass]
public sealed class ElementFieldQuantizationTests
{
	[TestMethod]
	public void EpochStepIsUnder900Microseconds()
	{
		// Eight decimals of a day. At orbital speed this is about six metres of along-track
		// position, which is already larger than every arithmetic term except float's.
		double seconds = ElementFieldQuantization.EpochDays * 86_400.0;

		Assert.IsLessThan(9e-4, seconds);
		Assert.IsGreaterThan(8e-4, seconds);
	}

	[TestMethod]
	public void EveryFixedDecimalFieldHasAStep()
	{
		Assert.HasCount(8, ElementFieldQuantization.FixedDecimalSteps);

		foreach (System.Collections.Generic.KeyValuePair<string, double> entry in ElementFieldQuantization.FixedDecimalSteps)
		{
			Assert.IsGreaterThan(0.0, entry.Value, $"{entry.Key} reported a non-positive step.");
		}
	}

	[TestMethod]
	public void ExponentialFieldStepScalesWithTheValue()
	{
		// BSTAR is a five-digit mantissa, so its step is a fraction of the value rather than a
		// fixed increment. The ISS element set carries 0.00011249608, which the format writes as
		// 0.11250e-3 — five digits running from 1e-4 down to 1e-8, so 1e-8 is the last of them.
		double step = ElementFieldQuantization.StepForExponentialField(0.00011249608);

		Assert.AreEqual(1e-8, step, 1e-12);

		// A decade up, the same five digits buy one decade less resolution.
		Assert.AreEqual(1e-7, ElementFieldQuantization.StepForExponentialField(0.0011249608), 1e-11);
	}

	[TestMethod]
	public void ExponentialFieldStepIsZeroForAZeroValue()
	{
		Assert.AreEqual(0.0, ElementFieldQuantization.StepForExponentialField(0.0));
	}
}
