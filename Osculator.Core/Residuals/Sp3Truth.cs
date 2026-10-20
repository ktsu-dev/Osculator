// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Residuals;

using System;
using System.Collections.Generic;
using System.Numerics;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;

/// <summary>
/// A precise orbit read as truth between its tabulated epochs: an SP3 file's positions and velocities,
/// interpolated and rotated into TEME.
/// </summary>
/// <typeparam name="T">The arithmetic, which for a decomposition is the reference.</typeparam>
/// <remarks>
/// <para>
/// <strong>Positions and velocities are interpolated separately</strong>, each with the same
/// tenth-order Lagrange polynomial <see cref="Sp3Interpolator{T}"/> uses, rather than the velocity
/// being taken as the derivative of the position polynomial. The file's velocities are an
/// independent product of the same orbit fit; differentiating the position polynomial would trade
/// them for a derivative that is least accurate exactly where the window has to slide, at the ends.
/// A file without velocities is refused, because a residual's RIC frame and its rate components both
/// need one.
/// </para>
/// <para>
/// <strong>The table is on the UTC scale.</strong> An ILRS laser-ranging orbit is written in UTC; an
/// IGS GNSS orbit is written in GPS time, which runs 18 s ahead of UTC since 2017, and has to be
/// re-timed before it is given here. Nothing in the numbers would show the mistake: 18 s is 140 km
/// of along-track motion at LEO speed, and it would all be reported as model error.
/// </para>
/// </remarks>
public sealed class Sp3Truth<T>
	where T : struct, INumber<T>
{
	private readonly Sp3Interpolator<T> positions;
	private readonly Sp3Interpolator<T> velocities;
	private readonly IStorageMath<T> math;

	/// <summary>
	/// Initializes a new instance of the <see cref="Sp3Truth{T}"/> class.
	/// </summary>
	/// <param name="start">The instant the table's times count from, on the UTC scale.</param>
	/// <param name="positions">The ITRF positions, in kilometres, timed in seconds after <paramref name="start"/>.</param>
	/// <param name="velocities">
	/// The ITRF velocities, in km/s, at the same times as <paramref name="positions"/>. Carried in a
	/// <see cref="TabulatedPosition{T}"/> because that is the shape the interpolator reads; its three
	/// components are the velocity's.
	/// </param>
	/// <param name="math">The storage type's functions.</param>
	/// <exception cref="ArgumentException">
	/// There are no velocities, or they are not tabulated at the same times as the positions.
	/// </exception>
	public Sp3Truth(JulianDate start, IReadOnlyList<TabulatedPosition<T>> positions, IReadOnlyList<TabulatedPosition<T>> velocities, IStorageMath<T> math)
	{
		Ensure.NotNull(positions);
		Ensure.NotNull(velocities);
		Ensure.NotNull(math);

		if (velocities.Count == 0)
		{
			throw new ArgumentException(
				"A truth needs velocities: the RIC frame and every rate component are built from one. This orbit has none.",
				nameof(velocities));
		}

		if (velocities.Count != positions.Count)
		{
			throw new ArgumentException(
				$"The table has {positions.Count} positions and {velocities.Count} velocities; each epoch needs both.",
				nameof(velocities));
		}

		for (int i = 0; i < positions.Count; i++)
		{
			if (positions[i].Seconds != velocities[i].Seconds)
			{
				throw new ArgumentException(
					$"Velocity {i} is at {velocities[i].Seconds} s and its position at {positions[i].Seconds} s.",
					nameof(velocities));
			}
		}

		Start = start;
		this.positions = new Sp3Interpolator<T>(positions, math);
		this.velocities = new Sp3Interpolator<T>(velocities, math);
		this.math = math;
	}

	/// <summary>Gets the instant the table's times count from, on the UTC scale.</summary>
	public JulianDate Start { get; }

	/// <summary>Gets the time of the first tabulated epoch, in seconds after <see cref="Start"/>.</summary>
	public T FirstSeconds => positions.FirstSeconds;

	/// <summary>Gets the time of the last tabulated epoch, in seconds after <see cref="Start"/>.</summary>
	public T LastSeconds => positions.LastSeconds;

	/// <summary>
	/// Interpolates the orbit at an instant and rotates it into TEME.
	/// </summary>
	/// <param name="seconds">The instant, in seconds after <see cref="Start"/>.</param>
	/// <param name="orientation">The Earth's orientation at that instant, with its sigmas.</param>
	/// <returns>The truth at that instant.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The instant is outside the table.</exception>
	public TruthState<T> At(T seconds, EarthOrientation orientation)
	{
		TabulatedPosition<T> r = positions.At(seconds);
		TabulatedPosition<T> v = velocities.At(seconds);

		// The instant is formed from the start's parts and the offset in days, so the large whole day
		// never shares a double with the offset (domain trap 4).
		JulianDate epoch = new(Start.Day, Start.DayFraction + (double.CreateChecked(seconds) / TimeScales.SecondsPerDay));

		return TruthState<T>.FromItrf(epoch, new ItrfState<T>(r.X, r.Y, r.Z, v.X, v.Y, v.Z), orientation, math);
	}
}
