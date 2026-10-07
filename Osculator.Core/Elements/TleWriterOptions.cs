// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Elements;

/// <summary>
/// The parts of a two-line element set that <see cref="ElementSet"/> does not carry, and so has to
/// be told when one is written.
/// </summary>
/// <remarks>
/// None of these change a value the propagator reads. They are spellings: the format has more than
/// one way to write the same element set, and which one a file uses is a property of whoever wrote
/// it rather than of the orbit.
/// </remarks>
public sealed record TleWriterOptions
{
	/// <summary>Gets the options matching what CelesTrak serves today: unclassified, <c>+0</c> exponents.</summary>
	public static TleWriterOptions CelesTrak { get; } = new();

	/// <summary>
	/// Gets the options matching the published SGP4 verification file, which writes a zero exponent
	/// as <c>-0</c>.
	/// </summary>
	public static TleWriterOptions Vallado { get; } = new() { ExponentZeroSign = '-' };

	/// <summary>Gets the classification letter written in column 8, <c>U</c> for unclassified.</summary>
	public char Classification { get; init; } = 'U';

	/// <summary>
	/// Gets the sign written before an exponent of zero in the two assumed-decimal fields, the
	/// second derivative of mean motion and the drag term.
	/// </summary>
	/// <remarks>
	/// Zero has no sign, so the format leaves it to the writer and writers disagree. CelesTrak writes
	/// <c>" 00000+0"</c>; the verification file mostly writes <c>" 00000-0"</c>, and also
	/// <c>"13519-0"</c> for a mantissa whose exponent is zero. The parser reads both the same way.
	/// </remarks>
	public char ExponentZeroSign { get; init; } = '+';
}
