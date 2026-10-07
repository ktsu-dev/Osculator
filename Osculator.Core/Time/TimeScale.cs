// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Time;

using System.Numerics;

/// <summary>
/// The time scale an instant is expressed on.
/// </summary>
/// <remarks>
/// <para>
/// Four scales, because the rest of the application needs four. Element sets are stamped in
/// <see cref="Utc"/>; the Earth's rotation angle is a function of <see cref="Ut1"/>; precession,
/// nutation and the force model are parameterized in <see cref="Tt"/>; and <see cref="Tai"/> is the
/// continuous atomic count the other two atomic-derived scales are offsets of.
/// </para>
/// <para>
/// TDB, which ephemerides are tabulated in, differs from TT by under two milliseconds periodically
/// and is not modelled here.
/// </para>
/// </remarks>
public enum TimeScale
{
	/// <summary>Coordinated Universal Time: atomic seconds, held within 0.9 s of UT1 by leap seconds.</summary>
	Utc,

	/// <summary>International Atomic Time: the continuous count of SI seconds.</summary>
	Tai,

	/// <summary>Terrestrial Time: TAI + 32.184 s exactly, the continuation of ephemeris time.</summary>
	Tt,

	/// <summary>Universal Time: the Earth's actual rotation angle, read as a time.</summary>
	Ut1,
}

/// <summary>
/// The fixed relations between the time scales, in whatever storage type they are wanted in.
/// </summary>
public static class TimeScales
{
	/// <summary>The number of SI seconds in a Julian day on every scale here.</summary>
	public const int SecondsPerDay = 86400;

	/// <summary>The offset between a Julian date and a modified Julian date, in days.</summary>
	public const double ModifiedJulianDayOffset = 2400000.5;

	/// <summary>
	/// Gets TT − TAI, which is 32.184 s by definition rather than by measurement.
	/// </summary>
	/// <typeparam name="T">The storage type.</typeparam>
	/// <returns>32.184 seconds.</returns>
	/// <remarks>
	/// Built as 32184 / 1000 rather than parsed or converted from a <see langword="double"/>: the
	/// quotient terminates, so it is exact in <see langword="decimal"/> and in an arbitrary-precision
	/// type, and in <see langword="double"/> it is the correctly rounded quotient, which is the
	/// nearest representable value to 32.184.
	/// </remarks>
	public static T TtMinusTaiSeconds<T>()
		where T : struct, INumber<T>
		=> T.CreateChecked(32184) / T.CreateChecked(1000);

	/// <summary>Converts a number of seconds to days in the storage type.</summary>
	/// <typeparam name="T">The storage type.</typeparam>
	/// <param name="seconds">The seconds.</param>
	/// <returns>The days.</returns>
	/// <remarks>
	/// One SI second is 1/86400 of a day, which does not terminate in decimal, so this is the one
	/// place an instant's arithmetic rounds in an arbitrary-precision type. It is always computed the
	/// same way for the same offset, so adding an offset and then subtracting it is exact wherever
	/// addition is.
	/// </remarks>
	internal static T SecondsToDays<T>(T seconds)
		where T : struct, INumber<T>
		=> seconds / T.CreateChecked(SecondsPerDay);
}
