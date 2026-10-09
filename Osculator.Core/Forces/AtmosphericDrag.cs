// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Forces;

using System;
using System.Globalization;
using System.Numerics;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// Atmospheric drag on a satellite moving through an atmosphere that turns with the Earth.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// <c>a = −½ ρ (C_D A / m) |v_rel| v_rel</c>, with <c>v_rel = v − ω × r</c>: the satellite's
/// velocity relative to air that co-rotates with the Earth. Leaving out the rotation is worth about
/// 7 % of the drag at LEO, which is not small next to anything else here. The integration frame has
/// to have the Earth's spin axis as its z axis for ω × r to be right, which TEME does.
/// </para>
/// <para>
/// Units: density in kg/m³ and the ballistic coefficient in m²/kg make <c>ρ C_D A / m</c> a reciprocal
/// metre, so with the velocity in km/s the product is a thousand times the acceleration in km/s².
/// </para>
/// <para>
/// The altitude is measured from the WGS-84 ellipsoid rather than from a sphere, using the
/// first-order radius <c>a (1 − f sin²φ)</c> at the geocentric latitude φ. A sphere would put a
/// polar orbit 21 km too high over the poles, where the density is a factor of 1.4 lower; the
/// first-order radius is within about 10 m of the true one.
/// </para>
/// </remarks>
public sealed class AtmosphericDrag<T> : IForceModel<T>
	where T : struct, INumber<T>
{
	private readonly ExponentialAtmosphere<T> atmosphere;
	private readonly IStorageMath<T> math;
	private readonly T rotationRate;
	private readonly T equatorialRadius;
	private readonly T flattening;
	private readonly T factor;

	/// <summary>Initializes a new instance of the <see cref="AtmosphericDrag{T}"/> class.</summary>
	/// <param name="model">The density model.</param>
	/// <param name="ballisticCoefficient">C_D A / m, in square metres per kilogram.</param>
	/// <param name="earthRotationRate">The rate the atmosphere turns at, in radians per second.</param>
	/// <param name="storageMath">The transcendental functions and working precision for <typeparamref name="T"/>.</param>
	/// <exception cref="ArgumentNullException"><paramref name="model"/> or <paramref name="storageMath"/> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="ballisticCoefficient"/> is negative.</exception>
	public AtmosphericDrag(ExponentialAtmosphere<T> model, T ballisticCoefficient, T earthRotationRate, IStorageMath<T> storageMath)
	{
		Ensure.NotNull(model);
		Ensure.NotNull(storageMath);
		if (ballisticCoefficient < T.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(ballisticCoefficient), "A ballistic coefficient cannot be negative.");
		}

		atmosphere = model;
		math = storageMath;
		BallisticCoefficient = ballisticCoefficient;
		rotationRate = earthRotationRate;
		equatorialRadius = Parse("6378.137");
		flattening = math.ToWorkingPrecision(T.One / Parse("298.257223563"));
		factor = math.ToWorkingPrecision(Parse("-500") * ballisticCoefficient);
	}

	/// <summary>Gets C_D A / m, in square metres per kilogram.</summary>
	public T BallisticCoefficient { get; }

	/// <summary>The altitude a position is at, above the ellipsoid to first order in the flattening.</summary>
	/// <param name="x">x, in kilometres.</param>
	/// <param name="y">y, in kilometres.</param>
	/// <param name="z">z, in kilometres.</param>
	/// <returns>The altitude, in kilometres.</returns>
	public T Altitude(T x, T y, T z)
	{
		T r2 = (x * x) + (y * y) + (z * z);
		T r = math.Sqrt(r2);
		T sinSquared = math.ToWorkingPrecision(z * z / r2);
		return math.ToWorkingPrecision(r - (equatorialRadius * (T.One - (flattening * sinSquared))));
	}

	/// <inheritdoc />
	public CartesianAcceleration<T> Acceleration(T secondsSinceEpoch, CartesianState<T> state)
	{
		T density = atmosphere.Density(Altitude(state.X, state.Y, state.Z));

		// v − ω × r, with ω along z: ω × r = (−ω y, ω x, 0).
		T vx = math.ToWorkingPrecision(state.VelocityX + (rotationRate * state.Y));
		T vy = math.ToWorkingPrecision(state.VelocityY - (rotationRate * state.X));
		T vz = state.VelocityZ;
		T speed = math.Sqrt((vx * vx) + (vy * vy) + (vz * vz));
		T k = math.ToWorkingPrecision(factor * density * speed);

		return new(
			math.ToWorkingPrecision(k * vx),
			math.ToWorkingPrecision(k * vy),
			math.ToWorkingPrecision(k * vz));
	}

	private static T Parse(string literal) => T.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture);
}
