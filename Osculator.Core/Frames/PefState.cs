// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Frames;

using System.Numerics;

/// <summary>
/// A position and velocity in the Pseudo Earth Fixed frame: rotating with the Earth, with its x
/// axis through the Greenwich meridian.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="X">Position along the PEF x axis, towards the Greenwich meridian, in kilometres.</param>
/// <param name="Y">Position along the PEF y axis, in kilometres.</param>
/// <param name="Z">Position along the PEF z axis, towards the pole, in kilometres.</param>
/// <param name="VelocityX">Velocity along the PEF x axis, in km/s, relative to the rotating frame.</param>
/// <param name="VelocityY">Velocity along the PEF y axis, in km/s, relative to the rotating frame.</param>
/// <param name="VelocityZ">Velocity along the PEF z axis, in km/s, relative to the rotating frame.</param>
/// <remarks>
/// <para>
/// <strong>PEF is ITRF to within polar motion, and this type is named so the two cannot be
/// confused</strong> — the same reason <c>TemeState</c> is not called an ECI state. The Earth's
/// rotation axis wanders relative to its crust by a few tenths of an arcsecond, which is about
/// <strong>9 metres</strong> at the surface. Applying that correction needs Earth orientation
/// parameters from the IERS and a sign convention this repository cannot yet check against
/// published test vectors, so it is deliberately not applied here rather than applied on a guess.
/// </para>
/// <para>
/// Nine metres is worth keeping in proportion: it is two orders of magnitude below SGP4's own
/// model error at LEO and eight orders above <see langword="double"/>'s arithmetic error, so it
/// belongs in the Δ_model column and is not a reason to distrust the transform.
/// </para>
/// <para>
/// The velocity is relative to the rotating frame, so it is what a ground observer measures and
/// not the inertial velocity re-expressed. For a geostationary satellite it is near zero.
/// </para>
/// </remarks>
public readonly record struct PefState<T>(T X, T Y, T Z, T VelocityX, T VelocityY, T VelocityZ)
	where T : struct, INumber<T>;
