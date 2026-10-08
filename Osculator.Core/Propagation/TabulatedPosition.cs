// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System.Numerics;

/// <summary>
/// A position read from a table at one instant, such as one epoch of a precise orbit file.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="Seconds">
/// The instant, in seconds after a reference the caller chooses. Every point handed to one
/// interpolator has to share that reference.
/// </param>
/// <param name="X">Position along the table's x axis, in kilometres.</param>
/// <param name="Y">Position along the table's y axis, in kilometres.</param>
/// <param name="Z">Position along the table's z axis, in kilometres.</param>
/// <remarks>
/// <para>
/// The frame is whatever the table was written in, and this type deliberately does not claim one.
/// An SP3 file states its own frame in its header — an ILRS or IGS orbit is Earth-fixed, so its
/// positions are ITRF-family coordinates rather than TEME — and the caller is the one holding that
/// header.
/// </para>
/// <para>
/// Seconds after a nearby reference rather than a Julian date, because the interpolator multiplies
/// time differences together ten at a time. A difference of two Julian dates near 2.46 million has
/// lost eleven of its sixteen digits before it is formed; a difference of two second counts measured
/// from the start of the file has lost none.
/// </para>
/// </remarks>
public readonly record struct TabulatedPosition<T>(T Seconds, T X, T Y, T Z)
	where T : struct, INumber<T>;
