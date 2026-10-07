// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Linq;
using System.Numerics;
using ktsu.Osculator.Core.Time;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the time scales, the leap-second table and the single-value instant.
/// </summary>
/// <remarks>
/// The round trips are asserted <em>exactly</em> in <c>PreciseNumber</c>, not to a tolerance: its
/// addition does not round, and an exact round trip is the claim the type exists to make.
/// </remarks>
[TestClass]
public sealed class TimeScaleTests
{
	internal const string IersSample = """
		#  Value of TAI-UTC in second valid beetween the initial value until
		#  the epoch given on the next line. The last line reads that NO
		#  leap second was introduced since the corresponding date
		#
		#  File expires on 28 June 2027
		#
		#    MJD        Date        TAI-UTC (s)
		#           day month year
		#    ---    --------------   ------
		#
		    41317.0    1  1 1972       10
		    41499.0    1  7 1972       11
		    41683.0    1  1 1973       12
		    42048.0    1  1 1974       13
		    42413.0    1  1 1975       14
		    42778.0    1  1 1976       15
		    43144.0    1  1 1977       16
		    43509.0    1  1 1978       17
		    43874.0    1  1 1979       18
		    44239.0    1  1 1980       19
		    44786.0    1  7 1981       20
		    45151.0    1  7 1982       21
		    45516.0    1  7 1983       22
		    46247.0    1  7 1985       23
		    47161.0    1  1 1988       24
		    47892.0    1  1 1990       25
		    48257.0    1  1 1991       26
		    48804.0    1  7 1992       27
		    49169.0    1  7 1993       28
		    49534.0    1  7 1994       29
		    50083.0    1  1 1996       30
		    50630.0    1  7 1997       31
		    51179.0    1  1 1999       32
		    53736.0    1  1 2006       33
		    54832.0    1  1 2009       34
		    56109.0    1  7 2012       35
		    57204.0    1  7 2015       36
		    57754.0    1  1 2017       37
		""";

	[TestMethod]
	public void TheBuiltInTableIsEveryStepSince1972OnTheDatesTheyAreAllowedOn()
	{
		LeapSeconds table = LeapSeconds.BuiltIn;

		Assert.HasCount(28, table.Entries);
		Assert.AreEqual(new LeapSecond(41317, 10), table.Entries[0]);
		Assert.AreEqual(new DateOnly(2017, 1, 1), table.Entries[^1].Date);
		Assert.AreEqual(37, table.Entries[^1].TaiMinusUtcSeconds);

		for (int i = 0; i < table.Entries.Count; i++)
		{
			DateOnly date = table.Entries[i].Date;

			// Each built-in row was written as a number with the date in a comment. This checks the
			// numbers against the dates, since the comments cannot check themselves.
			Assert.AreEqual(1, date.Day, $"{date} is not the first of a month.");
			Assert.IsTrue(date.Month is 1 or 7, $"{date} is not a January or July step.");

			if (i > 0)
			{
				Assert.AreEqual(table.Entries[i - 1].TaiMinusUtcSeconds + 1, table.Entries[i].TaiMinusUtcSeconds);
			}
		}
	}

	[TestMethod]
	public void TheIersFileParsesToTheBuiltInTableAndCarriesItsExpiry()
	{
		LeapSeconds parsed = LeapSeconds.Parse(IersSample);

		CollectionAssert.AreEqual(LeapSeconds.BuiltIn.Entries.ToList(), parsed.Entries.ToList());
		Assert.AreEqual(new DateOnly(2027, 6, 28), parsed.ExpiresOn);
		Assert.IsNull(LeapSeconds.BuiltIn.ExpiresOn, "The built-in table must not claim to be complete past when it was written.");
	}

	[TestMethod]
	public void AMangledFileIsRefusedRatherThanReadAsWrong()
	{
		Assert.ThrowsExactly<FormatException>(() => LeapSeconds.Parse("<html>Sign in to continue</html>"));
		Assert.ThrowsExactly<FormatException>(() => LeapSeconds.Parse("# only comments\n"));

		// The modified Julian date and the calendar date disagree by a day.
		Assert.ThrowsExactly<FormatException>(() => LeapSeconds.Parse("41318.0    1  1 1972       10"));

		// A step of two seconds is a dropped row.
		Assert.ThrowsExactly<FormatException>(() => LeapSeconds.Parse(
			"41317.0    1  1 1972       10\n41683.0    1  1 1973       12"));
	}

