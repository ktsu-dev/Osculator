// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Frames;

using System.Numerics;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;

/// <summary>
/// Rotates between TEME, which SGP4 emits, and the Earth-fixed frame a map is drawn in.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// One rotation about the pole, by Greenwich mean sidereal time. The whole of the difficulty is in
/// which sidereal time and which clock:
/// </para>
/// <para>
/// <strong>It must be the same GMST that SGP4 itself uses, which is the 1982 expression.</strong>
/// TEME is defined by the model that produces it, not by a standard, so pairing SGP4's output with
/// a modern GMST — the IAU 2006 one, say — mixes two definitions of the equinox and leaves a
/// residual of tens of metres. The routine is therefore taken from
/// <see cref="DeepSpace{T}"/> rather than written again here.
/// </para>
/// <para>
/// <strong>And it must be UT1, not UTC.</strong> The two are kept within 0.9 seconds of each other
/// by leap seconds, and 0.9 seconds of Earth rotation is <strong>about 415 metres</strong> at the
/// equator — forty times the polar-motion term this frame deliberately omits, and far the larger
/// error of the two. The UT1 − UTC offset is therefore a required argument rather than
/// an optional refinement: passing zero is a choice to accept up to 415 m, which is fine for a
/// map and not fine for a residual.
/// </para>
/// </remarks>
public static class EarthFixedFrame<T>
	where T : struct, INumber<T>
{
	/// <summary>
	/// The Earth's rotation rate, in radians per second.
	/// </summary>
	/// <remarks>
	/// The IERS mean value, excluding length-of-day variation. It is not derived from the WGS-72
	/// constants the propagator uses: those are part of a curve fit to orbital motion, while this
	/// is how fast the planet turns, and the two have no reason to agree.
	/// </remarks>
	public static T RotationRateRadiansPerSecond { get; } = T.CreateChecked(7.292115146706979e-5);

	/// <summary>
	/// Rotates a TEME state into the Earth-fixed frame.
	/// </summary>
	/// <param name="state">The state SGP4 produced.</param>
	/// <param name="epoch">The instant the state is for, on the UTC scale.</param>
	/// <param name="ut1MinusUtcSeconds">
	/// UT1 − UTC at that instant, in seconds, from an IERS bulletin. Pass zero to accept up to
	/// 415 m of along-track error; see the remarks on this class.
	/// </param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The same state, in the rotating frame.</returns>
	public static PefState<T> FromTeme(TemeState<T> state, JulianDate epoch, double ut1MinusUtcSeconds, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		T theta = SiderealAngle(epoch, ut1MinusUtcSeconds, math);
		T cos = math.Cos(theta);
		T sin = math.Sin(theta);

		// r_pef = ROT3(theta) r_teme. A rotation of the frame, not of the vector, so a point fixed
		// in inertial space appears to move west as the Earth turns east.
		T x = (state.X * cos) + (state.Y * sin);
		T y = (state.Y * cos) - (state.X * sin);

		// v_pef = ROT3(theta) v_teme - omega x r_pef. Without the second term the velocity would be
		// the inertial one merely re-expressed, and a geostationary satellite would read 3 km/s
		// instead of nearly zero — which is the cheapest way to catch its omission.
		T rotatedVelocityX = (state.VelocityX * cos) + (state.VelocityY * sin);
		T rotatedVelocityY = (state.VelocityY * cos) - (state.VelocityX * sin);

		return new PefState<T>(
			x,
			y,
			state.Z,
			rotatedVelocityX + (RotationRateRadiansPerSecond * y),
			rotatedVelocityY - (RotationRateRadiansPerSecond * x),
			state.VelocityZ);
	}

	/// <summary>
	/// Rotates an Earth-fixed state back into TEME.
	/// </summary>
	/// <param name="state">The state in the rotating frame.</param>
	/// <param name="epoch">The instant the state is for, on the UTC scale.</param>
	/// <param name="ut1MinusUtcSeconds">UT1 − UTC at that instant, in seconds.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The same state, in TEME.</returns>
	public static TemeState<T> ToTeme(PefState<T> state, JulianDate epoch, double ut1MinusUtcSeconds, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		T theta = SiderealAngle(epoch, ut1MinusUtcSeconds, math);
		T cos = math.Cos(theta);
		T sin = math.Sin(theta);

		// Undo the rotation-frame term first, then the rotation, in the reverse order to FromTeme.
		T inertialVelocityX = state.VelocityX - (RotationRateRadiansPerSecond * state.Y);
		T inertialVelocityY = state.VelocityY + (RotationRateRadiansPerSecond * state.X);

		return new TemeState<T>(
			(state.X * cos) - (state.Y * sin),
			(state.Y * cos) + (state.X * sin),
			state.Z,
			(inertialVelocityX * cos) - (inertialVelocityY * sin),
			(inertialVelocityY * cos) + (inertialVelocityX * sin),
			state.VelocityZ);
	}

	/// <summary>
	/// Greenwich mean sidereal time at an instant, in radians.
	/// </summary>
	/// <param name="epoch">The instant, on the UTC scale.</param>
	/// <param name="ut1MinusUtcSeconds">UT1 − UTC at that instant, in seconds.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The angle, in radians, in [0, 2π).</returns>
	public static T SiderealAngle(JulianDate epoch, double ut1MinusUtcSeconds, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		// The offset is added to the fraction rather than to the whole, so the two-part Julian date
		// keeps the precision it exists for.
		double ut1Fraction = epoch.DayFraction + (ut1MinusUtcSeconds / 86400.0);

		return DeepSpace<T>.GreenwichSiderealTime(
			T.CreateChecked(epoch.Day) + T.CreateChecked(ut1Fraction), math);
	}
}
