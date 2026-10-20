// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Slr;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Residuals;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Data.Cddis;
using ktsu.Osculator.Data.Iers;
using ktsu.Osculator.Data.SpaceTrack;

/// <summary>
/// What the M4 gate measured: which element set was tested, against which orbit, and how wrong it was.
/// </summary>
/// <param name="Elements">The element set tested.</param>
/// <param name="TruthSource">The orbit file the truth came from.</param>
/// <param name="Residuals">SGP4 minus truth at every epoch of the day, in the truth's RSW frame.</param>
public sealed record SlrGateResult(ElementSet Elements, string TruthSource, IReadOnlyList<TruthResidual<double>> Residuals);

/// <summary>
/// The M4 gate: a public LAGEOS-1 element set propagated with SGP4 and compared against the ILRS
/// precise orbit, reported as radial, along-track and cross-track residuals in kilometres.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why LAGEOS-1.</strong> It is a passive sphere of retroreflectors at 5,900 km, so it has
/// almost no drag, no manoeuvres and no attitude, and its orbit is known from laser ranging to a
/// centimetre. Every kilometre between it and an SGP4 prediction is therefore the element set and
/// the model, not the truth.
/// </para>
/// <para>
/// <strong>The element set is the latest one fitted before the day starts.</strong> That is the set a
/// user tracking the satellite would have had, and it makes every residual a prediction rather than
/// an interpolation within the arc the set was fitted to. Space-Track's history is read for the week
/// before; a satellite that went a week without a new set is refused rather than tested with a stale
/// one.
/// </para>
/// <para>
/// <strong>Everything is read through the cached clients.</strong> Space-Track's rate limits and
/// CDDIS's login are the clients' business, and a day that has been run once runs again offline: the
/// orbit file is immutable and cached at any age, and the history and the bulletin come back from
/// their caches. No credential passes through this class.
/// </para>
/// </remarks>
public static class LageosGate
{
	/// <summary>LAGEOS-1's NORAD catalogue number.</summary>
	public const int NoradCatalogId = 8820;

	/// <summary>LAGEOS-1's name in the CDDIS archive.</summary>
	public const string ArchiveName = "lageos1";

	/// <summary>How far back the history is read for an element set.</summary>
	public static readonly TimeSpan Lookback = TimeSpan.FromDays(7);

	/// <summary>
	/// Runs the gate for one UTC day.
	/// </summary>
	/// <param name="spaceTrack">The Space-Track client, for the element set history.</param>
	/// <param name="cddis">The CDDIS client, for the ILRS orbit.</param>
	/// <param name="orientation">The IERS bulletin covering the day.</param>
	/// <param name="day">The UTC day to compare over.</param>
	/// <param name="cancellationToken">Cancels the requests.</param>
	/// <returns>The residuals at every epoch of the orbit inside the day.</returns>
	/// <exception cref="InvalidOperationException">No element set was fitted in the week before the day, or the orbit has no epoch in it.</exception>
	/// <exception cref="CddisException">The orbit could not be fetched and nothing usable is cached.</exception>
	/// <exception cref="SpaceTrackException">The history could not be fetched and nothing usable is cached.</exception>
	public static async Task<SlrGateResult> RunAsync(
		SpaceTrackClient spaceTrack,
		CddisClient cddis,
		EarthOrientationTable orientation,
		DateOnly day,
		CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(spaceTrack);
		Ensure.NotNull(cddis);
		Ensure.NotNull(orientation);

		IlrsOrbit orbit = await cddis.GetOrbitAsync(ArchiveName, day, cancellationToken: cancellationToken).ConfigureAwait(false);

		if (orbit.Orbit.Satellites.Count != 1)
		{
			throw new InvalidOperationException(
				$"The orbit {orbit.Source.Name} declares {orbit.Orbit.Satellites.Count} satellites; an ILRS orbit declares one.");
		}

		IReadOnlyList<TruthSample<double>> truth = WithinDay(
			SlrTruth.FromSp3(orbit.Orbit, orbit.Orbit.Satellites[0], DoubleStorageMath.Instance),
			day);

		IReadOnlyList<ElementSet> history = await spaceTrack.GetHistoryAsync(
			NoradCatalogId,
			day.AddDays(-(int)Lookback.TotalDays),
			day.AddDays(1),
			cancellationToken).ConfigureAwait(false);

		ElementSet elements = LatestBefore(history, truth[0].Instant)
			?? throw new InvalidOperationException(
				$"Space-Track has no LAGEOS-1 element set in the {Lookback.TotalDays:F0} days before {day:yyyy-MM-dd}.");

		return new SlrGateResult(
			elements,
			orbit.Source.Name,
			TruthComparison<double>.Compare(elements, truth, orientation.At, DoubleStorageMath.Instance));
	}

	/// <summary>
	/// The element set a user would have held at an instant: the latest whose epoch is at or before it.
	/// </summary>
	/// <param name="history">Element sets, in any order.</param>
	/// <param name="instant">The instant, on the UTC scale.</param>
	/// <returns>The set, or null if every set is later.</returns>
	public static ElementSet? LatestBefore(IReadOnlyList<ElementSet> history, JulianDate instant)
	{
		Ensure.NotNull(history);

		ElementSet? latest = null;
		double latestMinutes = double.NegativeInfinity;

		foreach (ElementSet candidate in history)
		{
			// Minutes from the instant to the epoch: zero or negative means fitted by then.
			double minutes = TruthComparison<double>.MinutesBetween(instant, candidate.EpochJulianDate);

			if (minutes <= 0.0 && minutes > latestMinutes)
			{
				latest = candidate;
				latestMinutes = minutes;
			}
		}

		return latest;
	}

	/// <summary>
	/// The samples whose instants fall on one UTC day.
	/// </summary>
	/// <typeparam name="T">The numeric storage type.</typeparam>
	/// <param name="truth">The samples.</param>
	/// <param name="day">The day.</param>
	/// <returns>The samples from midnight inclusive to the next midnight exclusive.</returns>
	/// <exception cref="InvalidOperationException">No sample falls on the day.</exception>
	public static IReadOnlyList<TruthSample<T>> WithinDay<T>(IReadOnlyList<TruthSample<T>> truth, DateOnly day)
		where T : struct, System.Numerics.INumber<T>
	{
		Ensure.NotNull(truth);

		JulianDate start = JulianDate.FromCalendar(day.Year, day.Month, day.Day, 0, 0, 0.0);
		List<TruthSample<T>> inside = [];

		foreach (TruthSample<T> sample in truth)
		{
			double minutes = TruthComparison<T>.MinutesBetween(start, sample.Instant);

			if (minutes is >= 0.0 and < 1440.0)
			{
				inside.Add(sample);
			}
		}

		return inside.Count > 0
			? inside
			: throw new InvalidOperationException($"The orbit has no epoch on {day:yyyy-MM-dd}.");
	}
}
