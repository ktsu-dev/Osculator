// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using ktsu.Osculator.Core.Elements;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the quantization steps each distributed element-set format fixes.
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

		foreach (KeyValuePair<string, double> entry in ElementFieldQuantization.FixedDecimalSteps)
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

	[TestMethod]
	public void TheDragTermsStepDependsOnTheFormat()
	{
		// The ISS drag term as both formats carry it: 0.12172-3 in the text, 0.00012172288 in the
		// JSON. Five significant digits end at 1e-8; eight end at 1e-11.
		const double bStar = 0.00012172288;

		Assert.AreEqual(1e-8, ElementFieldQuantization.Tle.StepForExponentialField(bStar), 1e-20);
		Assert.AreEqual(1e-11, ElementFieldQuantization.Omm.StepForExponentialField(bStar), 1e-23);
	}

	[TestMethod]
	public void EccentricityCarriesOneMoreDigitInOmm()
	{
		Assert.AreEqual(1e-7, ElementFieldQuantization.Tle.Eccentricity);
		Assert.AreEqual(1e-8, ElementFieldQuantization.Omm.Eccentricity);
	}

	[TestMethod]
	public void EveryOtherFixedDecimalStepIsTheSameInBothFormats()
	{
		foreach (KeyValuePair<string, double> entry in ElementFieldQuantization.Tle.FixedDecimalSteps)
		{
			if (entry.Key == nameof(ElementSet.Eccentricity))
			{
				continue;
			}

			Assert.AreEqual(entry.Value, ElementFieldQuantization.Omm.FixedDecimalSteps[entry.Key], $"{entry.Key} differs between formats.");
		}
	}

	[TestMethod]
	[DataRow(8, 51, 14.158368)]
	[DataRow(21, 14, 23.428032)]
	public void TheJsonEpochIsTheTextEpochRenderedInMicroseconds(int hour, int minute, double second)
	{
		// OMM writes the epoch to the microsecond, which reads as 864 times finer than the text's
		// eighth decimal of a day. Both ISS epochs the tests carry land exactly on that eighth
		// decimal, which a value carried to the microsecond would do about once in 864 tries each.
		// So the JSON renders the text's epoch, and the epoch step is the same in both formats.
		decimal seconds = (hour * 3600m) + (minute * 60m) + (decimal)second;
		decimal stepSeconds = (decimal)ElementFieldQuantization.Omm.EpochDays * 86_400m;

		Assert.AreEqual(0m, seconds % stepSeconds, $"{hour:D2}:{minute:D2}:{second} is not a whole number of {stepSeconds} s steps.");
		Assert.AreEqual(ElementFieldQuantization.Tle.EpochDays, ElementFieldQuantization.Omm.EpochDays);
	}

	[TestMethod]
	public void TheStepsAreLookedUpByTheRecordedFormat()
	{
		Assert.AreSame(ElementFieldQuantization.Tle, ElementFieldQuantization.For(ElementSetFormat.Tle));
		Assert.AreSame(ElementFieldQuantization.Omm, ElementFieldQuantization.For(ElementSetFormat.Omm));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ElementFieldQuantization.For((ElementSetFormat)99));
	}

	[TestMethod]
	public void TheClassLevelStepsAreTheTwoLineFormats()
	{
		// Kept for code reading a TLE column directly; they must not drift from the TLE instance.
		Assert.AreEqual(ElementFieldQuantization.Tle.Eccentricity, ElementFieldQuantization.Eccentricity);
		Assert.AreEqual(ElementFieldQuantization.Tle.ExponentialFieldSignificantDigits, ElementFieldQuantization.ExponentialFieldSignificantDigits);
		Assert.AreSame(ElementFieldQuantization.Tle.FixedDecimalSteps, ElementFieldQuantization.FixedDecimalSteps);
	}
}
