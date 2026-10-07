// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.SpaceTrack;

using System;

/// <summary>
/// Thrown when a request was refused by <see cref="SpaceTrackRateLimiter"/> and nothing was cached.
/// </summary>
/// <remarks>
/// The request was never sent. <see cref="RetryAfter"/> says how long until the limiter would
/// grant it, so a caller can wait rather than guess.
/// </remarks>
public sealed class SpaceTrackRateLimitException : SpaceTrackException
{
	/// <summary>Creates an instance with no message.</summary>
	public SpaceTrackRateLimitException()
	{
	}

	/// <summary>Creates an instance with a message.</summary>
	/// <param name="message">The message.</param>
	public SpaceTrackRateLimitException(string message)
		: base(message)
	{
	}

	/// <summary>Creates an instance with a message and the failure underneath it.</summary>
	/// <param name="message">The message.</param>
	/// <param name="innerException">The underlying failure.</param>
	public SpaceTrackRateLimitException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	/// <summary>Creates an instance saying how long to wait.</summary>
	/// <param name="message">The message.</param>
	/// <param name="retryAfter">How long until the limiter has room.</param>
	public SpaceTrackRateLimitException(string message, TimeSpan retryAfter)
		: base(message) => RetryAfter = retryAfter;

	/// <summary>Gets how long until the limiter would grant the request.</summary>
	public TimeSpan RetryAfter { get; }
}
