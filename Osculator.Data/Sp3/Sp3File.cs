// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Sp3;

using System;
using System.Collections.Generic;
using System.Numerics;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;

/// <summary>
/// A parsed SP3-c precise orbit file: the header, and every satellite's entries in epoch order.
/// </summary>
/// <remarks>
/// Produced by <see cref="Sp3Parser.Parse"/>. Entries the file marks as missing — a position of
/// exactly zero on all three axes, which is how SP3 writes "no orbit for this satellite at this
/// epoch" — are left out of <see cref="RecordsOf"/> rather than handed on as a satellite sitting
/// at the centre of the Earth.
/// </remarks>
public sealed class Sp3File
{
	private readonly Dictionary<string, List<Sp3Record>> records;

	internal Sp3File(
		Sp3Header header,
		IReadOnlyList<string> satellites,
		IReadOnlyList<JulianDate> epochs,
		Dictionary<string, List<Sp3Record>> records)
	{
		Version = header.Version;
		HasVelocities = header.HasVelocities;
		Start = header.Start;
		EpochIntervalSeconds = header.EpochIntervalSeconds;
		CoordinateSystem = header.CoordinateSystem;
		OrbitType = header.OrbitType;
		Agency = header.Agency;
		TimeSystem = header.TimeSystem;
		Satellites = satellites;
		Epochs = epochs;
		this.records = records;
	}

	/// <summary>Gets the format version letter, which this parser only accepts as <c>c</c>.</summary>
	public char Version { get; }

	/// <summary>Gets a value indicating whether every position is followed by a velocity record.</summary>
	public bool HasVelocities { get; }

	/// <summary>Gets the file's start time, which <see cref="Sp3Record.SecondsSinceStart"/> counts from.</summary>
	public JulianDate Start { get; }

	/// <summary>Gets the nominal spacing between epochs, in seconds, as the header states it.</summary>
	public double EpochIntervalSeconds { get; }

	/// <summary>Gets the reference frame the positions are in, such as <c>IGS05</c> or <c>ITRF14</c>.</summary>
	/// <remarks>
	/// IGS and ILRS orbits are Earth-fixed, so their positions compare against an
	/// <see cref="Core.Frames.ItrfState{T}"/> and not against SGP4's TEME output directly.
	/// </remarks>
	public string CoordinateSystem { get; }

	/// <summary>Gets the orbit type code, such as <c>HLM</c> or <c>FIT</c>.</summary>
	public string OrbitType { get; }

	/// <summary>Gets the agency that produced the file.</summary>
	public string Agency { get; }

	/// <summary>Gets the time scale the epochs are on, such as <c>GPS</c> or <c>UTC</c>.</summary>
	/// <remarks>
	/// GPS time runs ahead of UTC by the leap seconds since 1980 — eighteen at present, which is
	/// some 70 km of a LEO orbit — so this is not a detail to drop on the way to a comparison.
	/// </remarks>
	public string TimeSystem { get; }

	/// <summary>Gets the satellite identifiers the header declares, in header order.</summary>
	public IReadOnlyList<string> Satellites { get; }

	/// <summary>Gets every epoch in the file, in order.</summary>
	public IReadOnlyList<JulianDate> Epochs { get; }

	/// <summary>
	/// The entries for one satellite, in epoch order, without the epochs the file marks as missing.
	/// </summary>
	/// <param name="satellite">The identifier, as the header writes it, such as <c>G05</c> or <c>L51</c>.</param>
	/// <returns>The entries.</returns>
	/// <exception cref="ArgumentException">The header does not declare that satellite.</exception>
	public IReadOnlyList<Sp3Record> RecordsOf(string satellite)
	{
		Ensure.NotNull(satellite);

		return records.TryGetValue(satellite, out List<Sp3Record>? found)
			? found
			: throw new ArgumentException($"The file declares no satellite \"{satellite}\".", nameof(satellite));
	}

	/// <summary>
	/// One satellite's positions as the table an <see cref="Sp3Interpolator{T}"/> reads.
	/// </summary>
	/// <typeparam name="T">The numeric storage type.</typeparam>
	/// <param name="satellite">The identifier.</param>
	/// <returns>Its positions, timed in seconds after <see cref="Start"/>.</returns>
	/// <remarks>
	/// The values pass through <see langword="double"/> on the way in, which costs nothing: the file
	/// writes six decimals of a five-digit number of kilometres, eleven significant digits that a
	/// <see langword="double"/> holds exactly enough to round-trip, and every conversion into the
	/// wider types reads that round-trip text back.
	/// </remarks>
	public IReadOnlyList<TabulatedPosition<T>> Tabulate<T>(string satellite)
		where T : struct, INumber<T>
	{
		IReadOnlyList<Sp3Record> source = RecordsOf(satellite);
		TabulatedPosition<T>[] table = new TabulatedPosition<T>[source.Count];

		for (int i = 0; i < source.Count; i++)
		{
			Sp3Record r = source[i];
			table[i] = new TabulatedPosition<T>(
				T.CreateChecked(r.SecondsSinceStart),
				T.CreateChecked(r.X),
				T.CreateChecked(r.Y),
				T.CreateChecked(r.Z));
		}

		return table;
	}
}
