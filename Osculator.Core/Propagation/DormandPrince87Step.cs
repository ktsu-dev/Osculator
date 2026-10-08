// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System.Numerics;

/// <summary>
/// What one step of <see cref="DormandPrince87{T}"/> produced.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="State">The eighth-order state at the end of the step.</param>
/// <param name="ErrorEstimate">
/// The difference between the eighth- and seventh-order solutions, per component. An estimate of
/// the seventh-order solution's local error, and so a conservative bound on the eighth's.
/// </param>
public readonly record struct DormandPrince87Step<T>(CartesianState<T> State, CartesianState<T> ErrorEstimate)
	where T : struct, INumber<T>;
