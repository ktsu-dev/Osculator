// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Forces;

using System.Globalization;
using System.Numerics;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// The tidal pull of a third body — the Sun or the Moon — on an orbit about the Earth.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// The acceleration is the body's pull on the satellite less its pull on the Earth,
/// <c>μ (d/|d|³ − s/|s|³)</c> with <c>d = s − r</c>. Written that way it is the difference of two
/// nearly equal vectors: for the Sun the two terms agree to about one part in 20,000, so a
/// <see langword="double"/> loses four or five of its sixteen digits to the subtraction before any
/// physics is involved. This repository exists to measure arithmetic error, so it does not
/// manufacture any. Battin's form (<em>An Introduction to the Mathematics and Methods of
/// Astrodynamics</em>, §8.4) moves the cancellation into a scalar that is computed directly:
/// <c>a = −μ/|d|³ (r + F(q) s)</c>, <c>q = r·(r − 2s)/(s·s)</c>,
/// <c>F(q) = q (3 + 3q + q²)/(1 + (1 + q)^{3/2})</c>. The test checks it against the direct form
/// at fifty digits, where both are exact.
/// </para>
/// <para>
/// The body's position and the satellite's have to be in the same frame. Horizons vectors are
/// ICRF, an SGP4 state is TEME; until the TEME → GCRF transform exists, mixing them misplaces the
/// Moon by up to a third of a degree, which changes its pull on a LEO satellite by under one part
/// in a hundred of an acceleration that is itself ten million times smaller than gravity.
/// </para>
/// </remarks>
public sealed class ThirdBodyGravity<T> : IForceModel<T>
	where T : struct, INumber<T>
{
	private readonly IBodyEphemeris<T> body;
	private readonly IStorageMath<T> math;

	/// <summary>Initializes a new instance of the <see cref="ThirdBodyGravity{T}"/> class.</summary>
	/// <param name="gravitationalParameter">The body's μ, in km³/s².</param>
	/// <param name="ephemeris">Where the body is, relative to the Earth.</param>
	/// <param name="storageMath">The transcendental functions and working precision for <typeparamref name="T"/>.</param>
	/// <exception cref="System.ArgumentNullException"><paramref name="ephemeris"/> or <paramref name="storageMath"/> is null.</exception>
	public ThirdBodyGravity(T gravitationalParameter, IBodyEphemeris<T> ephemeris, IStorageMath<T> storageMath)
	{
		Ensure.NotNull(ephemeris);
		Ensure.NotNull(storageMath);
		GravitationalParameter = gravitationalParameter;
		body = ephemeris;
		math = storageMath;
	}

	/// <summary>Gets the Sun's μ from JPL's DE440, 132712440041.279419 km³/s².</summary>
	public static T SunGravitationalParameter { get; } = Parse("132712440041.279419");

	/// <summary>Gets the Moon's μ from JPL's DE440, 4902.800118 km³/s².</summary>
	public static T MoonGravitationalParameter { get; } = Parse("4902.800118");

	/// <summary>Gets the body's μ, in km³/s².</summary>
	public T GravitationalParameter { get; }

	/// <inheritdoc />
	public CartesianAcceleration<T> Acceleration(T secondsSinceEpoch, CartesianState<T> state)
	{
		BodyPosition<T> s = body.PositionAt(secondsSinceEpoch);
		T dx = s.X - state.X;
		T dy = s.Y - state.Y;
		T dz = s.Z - state.Z;
		T d2 = math.ToWorkingPrecision((dx * dx) + (dy * dy) + (dz * dz));
		T d = math.Sqrt(d2);
		T s2 = math.ToWorkingPrecision((s.X * s.X) + (s.Y * s.Y) + (s.Z * s.Z));

		T two = T.CreateChecked(2);
		T three = T.CreateChecked(3);
		T q = math.ToWorkingPrecision(
			((state.X * (state.X - (two * s.X))) + (state.Y * (state.Y - (two * s.Y))) + (state.Z * (state.Z - (two * s.Z)))) / s2);
		T onePlusQ = T.One + q;
		T f = math.ToWorkingPrecision(q * (three + (three * q) + (q * q)) / (T.One + (onePlusQ * math.Sqrt(onePlusQ))));

		// −μ/d² times (r + F s)/d rather than −μ/d³ times (r + F s): for the Sun, μ/d³ is about
		// 4e-14, where a decimal keeps fourteen digits (domain trap 10); both factors here sit
		// where it keeps twenty.
		T k = math.ToWorkingPrecision(-GravitationalParameter / d2);

		return new(
			math.ToWorkingPrecision(k * ((state.X + (f * s.X)) / d)),
			math.ToWorkingPrecision(k * ((state.Y + (f * s.Y)) / d)),
			math.ToWorkingPrecision(k * ((state.Z + (f * s.Z)) / d)));
	}

	private static T Parse(string literal) => T.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture);
}
