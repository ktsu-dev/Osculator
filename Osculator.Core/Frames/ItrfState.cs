// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Frames;

using System.Numerics;

/// <summary>
/// A position and velocity in the International Terrestrial Reference Frame: bolted to the
/// Earth's crust, which is the frame a ground station's surveyed coordinates are in.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="X">Position along the ITRF x axis, towards the Greenwich meridian, in kilometres.</param>
/// <param name="Y">Position along the ITRF y axis, towards 90° east, in kilometres.</param>
/// <param name="Z">Position along the ITRF z axis, towards the conventional pole, in kilometres.</param>
/// <param name="VelocityX">Velocity along the ITRF x axis, in km/s, relative to the rotating frame.</param>
/// <param name="VelocityY">Velocity along the ITRF y axis, in km/s, relative to the rotating frame.</param>
/// <param name="VelocityZ">Velocity along the ITRF z axis, in km/s, relative to the rotating frame.</param>
/// <remarks>
/// <para>
/// <strong>This is <see cref="PefState{T}"/> with polar motion applied, and the difference is
/// about 12 metres</strong> at the surface for present-day pole coordinates. PEF's z axis is the
/// Celestial Intermediate Pole — where the rotation axis actually is — and ITRF's is the
/// conventional pole the crust is referenced to. The two wander apart by a few tenths of an
/// arcsecond.
/// </para>
/// <para>
/// Geodetic latitude and longitude are defined against this frame, not against PEF, which is why
/// <see cref="Geodetic{T}"/> takes this type. Twelve metres is small next to SGP4's model error
/// and large next to a laser-ranging residual, so which frame a ground track is computed in
/// stops mattering and starts mattering depending on what it is being compared against.
/// </para>
/// </remarks>
public readonly record struct ItrfState<T>(T X, T Y, T Z, T VelocityX, T VelocityY, T VelocityZ)
	where T : struct, INumber<T>;
