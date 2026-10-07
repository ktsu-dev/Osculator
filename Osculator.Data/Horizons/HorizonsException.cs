// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Horizons;

using System;

/// <summary>
/// A vector table could not be obtained from Horizons.
/// </summary>
public sealed class HorizonsException : Exception
{
	/// <summary>Initializes a new instance of the <see cref="HorizonsException"/> class.</summary>
	public HorizonsException()
	{
	}

	/// <summary>Initializes a new instance of the <see cref="HorizonsException"/> class.</summary>
	/// <param name="message">What went wrong.</param>
	public HorizonsException(string message)
		: base(message)
	{
	}

	/// <summary>Initializes a new instance of the <see cref="HorizonsException"/> class.</summary>
	/// <param name="message">What went wrong.</param>
	/// <param name="innerException">The underlying failure.</param>
	public HorizonsException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
