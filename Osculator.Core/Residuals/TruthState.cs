// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Residuals;

using System;
using System.Numerics;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;

/// <summary>
/// Where a truth state came from, which decides what it carries into the decomposition besides the
/// state itself.
/// </summary>
public enum TruthSource
{
	/// <summary>
	/// A later element set evaluated at its own epoch. Not the orbit: another fit of the same model,
	/// so Δ_model against it includes both sets' fit residuals.
	/// </summary>
	ElementSet,

	/// <summary>
	/// A state already in TEME, taken as given. What a test uses to build truth from SGP4's own
	/// output, where Δ_model has to come back as zero.
	/// </summary>
	Teme,

	/// <summary>
	/// A precise orbit tabulated in the ITRF, such as an IGS or ILRS SP3 file. Earth-fixed, so the
	/// IERS's uncertainty in the Earth's orientation is part of what it carries.
	/// </summary>
	EarthFixed,

	/// <summary>
	/// A state in the GCRF or ICRF, such as a JPL Horizons spacecraft vector. Inertial, so the Earth's
	/// orientation plays no part in it.
	/// </summary>
	Celestial,
}

/// <summary>
/// One state taken as truth for a decomposition, already in TEME, with what was needed to put it
/// there.
/// </summary>
/// <typeparam name="T">The arithmetic the state is held in: the decomposition's reference.</typeparam>
/// <param name="Source">Where the state came from.</param>
/// <param name="Epoch">The instant the state is for, on the UTC scale SGP4's epochs are on.</param>
/// <param name="EpochJulianDate">The same instant, as a two-part Julian date on the UTC scale.</param>
/// <param name="State">The state, in TEME, in kilometres and km/s.</param>
/// <param name="Orientation">
/// The Earth orientation the state was rotated with. Meaningful only for an
/// <see cref="TruthSource.EarthFixed"/> truth, whose rotation into TEME depends on it; for every
/// other source it is <see cref="EarthOrientation.Ignored"/> and reads nothing.
/// </param>
/// <param name="ElementSet">The element set it was evaluated from, for an <see cref="TruthSource.ElementSet"/> truth.</param>
/// <remarks>
/// <para>
/// <strong>Every truth is rotated into TEME before it is compared, rather than the prediction out of
/// it.</strong> TEME is where the prediction is born and where the arithmetic terms are measured, and
/// the residual is resolved in the truth's own RIC frame either way: the RSW basis is built from the
/// truth's position and velocity, which a rotation carries along with it. So the choice changes no
/// component of Δ_model, and taking it this way keeps the four storage types' predictions free of a
/// second transform each.
/// </para>
/// <para>
/// <strong>The rotation is done in <typeparamref name="T"/></strong>, with its angles evaluated in
/// <see langword="double"/> as everywhere else in the frame layer. Its rounding is therefore shared by
/// every term and cancels from Δ_arith; what it can add to Δ_model is the frame layer's own accuracy,
/// measured by gate 3 at 6e-8 km.
/// </para>
/// </remarks>
public sealed record TruthState<T>(
	TruthSource Source,
	DateTime Epoch,
	JulianDate EpochJulianDate,
	TemeState<T> State,
	EarthOrientation Orientation,
	ElementSet? ElementSet = null)
	where T : struct, INumber<T>
{
	/// <summary>The seconds in a day.</summary>
	private const double SecondsPerDay = TimeScales.SecondsPerDay;

	/// <summary>The Julian date of 0001-01-01T00:00Z, where <see cref="DateTime"/> ticks start.</summary>
	private const double DateTimeZeroJulianDay = 1721425.5;

	/// <summary>Gets a value indicating whether the state's frame turned with the Earth, so that its orientation is part of its uncertainty.</summary>
	public bool IsEarthFixed => Source == TruthSource.EarthFixed;

	/// <summary>
	/// Takes a TEME state as truth, as it stands.
	/// </summary>
	/// <param name="epoch">The instant, on the UTC scale.</param>
	/// <param name="state">The state, in TEME.</param>
	/// <returns>The truth.</returns>
	public static TruthState<T> FromTeme(JulianDate epoch, TemeState<T> state) =>
		new(TruthSource.Teme, ToDateTime(epoch), epoch, state, EarthOrientation.Ignored);

	/// <summary>
	/// Evaluates an element set at its own epoch and takes that as truth.
	/// </summary>
	/// <param name="elements">The element set.</param>
	/// <param name="math">The arithmetic to evaluate it in.</param>
	/// <returns>The truth, or the error SGP4 refused it with.</returns>
	public static (TruthState<T>? Truth, Sgp4Error Error) FromElementSet(ElementSet elements, IStorageMath<T> math)
	{
		Ensure.NotNull(elements);
		Ensure.NotNull(math);

		Sgp4Result<T> result = Sgp4<T>.Propagate(Sgp4<T>.Initialize(elements, math), T.Zero, math);

		return result.IsSuccess
			? (new TruthState<T>(TruthSource.ElementSet, elements.Epoch, elements.EpochJulianDate, result.State, EarthOrientation.Ignored, elements), Sgp4Error.None)
			: (null, result.Error);
	}

	/// <summary>
	/// Rotates an Earth-fixed state, such as an SP3 orbit's, into TEME and takes it as truth.
	/// </summary>
	/// <param name="epoch">The instant, on the UTC scale. An SP3 file in GPS time has to be converted first.</param>
	/// <param name="state">The state in the ITRF, in kilometres and km/s, with its velocity relative to the rotating Earth as SP3 writes it.</param>
	/// <param name="orientation">
	/// The Earth's orientation at that instant, from an IERS bulletin, with its sigmas: they become the
	/// orientation share of Δ_data. <see cref="EarthOrientation.Ignored"/> is accepted and costs up to
	/// 415 m of rotation, which would then be reported as model error.
	/// </param>
	/// <param name="math">The transcendental functions.</param>
	/// <returns>The truth.</returns>
	public static TruthState<T> FromItrf(JulianDate epoch, ItrfState<T> state, EarthOrientation orientation, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		PefState<T> pef = EarthFixedFrame<T>.ItrfToPef(state, orientation, math);
		TemeState<T> teme = EarthFixedFrame<T>.PefToTeme(pef, epoch, orientation, math);

		return new TruthState<T>(TruthSource.EarthFixed, ToDateTime(epoch), epoch, teme, orientation);
	}

	/// <summary>
	/// Rotates a celestial state, such as a Horizons spacecraft vector, into TEME and takes it as truth.
	/// </summary>
	/// <param name="terrestrialTime">
	/// The instant, on the TT scale. A Horizons table's TDB epoch is accepted here as it stands: the two
	/// scales differ periodically by under 1.7 ms, which is 13 m of along-track motion at LEO speed
	/// and is reported as model error. Convert first if that matters.
	/// </param>
	/// <param name="state">The state in the GCRF or ICRF, in kilometres and km/s, geocentric.</param>
	/// <param name="offsets">The IERS celestial pole offsets, or <see cref="CelestialPoleOffsets.Ignored"/>.</param>
	/// <param name="leapSeconds">The leap-second table, to put the instant on the UTC scale.</param>
	/// <param name="math">The transcendental functions.</param>
	/// <returns>The truth.</returns>
	/// <remarks>
	/// The ICRF and the GCRF share their axes to within tens of milliarcseconds and a geocentric
	/// Horizons vector is already in the GCRF's origin, so no transform is applied between them.
	/// </remarks>
	public static TruthState<T> FromGcrf(
		JulianDate terrestrialTime,
		GcrfState<T> state,
		CelestialPoleOffsets offsets,
		LeapSeconds leapSeconds,
		IStorageMath<T> math)
	{
		Ensure.NotNull(leapSeconds);
		Ensure.NotNull(math);

		TemeState<T> teme = GcrfFrame<T>.ToTeme(state, terrestrialTime, offsets, math);

		// TT − UTC = (TAI − UTC) + 32.184 s. The leap-second lookup is by TAI, which is TT less the
		// fixed 32.184 s, so the conversion goes through it rather than guessing at UTC first.
		double ttMinusTai = TimeScales.TtMinusTaiSeconds<double>();
		double taiModifiedJulianDay = terrestrialTime.Day - TimeScales.ModifiedJulianDayOffset
			+ terrestrialTime.DayFraction - (ttMinusTai / SecondsPerDay);
		int taiMinusUtc = leapSeconds.TaiMinusUtcAtTai(taiModifiedJulianDay);
		JulianDate utc = new(terrestrialTime.Day, terrestrialTime.DayFraction - ((taiMinusUtc + ttMinusTai) / SecondsPerDay));

		return new TruthState<T>(TruthSource.Celestial, ToDateTime(utc), utc, teme, EarthOrientation.Ignored);
	}

	/// <summary>Converts a UTC Julian date to the <see cref="DateTime"/> it names, to the tick.</summary>
	/// <param name="epoch">The instant.</param>
	/// <returns>The instant as a <see cref="DateTime"/>, of kind <see cref="DateTimeKind.Utc"/>.</returns>
	/// <remarks>
	/// For display and for ordering only. The decomposition's horizon is computed from the two-part
	/// Julian date, never from this (domain trap 8).
	/// </remarks>
	private static DateTime ToDateTime(JulianDate epoch)
	{
		double days = epoch.Day - DateTimeZeroJulianDay + epoch.DayFraction;
		return new DateTime((long)Math.Round(days * TimeSpan.TicksPerDay), DateTimeKind.Utc);
	}
}
