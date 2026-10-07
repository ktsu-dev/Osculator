// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.SpaceTrack;

using System;

/// <summary>
/// Thrown when Space-Track could not supply an answer and nothing usable was cached.
/// </summary>
/// <remarks>
/// Like <see cref="CelesTrak.CelesTrakException"/>, this is raised only once the cache fallback
/// has also come up empty: a failed request with something cached is not a failure here.
/// </remarks>
public class SpaceTrackException : Exception
{
	/// <summary>Creates an instance with no message.</summary>
	public SpaceTrackException()
	{
	}

	/// <summary>Creates an instance with a message.</summary>
	/// <param name="message">The message.</param>
	public SpaceTrackException(string message)
		: base(message)
	{
	}

	/// <summary>Creates an instance with a message and the failure underneath it.</summary>
	/// <param name="message">The message.</param>
	/// <param name="innerException">The underlying failure.</param>
	public SpaceTrackException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
