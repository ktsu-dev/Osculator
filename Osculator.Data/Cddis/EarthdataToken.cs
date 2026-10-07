// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Cddis;

using System;

/// <summary>
/// An Earthdata Login bearer token, held so that it cannot be written anywhere by accident.
/// </summary>
/// <remarks>
/// <para>
/// A plain string reaches a log the first time anything interpolates it: an exception message, a
/// debugger display, a <c>ToString</c> in a diagnostic. This type has no member that gives the
/// token back except the internal one the client puts in an <c>Authorization</c> header, and its
/// <see cref="ToString"/> says what it is without saying what it holds. A test asserts both.
/// </para>
/// <para>
/// It is a class rather than a record on purpose: a record synthesises a <c>ToString</c> and an
/// equality that print and compare the value, and either would be a way for the token to leak.
/// </para>
/// </remarks>
public sealed class EarthdataToken
{
	/// <summary>Initializes a new instance of the <see cref="EarthdataToken"/> class.</summary>
	/// <param name="value">The token, as Earthdata Login issued it.</param>
	/// <exception cref="ArgumentException">The token is empty or contains whitespace.</exception>
	public EarthdataToken(string value)
	{
		Ensure.NotNull(value);

		string trimmed = value.Trim();

		// A credential store hands back a trailing newline more often than not; anything inside the
		// token is not something Earthdata issued, and would otherwise split the header.
		if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace))
		{
			throw new ArgumentException("An Earthdata token is a single non-empty word.", nameof(value));
		}

		Value = trimmed;
	}

	/// <summary>Gets the token itself. Internal, so that nothing outside the client can print it.</summary>
	internal string Value { get; }

	/// <summary>Describes the token without revealing it.</summary>
	/// <returns>A fixed placeholder.</returns>
	public override string ToString() => "EarthdataToken(redacted)";
}
