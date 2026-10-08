// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.CelesTrak;

using System;

/// <summary>
/// Thrown by <see cref="ResponseCache.FetchAsync"/> when the source could not supply a body and
/// nothing is cached for the key at any age.
/// </summary>
/// <remarks>
/// The inner exception is the failure behind it. It is null only when the call was refused
/// without asking — a recent failed attempt is still inside its back-off — and that attempt was
/// recorded by an earlier process, so the failure itself is not known.
/// </remarks>
public sealed class ResponseUnavailableException : Exception
{
	/// <summary>Creates an instance with no message.</summary>
	public ResponseUnavailableException()
	{
	}

	/// <summary>Creates an instance with a message.</summary>
	/// <param name="message">The message.</param>
	public ResponseUnavailableException(string message)
		: base(message)
	{
	}

	/// <summary>Creates an instance with a message and the failure underneath it.</summary>
	/// <param name="message">The message.</param>
	/// <param name="innerException">The underlying failure.</param>
	public ResponseUnavailableException(string message, Exception? innerException)
		: base(message, innerException)
	{
	}
}
