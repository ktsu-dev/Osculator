// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Forces;

using System.Numerics;

/// <summary>
/// An acceleration in an inertial Cartesian frame, as a force model reports it.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="X">Acceleration along the x axis, in kilometres per second squared.</param>
/// <param name="Y">Acceleration along the y axis, in kilometres per second squared.</param>
/// <param name="Z">Acceleration along the z axis, in kilometres per second squared.</param>
/// <remarks>
/// Signed per component and summed componentwise, so several force models compose by addition.
/// </remarks>
public readonly record struct CartesianAcceleration<T>(T X, T Y, T Z)
	where T : struct, INumber<T>;
