// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.SpaceTrack;

using System;

/// <summary>
/// One public conjunction data message from Space-Track's <c>cdm_public</c> class: a predicted close
/// approach between two catalogued objects.
/// </summary>
/// <param name="Id">Space-Track's identifier for the message.</param>
/// <param name="Created">When the message was produced, in UTC.</param>
/// <param name="TimeOfClosestApproach">The predicted time of closest approach, in UTC.</param>
/// <param name="MissDistanceKm">The predicted miss distance at that time, in kilometres.</param>
/// <param name="CollisionProbability">The probability of collision, or null when none was published.</param>
/// <param name="EmergencyReportable">Whether the event met the emergency reporting criteria.</param>
/// <param name="Primary">The first object's NORAD catalogue number.</param>
/// <param name="PrimaryName">The first object's name, or null.</param>
/// <param name="Secondary">The second object's NORAD catalogue number.</param>
/// <param name="SecondaryName">The second object's name, or null.</param>
/// <remarks>
/// Space-Track publishes the miss distance in metres; it is held in kilometres here because every
/// other distance in this application is, the conjunction screen's included, so a cross-check never
/// needs a factor of a thousand at the call site.
/// </remarks>
public sealed record ConjunctionDataMessage(
	string Id,
	DateTimeOffset Created,
	DateTimeOffset TimeOfClosestApproach,
	double MissDistanceKm,
	double? CollisionProbability,
	bool EmergencyReportable,
	int Primary,
	string? PrimaryName,
	int Secondary,
	string? SecondaryName)
{
	/// <summary>Whether an object is either party to the approach.</summary>
	/// <param name="noradCatalogId">The NORAD catalogue number.</param>
	/// <returns>True when it is the primary or the secondary.</returns>
	public bool Involves(int noradCatalogId) => Primary == noradCatalogId || Secondary == noradCatalogId;
}
