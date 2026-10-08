// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System.Numerics;

/// <summary>
/// A position and velocity in an inertial Cartesian frame, as a numerical integrator carries them.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="X">Position along the x axis, in kilometres.</param>
/// <param name="Y">Position along the y axis, in kilometres.</param>
/// <param name="Z">Position along the z axis, in kilometres.</param>
/// <param name="VelocityX">Velocity along the x axis, in kilometres per second.</param>
/// <param name="VelocityY">Velocity along the y axis, in kilometres per second.</param>
/// <param name="VelocityZ">Velocity along the z axis, in kilometres per second.</param>
/// <remarks>
/// <para>
/// Deliberately not <see cref="TemeState{T}"/>. That type names its frame because SGP4 emits one
/// frame and nothing else, and confusing it with J2000 is the commonest defect in the field. A
/// numerical integrator has no frame of its own: it integrates in whatever inertial frame the
/// initial state and the force model agree on. Naming this one TEME would make the same mistake in
/// the other direction.
/// </para>
/// <para>
/// Kilometres and seconds, so a state taken from SGP4 can seed an integration without a unit
/// conversion, and so the magnitudes the arithmetic sees are the same in both propagators.
/// </para>
/// </remarks>
public readonly record struct CartesianState<T>(T X, T Y, T Z, T VelocityX, T VelocityY, T VelocityZ)
	where T : struct, INumber<T>;
