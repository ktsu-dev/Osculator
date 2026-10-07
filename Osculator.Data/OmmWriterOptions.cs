// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data;

using ktsu.Osculator.Core.Elements;

/// <summary>
/// The OMM fields that <see cref="ElementSet"/> does not carry, and so have to be told when one is
/// written by <see cref="OmmJsonWriter"/>.
/// </summary>
public sealed record OmmWriterOptions
{
	/// <summary>Gets the options matching every general perturbations set CelesTrak serves.</summary>
	public static OmmWriterOptions Default { get; } = new();

	/// <summary>Gets the <c>EPHEMERIS_TYPE</c>, which is 0 for an SGP4 element set.</summary>
	public int EphemerisType { get; init; }

	/// <summary>Gets the <c>CLASSIFICATION_TYPE</c>, <c>U</c> for unclassified.</summary>
	public string Classification { get; init; } = "U";
}
