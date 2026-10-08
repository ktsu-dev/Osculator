// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Frames;

using System.Numerics;

/// <summary>
/// A position and velocity in the Geocentric Celestial Reference Frame.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="X">Position along the GCRF x axis, towards the J2000 equinox, in kilometres.</param>
/// <param name="Y">Position along the GCRF y axis, in kilometres.</param>
/// <param name="Z">Position along the GCRF z axis, towards the J2000 pole, in kilometres.</param>
/// <param name="VelocityX">Velocity along the GCRF x axis, in kilometres per second.</param>
/// <param name="VelocityY">Velocity along the GCRF y axis, in kilometres per second.</param>
/// <param name="VelocityZ">Velocity along the GCRF z axis, in kilometres per second.</param>
/// <remarks>
/// <para>
/// The inertial frame truth sources publish in: JPL Horizons vectors, spacecraft ephemerides, and
/// anything quoted as "J2000" or "ECI". It is fixed against the distant sky, where TEME turns with
/// the precessing equator and drifts tens of kilometres from it at LEO in a few decades.
/// </para>
/// <para>
/// <strong>Whether a value here is the GCRF or FK5 J2000 depends on how it was made</strong>, and
/// the type cannot record that. <see cref="GcrfFrame{T}"/> lands on the GCRF when given the IERS
/// celestial pole offsets and on FK5 mean equator and equinox of J2000 when given
/// <see cref="CelestialPoleOffsets.Ignored"/>. The two are a few tens of milliarcseconds apart,
/// tens of centimetres at LEO: far inside SGP4's model error, and far outside
/// <see langword="double"/>'s arithmetic error.
/// </para>
/// </remarks>
public readonly record struct GcrfState<T>(T X, T Y, T Z, T VelocityX, T VelocityY, T VelocityZ)
	where T : struct, INumber<T>;
