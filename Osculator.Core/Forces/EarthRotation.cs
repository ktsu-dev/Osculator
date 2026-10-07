// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Forces;

using System;
using System.Numerics;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Time;

/// <summary>
/// The Earth's orientation about its axis during an integration: the sidereal angle at the epoch
/// and the rate it turns at.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="AngleAtEpochRadians">Greenwich mean sidereal time at the integration's epoch, in radians.</param>
/// <param name="RateRadiansPerSecond">The rotation rate, in radians per second.</param>
/// <remarks>
/// <para>
/// A rotation about z and nothing else, which is exactly the TEME → PEF step of
/// <see cref="EarthFixedFrame{T}"/>. So the Earth-fixed forces — the harmonics, and the co-rotating
/// atmosphere drag sees — are oriented correctly when the integration runs in TEME, the frame an
/// SGP4 state arrives in. Polar motion is left out: it tilts the field by about a third of an
/// arcsecond, which moves a LEO acceleration by parts in 10¹¹. Integrating in GCRF needs
/// precession and nutation as well, which wait on the TEME → GCRF transform.
/// </para>
/// </remarks>
public readonly record struct EarthRotation<T>(T AngleAtEpochRadians, T RateRadiansPerSecond)
	where T : struct, INumber<T>
{
	/// <summary>The rotation at an instant, from the same sidereal time the frame layer uses.</summary>
	/// <param name="epoch">The integration's epoch, on the UTC scale.</param>
	/// <param name="ut1MinusUtcSeconds">UT1 − UTC at the epoch, in seconds.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The rotation.</returns>
	public static EarthRotation<T> At(JulianDate epoch, double ut1MinusUtcSeconds, IStorageMath<T> math) =>
		new(EarthFixedFrame<T>.SiderealAngle(epoch, ut1MinusUtcSeconds, math), EarthFixedFrame<T>.RotationRateRadiansPerSecond);

	/// <summary>The angle at a time, reduced to [0, 2π).</summary>
	/// <param name="secondsSinceEpoch">The time, in seconds since the epoch.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The angle, in radians.</returns>
	/// <remarks>
	/// The whole turns are removed in <typeparamref name="T"/> so the sine and cosine see a small
	/// argument. That does not recover what the sum itself rounded: thirty days is 189 radians of
	/// rotation, where a <see langword="float"/> resolves 1.5e-5 rad, about 100 m at the surface.
	/// That is the storage type's own arithmetic error, which is what this repository measures, so it
	/// is left in rather than hidden by computing the angle in <see langword="double"/>. The count of
	/// turns is an integer, so finding it in <see langword="double"/> loses nothing.
	/// </remarks>
	public T AngleAt(T secondsSinceEpoch, IStorageMath<T> math)
	{
		Ensure.NotNull(math);
		T angle = AngleAtEpochRadians + (RateRadiansPerSecond * secondsSinceEpoch);
		T fullTurn = math.Pi + math.Pi;
		double turns = Math.Floor(double.CreateChecked(angle) / double.CreateChecked(fullTurn));
		return math.ToWorkingPrecision(angle - (T.CreateChecked(turns) * fullTurn));
	}
}
