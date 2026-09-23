// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Elements;

/// <summary>
/// How <see cref="TleParser.Parse(string, string, string?, TleChecksum)"/> treats each line's
/// modulo-10 checksum digit.
/// </summary>
public enum TleChecksum
{
	/// <summary>
	/// A line whose checksum digit disagrees with its contents is rejected. This is the default,
	/// because the corruption the digit exists to catch — a re-wrapped, re-typed or truncated line
	/// that is still long enough to parse — otherwise yields a fully-formed element set with
	/// column-shifted fields and no error.
	/// </summary>
	Verify,

	/// <summary>
	/// The checksum digit is not read.
	/// </summary>
	/// <remarks>
	/// For element sets that were constructed rather than served: the standard verification vectors
	/// include objects whose fields were edited by hand without recomputing the digit, and those
	/// sets are still exactly the input a correct implementation has to accept. The line-number
	/// markers are checked either way, since no edit of that kind moves them.
	/// </remarks>
	Ignore,
}
