// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.SpaceTrack;

using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// Refuses a request that would take this process to 30 requests in a minute or 300 in an hour.
/// </summary>
/// <remarks>
/// <para>
/// Space-Track's limits are hard: its usage policy asks for <em>fewer than</em> 30 requests per
/// minute and fewer than 300 per hour, and an account that breaks them is suspended. So the ceilings
/// here are 29 and 299, they can be lowered but not raised, and a request over them is refused
/// rather than queued. Nothing sleeps on the caller's behalf: a refusal says how long to wait, and
/// whether to wait is the caller's decision.
/// </para>
/// <para>
/// Both windows slide. A fixed window that resets on the minute admits 58 requests in two seconds
/// either side of the boundary, which is exactly the burst the limit exists to forbid. Every
/// request is counted, the login included, because Space-Track counts it.
/// </para>
/// <para>
/// The clock is injected so a burst past either limit can be driven deterministically. A clock that
/// steps backwards leaves the requests it has already counted in the window until it catches up,
/// which refuses more rather than less.
/// </para>
/// </remarks>
public sealed class SpaceTrackRateLimiter
{
	/// <summary>The most requests Space-Track allows in any minute: fewer than 30.</summary>
	public const int MaximumPerMinute = 29;

	/// <summary>The most requests Space-Track allows in any hour: fewer than 300.</summary>
	public const int MaximumPerHour = 299;

	private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

	private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

	private readonly Queue<DateTimeOffset> granted = new();

	private readonly Lock gate = new();

	private readonly TimeProvider time;

	/// <summary>Creates a limiter at Space-Track's own ceilings.</summary>
	/// <param name="time">The clock.</param>
	public SpaceTrackRateLimiter(TimeProvider time)
		: this(time, MaximumPerMinute, MaximumPerHour)
	{
	}

	/// <summary>Creates a limiter stricter than Space-Track's ceilings.</summary>
	/// <param name="time">The clock.</param>
	/// <param name="perMinute">At most <see cref="MaximumPerMinute"/>.</param>
	/// <param name="perHour">At most <see cref="MaximumPerHour"/>.</param>
	/// <exception cref="ArgumentOutOfRangeException">A limit is below one or above Space-Track's.</exception>
	public SpaceTrackRateLimiter(TimeProvider time, int perMinute, int perHour)
	{
		Ensure.NotNull(time);
		ArgumentOutOfRangeException.ThrowIfLessThan(perMinute, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(perMinute, MaximumPerMinute);
		ArgumentOutOfRangeException.ThrowIfLessThan(perHour, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(perHour, MaximumPerHour);

		this.time = time;
		PerMinute = perMinute;
		PerHour = perHour;
	}

	/// <summary>Gets the most requests granted in any sliding minute.</summary>
	public int PerMinute { get; }

	/// <summary>Gets the most requests granted in any sliding hour.</summary>
	public int PerHour { get; }

	/// <summary>
	/// Records a request if both windows have room for it.
	/// </summary>
	/// <param name="retryAfter">
	/// When refused, how long until the window that refused has room again; otherwise zero.
	/// </param>
	/// <returns>Whether the request may be sent.</returns>
	public bool TryAcquire(out TimeSpan retryAfter)
	{
		lock (gate)
		{
			DateTimeOffset now = time.GetUtcNow();

			while (granted.Count > 0 && now - granted.Peek() >= Hour)
			{
				_ = granted.Dequeue();
			}

			// The queue is in grant order, so the requests inside the last minute are its tail.
			DateTimeOffset[] inHour = [.. granted];
			int firstInMinute = Array.FindIndex(inHour, stamp => now - stamp < Minute);
			int inMinute = firstInMinute < 0 ? 0 : inHour.Length - firstInMinute;

			TimeSpan wait = TimeSpan.Zero;

			if (inHour.Length >= PerHour)
			{
				wait = Max(wait, inHour[^PerHour] + Hour - now);
			}

			if (inMinute >= PerMinute)
			{
				wait = Max(wait, inHour[^PerMinute] + Minute - now);
			}

			if (wait > TimeSpan.Zero)
			{
				retryAfter = wait;
				return false;
			}

			granted.Enqueue(now);
			retryAfter = TimeSpan.Zero;
			return true;
		}
	}

	private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;
}
