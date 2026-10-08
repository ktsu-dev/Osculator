// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Forces;

using System.Globalization;
using System.Numerics;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// The point-mass gravity of a central body: <c>a = −μ r / |r|³</c>.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// The simplest force there is, and for that reason the one the round-off demonstration runs on. A
/// two-body orbit has an exact solution, conserves its energy and angular momentum exactly, and
/// returns to its starting point after one period, so every departure from those is measurable and
/// attributable to the integrator or to the arithmetic, with no model error to hide behind.
/// </para>
/// <para>
/// The gravitational parameter is a constructor argument rather than a constant, because the right
/// value depends on what the integration is for. <see cref="EarthGravitationalParameter"/> is the
/// EGM96 / WGS-84 value a numerical integrator uses; SGP4's WGS-72 value lives in
/// <see cref="Wgs72{T}"/> and belongs to that model's fit, not to the physics.
/// </para>
/// </remarks>
public sealed class TwoBody<T> : IForceModel<T>
	where T : struct, INumber<T>
{
	private readonly IStorageMath<T> storageMath;

	/// <summary>Initializes a new instance of the <see cref="TwoBody{T}"/> class.</summary>
	/// <param name="gravitationalParameter">μ, in cubic kilometres per second squared.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <exception cref="System.ArgumentNullException"><paramref name="math"/> is null.</exception>
	public TwoBody(T gravitationalParameter, IStorageMath<T> math)
	{
		Ensure.NotNull(math);
		storageMath = math;
		GravitationalParameter = gravitationalParameter;
	}

	/// <summary>
	/// Gets the Earth's gravitational parameter as EGM96 and WGS-84 state it, 398600.4418 km³/s².
	/// </summary>
	/// <remarks>
	/// Parsed from the literal rather than converted from a <see langword="double"/>, so a
	/// high-precision storage type carries exactly the published figure.
	/// </remarks>
	public static T EarthGravitationalParameter { get; } =
		T.Parse("398600.4418", NumberStyles.Float, CultureInfo.InvariantCulture);

	/// <summary>Gets μ, in cubic kilometres per second squared.</summary>
	public T GravitationalParameter { get; }

	/// <inheritdoc />
	public CartesianAcceleration<T> Acceleration(T secondsSinceEpoch, CartesianState<T> state)
	{
		T radiusSquared = (state.X * state.X) + (state.Y * state.Y) + (state.Z * state.Z);
		T radius = storageMath.Sqrt(radiusSquared);
		T scale = storageMath.ToWorkingPrecision(-GravitationalParameter / (radiusSquared * radius));

		return new(
			storageMath.ToWorkingPrecision(scale * state.X),
			storageMath.ToWorkingPrecision(scale * state.Y),
			storageMath.ToWorkingPrecision(scale * state.Z));
	}
}
