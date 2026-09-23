// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.CelesTrak;

using System;

/// <summary>
/// Thrown when CelesTrak could not supply an element set and nothing usable was cached.
/// </summary>
/// <remarks>
/// Distinct from <see cref="System.Net.Http.HttpRequestException"/> because the two mean different
/// things to a caller: an HTTP failure with something in the cache is not a failure at all here,
/// and this is only raised once the fallback has also come up empty.
/// </remarks>
public sealed class CelesTrakException : Exception
{
	/// <summary>Creates an instance with no message.</summary>
	public CelesTrakException()
	{
	}

	/// <summary>Creates an instance with a message.</summary>
	/// <param name="message">The message.</param>
	public CelesTrakException(string message)
		: base(message)
	{
	}

	/// <summary>Creates an instance with a message and the failure underneath it.</summary>
	/// <param name="message">The message.</param>
	/// <param name="innerException">The underlying failure.</param>
	public CelesTrakException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
