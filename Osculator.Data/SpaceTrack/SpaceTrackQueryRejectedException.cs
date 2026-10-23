// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.SpaceTrack;

using System;

/// <summary>
/// Thrown when Space-Track answered a query with an <c>error</c> object saying the query itself is
/// wrong.
/// </summary>
/// <remarks>
/// This is an answer, not an outage, as an invalid query is for <see cref="CelesTrak.CelesTrakClient"/>:
/// it is never cached, never throttled, and never answered with an older copy of the same query,
/// because serving last week's data would hide what the service said. Space-Track's rate-limit
/// error is the one <c>error</c> object that is not this; it is raised as
/// <see cref="SpaceTrackRateLimitException"/> and treated as the service being unavailable.
/// </remarks>
public sealed class SpaceTrackQueryRejectedException : SpaceTrackException
{
	/// <summary>Creates an instance with no message.</summary>
	public SpaceTrackQueryRejectedException()
	{
	}

	/// <summary>Creates an instance with a message.</summary>
	/// <param name="message">The message.</param>
	public SpaceTrackQueryRejectedException(string message)
		: base(message)
	{
	}

	/// <summary>Creates an instance with a message and the failure underneath it.</summary>
	/// <param name="message">The message.</param>
	/// <param name="innerException">The underlying failure.</param>
	public SpaceTrackQueryRejectedException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
