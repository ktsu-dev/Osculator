// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Forces;

using System.Numerics;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// One contribution to the acceleration a numerically integrated orbit feels.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// The seam between the integrator and the physics. <see cref="Cowell{T}"/> knows nothing about
/// gravity; it asks this interface for an acceleration at a time and a state, and integrates
/// whatever comes back. <see cref="TwoBody{T}"/> is the first implementation; spherical harmonics,
/// drag, solar radiation pressure and third bodies implement the same interface.
/// </para>
/// <para>
/// The state carries velocity as well as position because drag depends on it. A model that does
/// not need it ignores it.
/// </para>
/// <para>
/// An implementation should pass what it returns through
/// <see cref="IStorageMath{T}.ToWorkingPrecision"/>. The integrator multiplies every acceleration by
/// a step and a coefficient and sums thirteen of them, so an arbitrary-precision value returned at
/// full width is carried, and grown, through every stage.
/// </para>
/// </remarks>
public interface IForceModel<T>
	where T : struct, INumber<T>
{
	/// <summary>Computes the acceleration at a time and a state.</summary>
	/// <param name="secondsSinceEpoch">The time, in seconds since the integration's epoch.</param>
	/// <param name="state">The position and velocity, in kilometres and kilometres per second.</param>
	/// <returns>The acceleration, in kilometres per second squared.</returns>
	public CartesianAcceleration<T> Acceleration(T secondsSinceEpoch, CartesianState<T> state);
}
