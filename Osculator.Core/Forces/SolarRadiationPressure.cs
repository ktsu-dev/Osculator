// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Forces;

using System;
using System.Globalization;
using System.Numerics;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// Solar radiation pressure on a cannonball: a sphere, so the force points straight away from the
/// Sun whatever the satellite's attitude, switched off in the Earth's shadow.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// <c>a = P☉ C_R (A/m) (AU/|r − s|)² (r − s)/|r − s|</c>, with P☉ = 4.56e-6 N/m² the pressure at
/// one astronomical unit (Montenbruck &amp; Gill, §3.4). N/m² times m²/kg is m/s², so the result is
/// divided by a thousand for km/s².
/// </para>
/// <para>
/// The shadow is a cylinder of the Earth's equatorial radius behind it, the model Vallado uses. It
/// has no penumbra: the force switches off in one step rather than over the ten or so seconds a
/// LEO satellite takes to cross the penumbra, which a fixed-step integration feels as a small kick
/// at each shadow boundary. A conical model with a penumbra fraction is the refinement.
/// </para>
/// </remarks>
public sealed class SolarRadiationPressure<T> : IForceModel<T>
	where T : struct, INumber<T>
{
	private readonly IBodyEphemeris<T> sun;
	private readonly IStorageMath<T> math;
	private readonly T factor;
	private readonly T auSquared;
	private readonly T earthRadius;

	/// <summary>Initializes a new instance of the <see cref="SolarRadiationPressure{T}"/> class.</summary>
	/// <param name="sunEphemeris">Where the Sun is, relative to the Earth.</param>
	/// <param name="reflectivityAreaToMass">C_R A / m, in square metres per kilogram.</param>
	/// <param name="storageMath">The transcendental functions and working precision for <typeparamref name="T"/>.</param>
	/// <param name="shadow">Whether the Earth's shadow switches the force off.</param>
	/// <exception cref="ArgumentNullException"><paramref name="sunEphemeris"/> or <paramref name="storageMath"/> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="reflectivityAreaToMass"/> is negative.</exception>
	public SolarRadiationPressure(IBodyEphemeris<T> sunEphemeris, T reflectivityAreaToMass, IStorageMath<T> storageMath, bool shadow = true)
	{
		Ensure.NotNull(sunEphemeris);
		Ensure.NotNull(storageMath);
		if (reflectivityAreaToMass < T.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(reflectivityAreaToMass), "C_R A / m cannot be negative.");
		}

		sun = sunEphemeris;
		math = storageMath;
		Shadow = shadow;
		ReflectivityAreaToMass = reflectivityAreaToMass;
		T au = Parse(AstronomicalUnitKm);
		auSquared = math.ToWorkingPrecision(au * au);
		factor = math.ToWorkingPrecision(Parse(PressureAtOneAu) * reflectivityAreaToMass / Parse("1000"));
		earthRadius = Parse("6378.137");
	}

	/// <summary>The solar radiation pressure at one astronomical unit, in N/m².</summary>
	public const string PressureAtOneAu = "4.56e-6";

	/// <summary>The astronomical unit, in kilometres (IAU 2012, exact).</summary>
	public const string AstronomicalUnitKm = "149597870.7";

	/// <summary>Gets C_R A / m, in square metres per kilogram.</summary>
	public T ReflectivityAreaToMass { get; }

	/// <summary>Gets a value indicating whether the Earth's shadow switches the force off.</summary>
	public bool Shadow { get; }

	/// <summary>Whether a position is in the Earth's cylindrical shadow.</summary>
	/// <param name="state">The satellite's state.</param>
	/// <param name="sunPosition">The Sun's position.</param>
	/// <returns><see langword="true"/> if it is behind the Earth and within an Earth radius of the shadow axis.</returns>
	public bool InShadow(CartesianState<T> state, BodyPosition<T> sunPosition)
	{
		T sunDistance = math.Sqrt((sunPosition.X * sunPosition.X) + (sunPosition.Y * sunPosition.Y) + (sunPosition.Z * sunPosition.Z));
		T along = math.ToWorkingPrecision(((state.X * sunPosition.X) + (state.Y * sunPosition.Y) + (state.Z * sunPosition.Z)) / sunDistance);
		if (along >= T.Zero)
		{
			return false;
		}

		T r2 = (state.X * state.X) + (state.Y * state.Y) + (state.Z * state.Z);
		T perpendicular2 = math.ToWorkingPrecision(r2 - (along * along));
		return perpendicular2 < earthRadius * earthRadius;
	}

	/// <inheritdoc />
	public CartesianAcceleration<T> Acceleration(T secondsSinceEpoch, CartesianState<T> state)
	{
		BodyPosition<T> s = sun.PositionAt(secondsSinceEpoch);
		if (Shadow && InShadow(state, s))
		{
			return new(T.Zero, T.Zero, T.Zero);
		}

		T dx = state.X - s.X;
		T dy = state.Y - s.Y;
		T dz = state.Z - s.Z;
		T d2 = math.ToWorkingPrecision((dx * dx) + (dy * dy) + (dz * dz));
		T d = math.Sqrt(d2);

		// The magnitude at this distance, then the direction. Formed as factor/d³ times the offset
		// instead, the quotient is around 6e-22, where a decimal keeps six digits (domain trap 10).
		T k = math.ToWorkingPrecision(factor * (auSquared / d2));

		return new(
			math.ToWorkingPrecision(k * (dx / d)),
			math.ToWorkingPrecision(k * (dy / d)),
			math.ToWorkingPrecision(k * (dz / d)));
	}

	private static T Parse(string literal) => T.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture);
}
