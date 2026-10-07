// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Horizons;

using System.Collections.Generic;
using ktsu.Osculator.Core.Time;

/// <summary>
/// The time scales Horizons can tabulate vectors in.
/// </summary>
/// <remarks>
/// A label, not a conversion. This repository has no TDB or TT yet, so nothing here turns one into
/// another; what matters now is that a table cannot be read without saying which scale its epochs
/// are in. TDB and TT differ by under two milliseconds, about two metres of the Moon's motion, but
/// UT trails TT by about 69 seconds, which is about seventy kilometres of it. Reading a TDB table as
/// UT is the mistake the label is there to prevent.
/// </remarks>
public enum HorizonsTimeScale
{
	/// <summary>Barycentric Dynamical Time, Horizons' default for vectors.</summary>
	Tdb,

	/// <summary>Terrestrial Time.</summary>
	Tt,

	/// <summary>Universal Time.</summary>
	Ut,
}

/// <summary>
/// One row of a Horizons vector table.
/// </summary>
/// <param name="Epoch">The instant, as a two-part Julian date in the table's time scale.</param>
/// <param name="CalendarDate">The instant as Horizons wrote it, for display.</param>
/// <param name="X">Position along the x axis, in kilometres.</param>
/// <param name="Y">Position along the y axis, in kilometres.</param>
/// <param name="Z">Position along the z axis, in kilometres.</param>
/// <param name="VelocityX">Velocity along the x axis, in kilometres per second.</param>
/// <param name="VelocityY">Velocity along the y axis, in kilometres per second.</param>
/// <param name="VelocityZ">Velocity along the z axis, in kilometres per second.</param>
/// <remarks>
/// <para>
/// The axes are the ICRF's, with its equator as the reference plane. That is <strong>not</strong>
/// TEME, the frame SGP4 answers in: comparing these with a propagated state needs the TEME → GCRF
/// transform, which does not exist yet.
/// </para>
/// <para>
/// The epoch is split from the text rather than parsed as one number, for the same reason
/// <see cref="JulianDate"/> exists: a Julian date in one <see cref="double"/> resolves 48
/// microseconds, and the Moon moves five centimetres in that.
/// </para>
/// </remarks>
public readonly record struct HorizonsStateVector(
	JulianDate Epoch,
	string CalendarDate,
	double X,
	double Y,
	double Z,
	double VelocityX,
	double VelocityY,
	double VelocityZ);

/// <summary>
/// A table of state vectors from Horizons, with what it says about itself.
/// </summary>
/// <param name="TargetName">The target, as Horizons named it, such as <c>Moon (301)</c>.</param>
/// <param name="CenterName">The origin, as Horizons named it, such as <c>Earth (399)</c>.</param>
/// <param name="TimeScale">The scale the epochs are in.</param>
/// <param name="ReferenceFrame">The reference frame Horizons reported, such as <c>ICRF</c>.</param>
/// <param name="Vectors">The rows, in the order Horizons wrote them.</param>
public sealed record HorizonsEphemeris(
	string TargetName,
	string CenterName,
	HorizonsTimeScale TimeScale,
	string ReferenceFrame,
	IReadOnlyList<HorizonsStateVector> Vectors);
