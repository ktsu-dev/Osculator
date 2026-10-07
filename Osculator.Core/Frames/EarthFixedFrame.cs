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
	/// Rotates a TEME state all the way to the ITRF.
	/// </summary>
	/// <param name="state">The state SGP4 produced.</param>
	/// <param name="epoch">The instant the state is for, on the UTC scale.</param>
	/// <param name="orientation">
	/// How the Earth is actually oriented, from an IERS bulletin.
	/// <see cref="EarthOrientation.Ignored"/> accepts up to 415 m from UT1 − UTC and about 12 m
	/// from polar motion.
	/// </param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The same state, in the ITRF.</returns>
	public static ItrfState<T> ToItrf(TemeState<T> state, JulianDate epoch, EarthOrientation orientation, IStorageMath<T> math) =>
		PefToItrf(ToPef(state, epoch, orientation, math), orientation, math);

	/// <summary>
	/// Rotates a TEME state into the Pseudo Earth Fixed frame, stopping short of polar motion.
	/// </summary>
	/// <param name="state">The state SGP4 produced.</param>
	/// <param name="epoch">The instant the state is for, on the UTC scale.</param>
	/// <param name="orientation">How the Earth is actually oriented. Only UT1 − UTC is read here.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The same state, in the rotating frame.</returns>
	public static PefState<T> ToPef(TemeState<T> state, JulianDate epoch, EarthOrientation orientation, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		T theta = SiderealAngle(epoch, orientation.Ut1MinusUtcSeconds, math);
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
	/// Applies polar motion, taking a PEF state to the ITRF.
	/// </summary>
	/// <param name="state">The state in the Pseudo Earth Fixed frame.</param>
	/// <param name="orientation">How the Earth is actually oriented. Only the pole coordinates are read.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The same state, in the ITRF.</returns>
	/// <remarks>
	/// <para>
	/// IERS Technical Note 36 equation 5.3 gives <c>r_TIRS = W · r_ITRS</c> with
	/// <c>W = R3(−s′)·R2(xₚ)·R1(yₚ)</c>, so this way round is
	/// <c>r_ITRS = R1(−yₚ)·R2(−xₚ)·r_TIRS</c>. The TIO locator <c>s′</c> is under a
	/// microarcsecond — a few millimetres at the surface, four orders below the pole coordinates
	/// themselves — and is dropped; TIRS and PEF differ by the same negligible term.
	/// </para>
	/// <para>
	/// <strong>The sign convention is checked rather than recalled.</strong> Applying this to the
	/// PEF z axis must give ITRS <c>(xₚ, −yₚ, 1)</c>, because that is the definition of the pole
	/// coordinates: where the Celestial Intermediate Pole sits in the ITRS, xₚ towards Greenwich
	/// and yₚ towards 90° west. A test asserts exactly that, so getting a sign backwards fails
	/// against the published definition rather than against this code's own arithmetic.
	/// </para>
	/// <para>
	/// The velocity is rotated by the same matrix and carries no extra term: polar motion is a
	/// fixed rotation at any instant, and its own rate is some milliarcseconds per year, which is
	/// nanometres per second.
	/// </para>
	/// </remarks>
	public static ItrfState<T> PefToItrf(PefState<T> state, EarthOrientation orientation, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		T poleX = Arcseconds(orientation.PoleXArcseconds, math);
		T poleY = Arcseconds(orientation.PoleYArcseconds, math);
		T cosX = math.Cos(poleX);
		T sinX = math.Sin(poleX);
		T cosY = math.Cos(poleY);
		T sinY = math.Sin(poleY);

		(T x, T y, T z) = Rotate(state.X, state.Y, state.Z, cosX, sinX, cosY, sinY);
		(T vx, T vy, T vz) = Rotate(state.VelocityX, state.VelocityY, state.VelocityZ, cosX, sinX, cosY, sinY);

		return new ItrfState<T>(x, y, z, vx, vy, vz);
	}

	/// <summary>
	/// Removes polar motion, taking an ITRF state back to PEF.
	/// </summary>
	/// <param name="state">The state in the ITRF.</param>
	/// <param name="orientation">How the Earth is actually oriented.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The same state, in the PEF.</returns>
	public static PefState<T> ItrfToPef(ItrfState<T> state, EarthOrientation orientation, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		// The angles go in unnegated. RotateInverse is already the transpose, which is the whole of
		// the inversion for a rotation matrix; negating the angles as well would flip the sines a
		// second time and undo it. Written the other way first, and the round-trip test read
		// 10.9 m of error rather than zero.
		T poleX = Arcseconds(orientation.PoleXArcseconds, math);
		T poleY = Arcseconds(orientation.PoleYArcseconds, math);
		T cosX = math.Cos(poleX);
		T sinX = math.Sin(poleX);
		T cosY = math.Cos(poleY);
		T sinY = math.Sin(poleY);

		(T x, T y, T z) = RotateInverse(state.X, state.Y, state.Z, cosX, sinX, cosY, sinY);
		(T vx, T vy, T vz) = RotateInverse(state.VelocityX, state.VelocityY, state.VelocityZ, cosX, sinX, cosY, sinY);

		return new PefState<T>(x, y, z, vx, vy, vz);
	}

	/// <summary>
	/// Rotates a PEF state back into TEME.
	/// </summary>
	/// <param name="state">The state in the rotating frame.</param>
	/// <param name="epoch">The instant the state is for, on the UTC scale.</param>
	/// <param name="orientation">How the Earth is actually oriented. Only UT1 − UTC is read here.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The same state, in TEME.</returns>
	public static TemeState<T> PefToTeme(PefState<T> state, JulianDate epoch, EarthOrientation orientation, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		T theta = SiderealAngle(epoch, orientation.Ut1MinusUtcSeconds, math);
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
	/// The polar-motion rotation <c>R1(−yₚ)·R2(−xₚ)</c>, written out.
	/// </summary>
	/// <param name="x">The vector's x component.</param>
	/// <param name="y">The vector's y component.</param>
	/// <param name="z">The vector's z component.</param>
	/// <param name="cosX">Cosine of the x pole coordinate.</param>
	/// <param name="sinX">Sine of the x pole coordinate.</param>
	/// <param name="cosY">Cosine of the y pole coordinate.</param>
	/// <param name="sinY">Sine of the y pole coordinate.</param>
	/// <returns>The rotated vector.</returns>
	private static (T X, T Y, T Z) Rotate(T x, T y, T z, T cosX, T sinX, T cosY, T sinY) => (
		(cosX * x) + (sinX * z),
		(sinY * sinX * x) + (cosY * y) - (sinY * cosX * z),
		-(cosY * sinX * x) + (sinY * y) + (cosY * cosX * z));

	/// <summary>
	/// The transpose of <see cref="Rotate"/>, which for a rotation matrix is its inverse.
	/// </summary>
	/// <param name="x">The vector's x component.</param>
	/// <param name="y">The vector's y component.</param>
	/// <param name="z">The vector's z component.</param>
	/// <param name="cosX">Cosine of the x pole coordinate.</param>
	/// <param name="sinX">Sine of the x pole coordinate.</param>
	/// <param name="cosY">Cosine of the y pole coordinate.</param>
	/// <param name="sinY">Sine of the y pole coordinate.</param>
	/// <returns>The rotated vector.</returns>
	private static (T X, T Y, T Z) RotateInverse(T x, T y, T z, T cosX, T sinX, T cosY, T sinY) => (
		(cosX * x) + (sinX * sinY * y) - (sinX * cosY * z),
		(cosY * y) + (sinY * z),
		(sinX * x) - (cosX * sinY * y) + (cosX * cosY * z));

	/// <summary>Converts arcseconds to radians in the storage type.</summary>
	/// <param name="arcseconds">The angle, in arcseconds.</param>
	/// <param name="math">The transcendental functions, for π.</param>
	/// <returns>The angle, in radians.</returns>
	private static T Arcseconds(double arcseconds, IStorageMath<T> math) =>
		T.CreateChecked(arcseconds) * math.Pi / T.CreateChecked(180.0 * 3600.0);

	/// <summary>
	/// Greenwich mean sidereal time at an instant, in radians.
	/// </summary>
	/// <param name="epoch">The instant, on the UTC scale.</param>
	/// <param name="ut1MinusUtcSeconds">UT1 − UTC at that instant, in seconds.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The angle, in radians, in [0, 2π).</returns>
	/// <remarks>
	/// Sidereal time is a function of the instant, not of the storage type the state is kept in,
	/// so it is evaluated in double and only the finished angle is converted. Doing it in T threw
	/// the precision away the moment the date was formed: near JD 2.46e6 consecutive floats are a
	/// quarter of a day apart, so every instant snapped to a 6-hour mark and GMST was up to 45°
	/// out, about 4,700 km at the surface.
	/// </remarks>
	public static T SiderealAngle(JulianDate epoch, double ut1MinusUtcSeconds, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		// The offset is added to the fraction rather than to the whole, and the two are never
		// summed, so the two-part Julian date keeps the precision it exists for.
		double ut1Fraction = epoch.DayFraction + (ut1MinusUtcSeconds / 86400.0);

		return T.CreateChecked(GreenwichMeanSiderealTime(epoch.Day, ut1Fraction));
	}

	/// <summary>
	/// The 1982 GMST expression SGP4 uses, evaluated from a two-part Julian date without ever
	/// forming the date as one number.
	/// </summary>
	/// <param name="day">The whole part of the UT1 Julian date.</param>
	/// <param name="fraction">The fractional part, which may lie outside [0, 1).</param>
	/// <returns>The angle, in radians, in [0, 2π).</returns>
	/// <remarks>
	/// <para>
	/// The same polynomial as <see cref="DeepSpace{T}"/>'s, so the pairing with SGP4's equinox
	/// holds, but rearranged so that nothing large is ever rounded. Summing the two parts into one
	/// double, as this used to, snaps every instant to the 2⁻³¹-day grid of a Julian date near
	/// 2.46e6 — <strong>40 µs</strong>, or 1.5e-9 rad of rotation — so GMST climbed a staircase
	/// rather than a line. That is domain trap 4, inside the routine written to avoid it.
	/// </para>
	/// <para>
	/// The dominant term, <c>876600 h × T</c>, is exactly 360° per day elapsed since J2000, so
	/// it is taken as whole turns of the day count and reduced before anything else is added: the
	/// whole days vanish exactly, and the part of a day left over is a small number that keeps
	/// every bit of the fraction. What remains of the polynomial is a few thousand degrees at most,
	/// and the century count it is evaluated at needs only ~1e-12 relative precision. The result
	/// agrees with a 50-digit evaluation of the whole polynomial to 8.2e-14 rad, measured, against
	/// 1.4e-9 rad for the single-double sum.
	/// </para>
	/// <para>
	/// SGP4's own <c>Gsto</c> still goes through the single-date routine, deliberately: the
	/// Vallado verification vectors were computed that way, and they stay bit-identical.
	/// </para>
	/// </remarks>
	internal static double GreenwichMeanSiderealTime(double day, double fraction)
	{
		// Exact by Sterbenz's lemma: the two are within a factor of two of each other.
		double daysSinceJ2000 = day - 2451545.0;
		double wholeDays = System.Math.Floor(daysSinceJ2000);

		// Both exact: what is left of a half-integer day count is 0 or 0.5, and the fraction is
		// added to that rather than to a number in the millions.
		double turns = daysSinceJ2000 - wholeDays + fraction;
		turns -= System.Math.Floor(turns);

		double centuries = (daysSinceJ2000 + fraction) / 36525.0;
		double seconds = (-6.2e-6 * centuries * centuries * centuries)
			+ (0.093104 * centuries * centuries)
			+ (8640184.812866 * centuries)
			+ 67310.54841;

		double degrees = (seconds / 240.0 % 360.0) + (360.0 * turns);
		degrees %= 360.0;

		if (degrees < 0.0)
		{
			degrees += 360.0;
		}

		double radians = degrees * (System.Math.PI / 180.0);
		return radians >= 2.0 * System.Math.PI ? radians - (2.0 * System.Math.PI) : radians;
	}
}
