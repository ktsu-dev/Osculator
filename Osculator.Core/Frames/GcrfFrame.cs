// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Frames;

using System.Numerics;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;

/// <summary>
/// Rotates between TEME, which SGP4 emits, and the GCRF, which inertial truth sources publish in.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// Three rotations, in the FK5 reduction Vallado et al. 2006 define TEME against:
/// <c>r_GCRF = Pᵀ · Nᵀ · R3(−Eq_e) · r_TEME</c>. The equation of the equinoxes takes TEME's mean
/// equinox to the true one, giving true of date; nutation takes the true equator to the mean
/// one; precession takes the mean equator and equinox of date back to J2000. See
/// <see cref="PrecessionNutation"/> for why these models and not the 2000A ones.
/// </para>
/// <para>
/// <strong>The instant is TT, not UTC.</strong> Precession and nutation are functions of
/// dynamical time. Passing a UTC date instead is 69 seconds out today, which is 69 seconds of
/// precession and nutation: about a nanoradian, or <strong>under a centimetre</strong> at LEO.
/// Small, and still a choice rather than something to let happen; TT is UTC plus TAI − UTC plus
/// 32.184 s.
/// </para>
/// <para>
/// <strong>The velocity is rotated by the same matrix and carries no rate term.</strong> The
/// matrix itself turns — precession at 50″ a year, nutation at up to about 1e-11 rad/s — and
/// dropping its derivative leaves about <strong>a tenth of a millimetre per second</strong> at
/// LEO. Vallado's reference does the same, which is why the published velocity is reproduced to
/// its last printed digit.
/// </para>
/// </remarks>
public static class GcrfFrame<T>
	where T : struct, INumber<T>
{
	/// <summary>
	/// Rotates a TEME state into the GCRF, with the IAU 1994 equation of the equinoxes.
	/// </summary>
	/// <param name="state">The state SGP4 produced.</param>
	/// <param name="terrestrialTime">The instant the state is for, on the TT scale.</param>
	/// <param name="offsets">
	/// The IERS celestial pole offsets at that instant. <see cref="CelestialPoleOffsets.Ignored"/>
	/// lands on FK5 J2000 rather than the GCRF.
	/// </param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The same state, in the GCRF.</returns>
	public static GcrfState<T> FromTeme(TemeState<T> state, JulianDate terrestrialTime, CelestialPoleOffsets offsets, IStorageMath<T> math) =>
		FromTeme(state, terrestrialTime, offsets, EquationOfEquinoxes.Iau1994, math);

	/// <summary>
	/// Rotates a TEME state into the GCRF.
	/// </summary>
	/// <param name="state">The state SGP4 produced.</param>
	/// <param name="terrestrialTime">The instant the state is for, on the TT scale.</param>
	/// <param name="offsets">The IERS celestial pole offsets at that instant.</param>
	/// <param name="equation">Which equation of the equinoxes takes TEME to true of date.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The same state, in the GCRF.</returns>
	public static GcrfState<T> FromTeme(TemeState<T> state, JulianDate terrestrialTime, CelestialPoleOffsets offsets, EquationOfEquinoxes equation, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		GcrfRotation<T> rotation = TemeToGcrf(PrecessionNutation.At(terrestrialTime, offsets, equation), math);
		(T x, T y, T z) = rotation.Apply(state.X, state.Y, state.Z, math);
		(T vx, T vy, T vz) = rotation.Apply(state.VelocityX, state.VelocityY, state.VelocityZ, math);

		return new GcrfState<T>(x, y, z, vx, vy, vz);
	}

	/// <summary>
	/// Rotates a GCRF state into TEME, with the IAU 1994 equation of the equinoxes.
	/// </summary>
	/// <param name="state">The state in the GCRF.</param>
	/// <param name="terrestrialTime">The instant the state is for, on the TT scale.</param>
	/// <param name="offsets">The IERS celestial pole offsets at that instant.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The same state, in TEME.</returns>
	public static TemeState<T> ToTeme(GcrfState<T> state, JulianDate terrestrialTime, CelestialPoleOffsets offsets, IStorageMath<T> math) =>
		ToTeme(state, terrestrialTime, offsets, EquationOfEquinoxes.Iau1994, math);

	/// <summary>
	/// Rotates a GCRF state into TEME, undoing <see cref="FromTeme(TemeState{T}, JulianDate, CelestialPoleOffsets, EquationOfEquinoxes, IStorageMath{T})"/>.
	/// </summary>
	/// <param name="state">The state in the GCRF.</param>
	/// <param name="terrestrialTime">The instant the state is for, on the TT scale.</param>
	/// <param name="offsets">The IERS celestial pole offsets at that instant.</param>
	/// <param name="equation">Which equation of the equinoxes takes TEME to true of date.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The same state, in TEME.</returns>
	public static TemeState<T> ToTeme(GcrfState<T> state, JulianDate terrestrialTime, CelestialPoleOffsets offsets, EquationOfEquinoxes equation, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		// The transpose of the same matrix, not a second reduction with negated angles: built once,
		// the round trip is exact to the rounding of one matrix rather than two.
		GcrfRotation<T> rotation = TemeToGcrf(PrecessionNutation.At(terrestrialTime, offsets, equation), math);
		(T x, T y, T z) = rotation.ApplyTranspose(state.X, state.Y, state.Z, math);
		(T vx, T vy, T vz) = rotation.ApplyTranspose(state.VelocityX, state.VelocityY, state.VelocityZ, math);

		return new TemeState<T>(x, y, z, vx, vy, vz);
	}

	/// <summary>The equation-of-the-equinoxes step, TEME to true of date: <c>R3(−Eq_e)</c>.</summary>
	/// <param name="angles">The reduction's angles.</param>
	/// <param name="math">The transcendental functions.</param>
	/// <returns>The rotation.</returns>
	internal static GcrfRotation<T> TemeToTrueOfDate(PrecessionNutationAngles angles, IStorageMath<T> math) =>
		GcrfRotation<T>.AboutZ(-Angle(angles.EquationOfEquinoxes), math);

	/// <summary>
	/// The nutation step, true of date to mean of date: <c>Nᵀ</c>, where
	/// <c>N = R1(−ε)·R3(−Δψ)·R1(ε̄)</c> takes mean to true.
	/// </summary>
	/// <param name="angles">The reduction's angles.</param>
	/// <param name="math">The transcendental functions.</param>
	/// <returns>The rotation.</returns>
	internal static GcrfRotation<T> TrueOfDateToMeanOfDate(PrecessionNutationAngles angles, IStorageMath<T> math)
	{
		GcrfRotation<T> meanToTrue = GcrfRotation<T>.AboutX(-Angle(angles.MeanObliquity + angles.DeltaEpsilon), math)
			.Times(GcrfRotation<T>.AboutZ(-Angle(angles.DeltaPsi), math), math)
			.Times(GcrfRotation<T>.AboutX(Angle(angles.MeanObliquity), math), math);
		return meanToTrue.Transposed();
	}

	/// <summary>
	/// The precession step, mean of date to J2000: <c>Pᵀ</c>, where <c>P = R3(−z)·R2(θ)·R3(−ζ)</c>
	/// takes J2000 to mean of date.
	/// </summary>
	/// <param name="angles">The reduction's angles.</param>
	/// <param name="math">The transcendental functions.</param>
	/// <returns>The rotation.</returns>
	internal static GcrfRotation<T> MeanOfDateToGcrf(PrecessionNutationAngles angles, IStorageMath<T> math)
	{
		GcrfRotation<T> j2000ToMean = GcrfRotation<T>.AboutZ(-Angle(angles.Z), math)
			.Times(GcrfRotation<T>.AboutY(Angle(angles.Theta), math), math)
			.Times(GcrfRotation<T>.AboutZ(-Angle(angles.Zeta), math), math);
		return j2000ToMean.Transposed();
	}

	/// <summary>The whole reduction, TEME to GCRF, as one matrix.</summary>
	/// <param name="angles">The reduction's angles.</param>
	/// <param name="math">The transcendental functions.</param>
	/// <returns>The rotation.</returns>
	internal static GcrfRotation<T> TemeToGcrf(PrecessionNutationAngles angles, IStorageMath<T> math) =>
		MeanOfDateToGcrf(angles, math)
			.Times(TrueOfDateToMeanOfDate(angles, math), math)
			.Times(TemeToTrueOfDate(angles, math), math);

	/// <summary>Converts an angle evaluated in <see langword="double"/> to the storage type.</summary>
	/// <param name="radians">The angle.</param>
	/// <returns>The angle in <typeparamref name="T"/>.</returns>
	private static T Angle(double radians) => T.CreateChecked(radians);
}
