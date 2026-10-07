// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Forces;

using System;
using System.IO;

/// <summary>
/// The Earth Gravitational Model 1996, to degree and order 70.
/// </summary>
/// <remarks>
/// <para>
/// Bundled as text in this assembly, transcribed from the NGA's coefficient set (by way of
/// GeographicLib's binary redistribution of it) and checked to round-trip: every value in the file
/// is the published twelve-digit coefficient exactly. Degree 70 is the usual choice for precise
/// low-Earth-orbit work; the terms above it move a LEO satellite by centimetres over a day. The
/// full degree-360 set reads through <see cref="GravityField.Parse"/> with the constants below.
/// </para>
/// <para>
/// The constants are the ones the coefficients are scaled to, which are <strong>not</strong>
/// WGS-84's: μ = 398600.4415 km³/s² and a = 6378.1363 km, the TOPEX/Poseidon values EGM96 was
/// solved with (NASA/TP-1998-206861). <see cref="TwoBody{T}.EarthGravitationalParameter"/> is the
/// WGS-84 figure, 398600.4418; the 3e-4 km³/s² between them is below a part in a billion, and the
/// harmonic terms are scaled by μ only as a whole, so pairing EGM96's harmonics with WGS-84's
/// central term is the usual practice rather than an inconsistency. C̄₂₀ is the tide-free value.
/// </para>
/// </remarks>
public static class Egm96
{
	/// <summary>The gravitational parameter the coefficients are scaled to, in km³/s².</summary>
	public const string GravitationalParameter = "398600.4415";

	/// <summary>The reference radius the coefficients are scaled to, in km.</summary>
	public const string ReferenceRadius = "6378.1363";

	/// <summary>The highest degree bundled with this assembly.</summary>
	public const int BundledDegree = 70;

	private const string ResourceName = "ktsu.Osculator.Core.Forces.Data.egm96-to70.txt";

	/// <summary>Loads the bundled coefficients to a degree.</summary>
	/// <param name="maximumDegree">The highest degree to keep, from 2 to <see cref="BundledDegree"/>.</param>
	/// <returns>The model.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumDegree"/> is outside 2 to <see cref="BundledDegree"/>.</exception>
	public static GravityField Load(int maximumDegree = BundledDegree)
	{
		if (maximumDegree is < 2 or > BundledDegree)
		{
			throw new ArgumentOutOfRangeException(
				nameof(maximumDegree),
				$"The bundled EGM96 runs from degree 2 to {BundledDegree}; read the full model with GravityField.Parse.");
		}

		using Stream stream = typeof(Egm96).Assembly.GetManifestResourceStream(ResourceName)
			?? throw new InvalidOperationException($"The {ResourceName} resource is missing from {typeof(Egm96).Assembly.GetName().Name}.");
		using StreamReader reader = new(stream);
		return GravityField.Parse(reader, "EGM96", GravitationalParameter, ReferenceRadius, maximumDegree);
	}

	/// <summary>Reads the full NGA distribution of the model from text.</summary>
	/// <param name="reader">The NGA's <c>EGM96</c> coefficient file, or any file in its layout.</param>
	/// <param name="maximumDegree">The highest degree to keep.</param>
	/// <returns>The model.</returns>
	public static GravityField Parse(TextReader reader, int maximumDegree) =>
		GravityField.Parse(reader, "EGM96", GravitationalParameter, ReferenceRadius, maximumDegree);
}
