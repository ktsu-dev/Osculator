// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System.Numerics;

/// <summary>
/// A position and velocity in the True Equator Mean Equinox frame.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="X">Position along the TEME x axis, in kilometres.</param>
/// <param name="Y">Position along the TEME y axis, in kilometres.</param>
/// <param name="Z">Position along the TEME z axis, in kilometres.</param>
/// <param name="VelocityX">Velocity along the TEME x axis, in kilometres per second.</param>
/// <param name="VelocityY">Velocity along the TEME y axis, in kilometres per second.</param>
/// <param name="VelocityZ">Velocity along the TEME z axis, in kilometres per second.</param>
/// <remarks>
/// <para>
/// <strong>TEME is not J2000, and this type is named so the two cannot be confused.</strong> SGP4
/// emits TEME and nothing else; treating its output as ECI/J2000 is the most common defect in
/// amateur trackers and costs between a hundred metres and several kilometres depending on epoch.
/// A conversion belongs in the frame layer, which does not exist yet.
/// </para>
/// <para>
/// Kilometres rather than SI metres, because that is the unit the model works in and every published
/// verification value is quoted in. Converting to a <c>Position3D</c> is the caller's step.
/// </para>
/// </remarks>
public readonly record struct TemeState<T>(T X, T Y, T Z, T VelocityX, T VelocityY, T VelocityZ)
	where T : struct, INumber<T>;
