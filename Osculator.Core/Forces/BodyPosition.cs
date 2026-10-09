// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Forces;

using System.Numerics;

/// <summary>
/// Where a perturbing body is, relative to the centre of the integration.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="X">Position along the x axis, in kilometres.</param>
/// <param name="Y">Position along the y axis, in kilometres.</param>
/// <param name="Z">Position along the z axis, in kilometres.</param>
public readonly record struct BodyPosition<T>(T X, T Y, T Z)
	where T : struct, INumber<T>;
