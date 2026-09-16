// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System.Numerics;

/// <summary>
/// The WGS-72 gravitational constants, in the units SGP4 works in.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// <strong>WGS-72, not WGS-84, and that is deliberate.</strong> Element sets are produced by fitting
/// this model with these constants. The model is a curve fit rather than the physics, and the
/// constants are part of the fit, so substituting the more accurate WGS-84 values makes the answer
/// <em>worse</em> — by hundreds of metres, growing with the horizon. This looks like a bug to anyone
/// who knows WGS-84 is the better geodetic model, which is why it is written down here.
/// </para>
/// <para>
/// Values are parsed per closed generic rather than converted from <see langword="double"/>, so a
/// high-precision storage type gets the literal rather than fifteen digits of it.
/// </para>
/// </remarks>
public static class Wgs72<T>
	where T : struct, INumber<T>
{
	/// <summary>Gets the Earth's equatorial radius, in kilometres.</summary>
	public static T RadiusEarthKm { get; } = T.CreateChecked(6378.135);

	/// <summary>Gets the gravitational parameter, in cubic kilometres per second squared.</summary>
	public static T Mu { get; } = T.CreateChecked(398600.8);

	/// <summary>Gets the second zonal harmonic.</summary>
	public static T J2 { get; } = T.CreateChecked(0.001082616);

	/// <summary>Gets the third zonal harmonic.</summary>
	public static T J3 { get; } = T.CreateChecked(-0.00000253881);

	/// <summary>Gets the fourth zonal harmonic.</summary>
	public static T J4 { get; } = T.CreateChecked(-0.00000165597);

	/// <summary>Gets the ratio of the third to the second zonal harmonic.</summary>
	public static T J3OverJ2 { get; } = J3 / J2;

	/// <summary>
	/// Gets the square root of the gravitational parameter in Earth radii and minutes.
	/// </summary>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The reciprocal of one time unit in minutes.</returns>
	/// <remarks>
	/// Derived rather than stored, because it is defined by the other two and a literal here could
	/// drift from them. It takes <paramref name="math"/> because the definition contains a square
	/// root, which <see cref="INumber{TSelf}"/> does not provide.
	/// </remarks>
	public static T Xke(IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		T sixty = T.CreateChecked(60);
		return sixty / math.Sqrt(RadiusEarthKm * RadiusEarthKm * RadiusEarthKm / Mu);
	}
}
