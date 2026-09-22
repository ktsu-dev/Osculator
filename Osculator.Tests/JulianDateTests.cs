// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Time;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the two-part Julian date, and the precision it exists to keep.
/// </summary>
[TestClass]
public sealed class JulianDateTests
{
	/// <summary>The same ISS epoch the parser tests use, in the two-line format's own units.</summary>
	private const double IssEpochDayOfYear = 258.88499338;

	/// <summary>Its year.</summary>
	private const int IssEpochYear = 2026;

	[TestMethod]
	public void TheJ2000EpochComesOutAtItsDefinedJulianDate()
	{
		// J2000.0 is 2000 January 1 at 12:00, and is 2451545.0 by definition. A conversion that gets
		// this wrong is wrong about the calendar rather than about precision.
		JulianDate j2000 = JulianDate.FromCalendar(2000, 1, 1, 12, 0, 0.0);

		Assert.AreEqual(2451545.0, j2000.Day + j2000.DayFraction, 0.0);
		Assert.AreEqual(2451544.5, j2000.Day, 0.0);
		Assert.AreEqual(0.5, j2000.DayFraction, 0.0);
	}

	[TestMethod]
	public void TheFractionalDayOfYearResolvesToTheCalendarDateAndTimeTheOmmWritesOut()
	{
		// Cross-check rather than self-check: the two-line format gives this epoch as a fractional
		// day of the year, and the OMM for the same element set gives it as 2026-09-15T21:14:23.428032.
		// Agreeing with that is evidence the month-and-day arithmetic is right, from a source that
		// does not share any of it.
		JulianDate fromDayOfYear = JulianDate.FromDayOfYear(IssEpochYear, IssEpochDayOfYear);
		JulianDate fromCalendar = JulianDate.FromCalendar(2026, 9, 15, 21, 14, 23.428032);

		Assert.AreEqual(fromCalendar.Day, fromDayOfYear.Day, 0.0);
		// A hundred nanoseconds, which is where the two routes to the same instant may differ in
		// their last bits without disagreeing about the calendar.
		Assert.AreEqual(fromCalendar.DayFraction, fromDayOfYear.DayFraction, 1.2e-12);
	}

	[TestMethod]
	public void GoingThroughADateTimeCostsUpToOneTickOfTheEpoch()
	{
		// Measured, because the size of this is the whole argument for ElementSet carrying a Julian
		// date of its own, and it is smaller than it is often said to be: DateTime truncates to its
		// 100-nanosecond tick, it does not round to the millisecond. Across these five real element
		// sets the loss runs from under a nanosecond to 99 of them.
		double[] epochs = [258.88499338, 179.78495062, 305.49999999, 333.02012661, 94.46235912];
		double worstNanoseconds = 0.0;

		foreach (double dayOfYear in epochs)
		{
			JulianDate exact = JulianDate.FromDayOfYear(IssEpochYear, dayOfYear);
			JulianDate viaDateTime = JulianDate.FromUtc(TleParser.EpochOf(IssEpochYear, dayOfYear));
			double nanoseconds = System.Math.Abs(exact.DayFraction - viaDateTime.DayFraction) * 86400.0 * 1e9;

			worstNanoseconds = System.Math.Max(worstNanoseconds, nanoseconds);
		}

		Assert.IsGreaterThan(1.0, worstNanoseconds, "If the loss were nothing at all, the exact path would have no reason to exist.");
		Assert.IsLessThan(100.0, worstNanoseconds, "One tick is the most the truncation can cost; more than that means something else is wrong.");
	}

	[TestMethod]
	public void TheParserGivesTheSameEpochInBothOfItsForms()
	{
		ElementSet iss = TleParser.Parse(
			"1 25544U 98067A   26258.88499338  .00006292  00000+0  12172-3 0  9999",
			"2 25544  51.6310 211.2092 0004923 144.3133 215.8185 15.49128922585812");

		JulianDate expected = JulianDate.FromDayOfYear(IssEpochYear, IssEpochDayOfYear);

		Assert.AreEqual(expected, iss.EpochJulianDate);

		// And the days-since-1950 form the deep-space model actually consumes lands where the
		// calendar says it should: 1949 December 31 to 2026 September 15 is 76.7 years.
		Assert.AreEqual(28017.885, iss.EpochJulianDate.DaysSinceSgp4DayZero, 0.001);
	}
}
