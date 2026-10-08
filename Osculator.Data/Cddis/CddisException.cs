// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Cddis;

using System;

/// <summary>
/// Thrown when CDDIS could not supply an orbit and nothing usable was cached.
/// </summary>
/// <remarks>
/// Like the CelesTrak client's exception, this is raised only once the cache has also come up
/// empty: a failed request with a cached copy behind it is not a failure here. No message built
/// in this namespace contains the Earthdata token, and a test asserts it.
/// </remarks>
public sealed class CddisException : Exception
{
	/// <summary>Initializes a new instance of the <see cref="CddisException"/> class.</summary>
	public CddisException()
	{
	}

	/// <summary>Initializes a new instance of the <see cref="CddisException"/> class.</summary>
	/// <param name="message">What went wrong.</param>
	public CddisException(string message)
		: base(message)
	{
	}

	/// <summary>Initializes a new instance of the <see cref="CddisException"/> class.</summary>
	/// <param name="message">What went wrong.</param>
	/// <param name="innerException">The underlying failure.</param>
	public CddisException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