	[TestMethod]
	public void UtcBefore1972IsRefused()
	{
		PreciseInstant<double> utc = PreciseInstant<double>.FromCalendar(1971, 12, 31, 12, 0, 0.0, TimeScale.Utc);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => utc.To(TimeScale.Tai, LeapSeconds.BuiltIn));
	}

	[TestMethod]
	[DataRow(2016, 12, 31, 23, 59, 59, 36, DisplayName = "Last second before the 2017 leap second")]
	[DataRow(2017, 1, 1, 0, 0, 0, 37, DisplayName = "First second after it")]
	[DataRow(2015, 6, 30, 23, 59, 59, 35, DisplayName = "Before the 2015 leap second")]
	[DataRow(2015, 7, 1, 0, 0, 0, 36, DisplayName = "After it")]
	public void UtcToTaiToTtToUtcIsExactOnBothSidesOfALeapSecond(int year, int month, int day, int hour, int minute, int second, int expectedOffset)
	{
		PreciseNumber fraction = P(1) / P(3);
		PreciseInstant<PreciseNumber> utc = PreciseInstant<PreciseNumber>.FromCalendar(
			year, month, day, hour, minute, P(second) + (fraction / P(2)), TimeScale.Utc);

		PreciseInstant<PreciseNumber> tai = utc.To(TimeScale.Tai, LeapSeconds.BuiltIn);
		PreciseInstant<PreciseNumber> tt = tai.To(TimeScale.Tt, LeapSeconds.BuiltIn);
		PreciseInstant<PreciseNumber> back = tt.To(TimeScale.Utc, LeapSeconds.BuiltIn);

		Assert.AreEqual(TimeScale.Utc, back.Scale);
		Assert.AreEqual(utc.JulianDay, back.JulianDay, "The round trip must be exact, not close.");
		Assert.AreEqual(back, tai.To(TimeScale.Utc, LeapSeconds.BuiltIn));

		AssertClose(P(expectedOffset), utc.SecondsUntil(tai with { Scale = TimeScale.Utc }), 1e-30);
		AssertClose(P(32184) / P(1000), tai.SecondsUntil(tt with { Scale = TimeScale.Tai }), 1e-30);
	}

	[TestMethod]
	public void TheJ2000EpochInTtIsElevenFiftyEightFiftyFivePointEightOneSixUtc()
	{
		ExpectJ2000<PreciseNumber>(1e-20);
		ExpectJ2000<decimal>(1e-9);

		// One unit in the last place of a double Julian date is 40 microseconds.
		ExpectJ2000<double>(1e-4);
	}

	[TestMethod]
	public void ASubMicrosecondOffsetSurvivesInPreciseNumberAndNotInDouble()
	{
		PreciseInstant<PreciseNumber> precise = new(P(24600005) / P(10), TimeScale.Tt);
		PreciseNumber offset = P(5) / P(10_000_000);

		AssertClose(offset, precise.SecondsUntil(precise.AddSeconds(offset)), 1e-30);

		PreciseInstant<double> coarse = new(2460000.5, TimeScale.Tt);
		double recovered = coarse.SecondsUntil(coarse.AddSeconds(5e-7));

		// The staircase: half a microsecond is a hundredth of a tread, so it vanishes entirely.
		Assert.AreEqual(0.0, recovered);
	}

	[TestMethod]
	public void TheInsertedSecondItselfComesBackOneSecondLateAndNothingElseDoes()
	{
		// TAI 2017-01-01 00:00:36.5 is UTC 2016-12-31 23:59:60.5, which no UTC Julian date can name.
		PreciseNumber midnight = P(57754);
		PreciseNumber halfSecond = P(1) / P(2);
		PreciseInstant<PreciseNumber> inLeapSecond = PreciseInstant<PreciseNumber>
			.FromModifiedJulianDay(midnight, TimeScale.Tai)
			.AddSeconds(P(36) + halfSecond);

		PreciseInstant<PreciseNumber> roundTrip = inLeapSecond.To(TimeScale.Utc, LeapSeconds.BuiltIn).To(TimeScale.Tai, LeapSeconds.BuiltIn);

		AssertClose(PreciseNumber.One, inLeapSecond.SecondsUntil(roundTrip), 1e-30);

		PreciseInstant<PreciseNumber> justAfter = inLeapSecond.AddSeconds(PreciseNumber.One);

		Assert.AreEqual(justAfter, justAfter.To(TimeScale.Utc, LeapSeconds.BuiltIn).To(TimeScale.Tai, LeapSeconds.BuiltIn));
	}

	[TestMethod]
	public void Ut1HasToBeGivenRatherThanAssumed()
	{
		PreciseInstant<decimal> utc = PreciseInstant<decimal>.FromCalendar(2026, 10, 7, 0, 0, 0m, TimeScale.Utc);

		Assert.ThrowsExactly<InvalidOperationException>(() => utc.To(TimeScale.Ut1, LeapSeconds.BuiltIn));

		PreciseInstant<decimal> ut1 = utc.To(TimeScale.Ut1, LeapSeconds.BuiltIn, 0.25m);

		Assert.AreEqual(TimeScale.Ut1, ut1.Scale);
		Assert.AreEqual(0.25m, decimal.Round(utc.SecondsUntil(ut1 with { Scale = TimeScale.Utc }), 12));
		Assert.AreEqual(utc.JulianDay, ut1.To(TimeScale.Utc, LeapSeconds.BuiltIn, 0.25m).JulianDay);
	}

	[TestMethod]
	public void TheTwoPartDateAndTheCalendarComeBackOut()
	{
		JulianDate expected = JulianDate.FromCalendar(2026, 10, 7, 12, 34, 56.789);
		PreciseInstant<PreciseNumber> instant = PreciseInstant<PreciseNumber>.FromCalendar(
			2026, 10, 7, 12, 34, P(56789) / P(1000), TimeScale.Utc);

		JulianDate twoPart = instant.ToJulianDate();

		Assert.AreEqual(expected.Day, twoPart.Day);
		Assert.AreEqual(expected.DayFraction, twoPart.DayFraction, 1e-15);

		(int year, int month, int day, int hour, int minute, PreciseNumber second) = instant.ToCalendar();

		Assert.AreEqual((2026, 10, 7, 12, 34), (year, month, day, hour, minute));
		AssertClose(P(56789) / P(1000), second, 1e-30);

		PreciseInstant<PreciseNumber> fromTwoPart = PreciseInstant<PreciseNumber>.FromJulianDate(twoPart, TimeScale.Utc);

		Assert.AreEqual(P(twoPart.Day) + P(twoPart.DayFraction), fromTwoPart.JulianDay);
	}

	[TestMethod]
	[DataRow(1900, 2, 28)]
	[DataRow(2000, 2, 29)]
	[DataRow(2100, 3, 1)]
	[DataRow(1972, 1, 1)]
	public void TheCalendarIsTheFullGregorianOne(int year, int month, int day)
	{
		PreciseInstant<decimal> instant = PreciseInstant<decimal>.FromCalendar(year, month, day, 6, 0, 0m, TimeScale.Tai);
		(int y, int m, int d, int h, int min, decimal s) = instant.ToCalendar();

		Assert.AreEqual((year, month, day, 6, 0), (y, m, d, h, min));
		Assert.AreEqual(0m, decimal.Round(s, 15));
		Assert.AreEqual(LeapSecond.ModifiedJulianDayOf(new DateOnly(year, month, day)) + 0.25m, instant.ModifiedJulianDay);
	}

	[TestMethod]
	public void AnInsertedSecondCannotBeWrittenInUtc() => Assert.ThrowsExactly<ArgumentOutOfRangeException>(
		() => PreciseInstant<double>.FromCalendar(2016, 12, 31, 23, 59, 60.5, TimeScale.Utc));

	private static PreciseNumber P(long value) => value.ToPreciseNumber();

	private static PreciseNumber P(double value) => value.ToPreciseNumber();

	private static void ExpectJ2000<T>(double toleranceSeconds)
		where T : struct, INumber<T>
	{
		PreciseInstant<T> tt = new(T.CreateChecked(2451545), TimeScale.Tt);
		PreciseInstant<T> utc = tt.To(TimeScale.Utc, LeapSeconds.BuiltIn);
		(int year, int month, int day, int hour, int minute, T second) = utc.ToCalendar();

		Assert.AreEqual((2000, 1, 1, 11, 58), (year, month, day, hour, minute), typeof(T).Name);
		AssertClose(T.CreateChecked(55816) / T.CreateChecked(1000), second, toleranceSeconds);
	}

	private static void AssertClose<T>(T expected, T actual, double tolerance)
		where T : struct, INumber<T>
	{
		T difference = T.Abs(expected - actual);

		Assert.IsTrue(
			difference <= T.CreateChecked(tolerance),
			$"{typeof(T).Name}: expected {expected}, got {actual}, a difference of {difference} against {tolerance}.");
	}
}
