// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using ktsu.Osculator.Data.SpaceTrack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Drives bursts past Space-Track's two hard limits on a clock the test moves by hand.
/// </summary>
/// <remarks>
/// Mutation-checked: counting from a fixed window that resets on the minute fails
/// <see cref="TheMinuteWindowSlidesRatherThanResettingOnTheMinute"/>; dropping the hour check fails
/// <see cref="TheHourLimitHoldsWhenEveryMinuteIsUnderItsOwnLimit"/>; and comparing with
/// <c>&gt;</c> rather than <c>&gt;=</c> admits a thirtieth request and fails
/// <see cref="ABurstStopsAtTwentyNineInAMinute"/>.
/// </remarks>
[TestClass]
public sealed class SpaceTrackRateLimiterTests
{
	private static readonly DateTimeOffset Start = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

	[TestMethod]
	public void ABurstStopsAtTwentyNineInAMinute()
	{
		ManualClock clock = new(Start);
		SpaceTrackRateLimiter limiter = new(clock);

		int granted = 0;

		for (int attempt = 0; attempt < 100; attempt++)
		{
			if (limiter.TryAcquire(out TimeSpan _))
			{
				granted++;
			}
		}

		// Space-Track's limit is "fewer than 30", so 30 must never be reached.
		Assert.AreEqual(29, granted);
	}

	[TestMethod]
	public void ARefusalSaysHowLongUntilThereIsRoom()
	{
		ManualClock clock = new(Start);
		SpaceTrackRateLimiter limiter = new(clock);

		for (int i = 0; i < 29; i++)
		{
			Assert.IsTrue(limiter.TryAcquire(out TimeSpan _));
			clock.Advance(TimeSpan.FromSeconds(1));
		}

		// 29 seconds in: the first grant leaves the window at 60 s.
		Assert.IsFalse(limiter.TryAcquire(out TimeSpan retryAfter));
		Assert.AreEqual(TimeSpan.FromSeconds(31), retryAfter);

		clock.Advance(retryAfter - TimeSpan.FromTicks(1));
		Assert.IsFalse(limiter.TryAcquire(out TimeSpan _), "One tick early.");

		clock.Advance(TimeSpan.FromTicks(1));
		Assert.IsTrue(limiter.TryAcquire(out TimeSpan none));
		Assert.AreEqual(TimeSpan.Zero, none);
	}

	[TestMethod]
	public void TheMinuteWindowSlidesRatherThanResettingOnTheMinute()
	{
		// 29 requests in the last second of one minute and 29 in the first second of the next is 58
		// in two seconds — the burst a window that resets on the minute admits.
		ManualClock clock = new(Start + TimeSpan.FromSeconds(59));
		SpaceTrackRateLimiter limiter = new(clock);

		for (int i = 0; i < 29; i++)
		{
			Assert.IsTrue(limiter.TryAcquire(out TimeSpan _));
		}

		clock.Advance(TimeSpan.FromSeconds(1));

		Assert.IsFalse(limiter.TryAcquire(out TimeSpan retryAfter));
		Assert.AreEqual(TimeSpan.FromSeconds(59), retryAfter);
	}

	[TestMethod]
	public void TheHourLimitHoldsWhenEveryMinuteIsUnderItsOwnLimit()
	{
		ManualClock clock = new(Start);
		SpaceTrackRateLimiter limiter = new(clock);

		// Twenty a minute is comfortably under the minute limit, and reaches 299 inside fifteen
		// minutes, well inside the hour.
		int granted = 0;

		for (int minute = 0; minute < 20; minute++)
		{
			for (int i = 0; i < 20; i++)
			{
				if (limiter.TryAcquire(out TimeSpan _))
				{
					granted++;
				}
			}

			clock.Advance(TimeSpan.FromMinutes(1));
		}

		Assert.AreEqual(299, granted);

		// The first grant was at Start; it leaves the hour at Start + 1 h, and it is now Start + 20 min.
		Assert.IsFalse(limiter.TryAcquire(out TimeSpan retryAfter));
		Assert.AreEqual(TimeSpan.FromMinutes(40), retryAfter);

		clock.Advance(retryAfter);
		Assert.IsTrue(limiter.TryAcquire(out TimeSpan _));
	}

	[TestMethod]
	public void TheLimitsCanBeLoweredButNotRaised()
	{
		ManualClock clock = new(Start);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SpaceTrackRateLimiter(clock, 30, 299));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SpaceTrackRateLimiter(clock, 29, 300));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SpaceTrackRateLimiter(clock, 0, 299));

		SpaceTrackRateLimiter strict = new(clock, 2, 10);

		Assert.IsTrue(strict.TryAcquire(out TimeSpan _));
		Assert.IsTrue(strict.TryAcquire(out TimeSpan _));
		Assert.IsFalse(strict.TryAcquire(out TimeSpan _));
	}

	[TestMethod]
	public void AClockSteppingBackwardsRefusesMoreRatherThanLess()
	{
		ManualClock clock = new(Start);
		SpaceTrackRateLimiter limiter = new(clock);

		for (int i = 0; i < 29; i++)
		{
			Assert.IsTrue(limiter.TryAcquire(out TimeSpan _));
		}

		clock.Advance(TimeSpan.FromHours(-2));

		Assert.IsFalse(limiter.TryAcquire(out TimeSpan _));
	}

	/// <summary>A clock that moves only when told to.</summary>
	/// <param name="start">The starting instant.</param>
	private sealed class ManualClock(DateTimeOffset start) : TimeProvider
	{
		private DateTimeOffset now = start;

		public override DateTimeOffset GetUtcNow() => now;

		public void Advance(TimeSpan by) => now += by;
	}
}
