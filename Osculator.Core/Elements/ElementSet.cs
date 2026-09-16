// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Elements;

using System;

/// <summary>
/// A general perturbations element set, as distributed in OMM or two-line element format.
/// </summary>
/// <remarks>
/// <para>
/// These are <em>mean</em> elements in the Brouwer sense, not osculating elements: they are the
/// input SGP4 was fitted to consume, and converting between the two is a step of the model rather
/// than a change of units. Feeding them to a general-purpose Keplerian propagator is a category
/// error that produces plausible, wrong answers.
/// </para>
/// <para>
/// Angles are degrees and <see cref="MeanMotion"/> is revolutions per day, matching the wire
/// format rather than SI. Conversion to SI belongs at the propagator boundary, where the storage
/// type is known; converting here would fix the precision of every field at <see cref="double"/>.
/// </para>
/// </remarks>
public sealed record ElementSet
{
	/// <summary>Gets the object's catalogued name, for example <c>ISS (ZARYA)</c>.</summary>
	public required string ObjectName { get; init; }

	/// <summary>Gets the international designator, for example <c>1998-067A</c>.</summary>
	public required string ObjectId { get; init; }

	/// <summary>Gets the NORAD catalogue number.</summary>
	public required int NoradCatalogId { get; init; }

	/// <summary>Gets the epoch the elements are valid at, in UTC.</summary>
	public required DateTime Epoch { get; init; }

	/// <summary>Gets the mean motion, in revolutions per day.</summary>
	public required double MeanMotion { get; init; }

	/// <summary>Gets the orbital eccentricity, dimensionless.</summary>
	public required double Eccentricity { get; init; }

	/// <summary>Gets the inclination, in degrees.</summary>
	public required double Inclination { get; init; }

	/// <summary>Gets the right ascension of the ascending node, in degrees.</summary>
	public required double RightAscensionOfAscendingNode { get; init; }

	/// <summary>Gets the argument of pericenter, in degrees.</summary>
	public required double ArgumentOfPericenter { get; init; }

	/// <summary>Gets the mean anomaly, in degrees.</summary>
	public required double MeanAnomaly { get; init; }

	/// <summary>Gets the SGP4 drag term, in inverse Earth radii.</summary>
	public required double BStar { get; init; }

	/// <summary>Gets the first derivative of mean motion, in revolutions per day squared.</summary>
	public double MeanMotionDot { get; init; }

	/// <summary>Gets the second derivative of mean motion, in revolutions per day cubed.</summary>
	public double MeanMotionDdot { get; init; }

	/// <summary>Gets the revolution number at epoch.</summary>
	public int RevolutionAtEpoch { get; init; }

	/// <summary>Gets the element set number.</summary>
	public int ElementSetNumber { get; init; }
}
