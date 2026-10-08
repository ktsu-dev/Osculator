// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Sp3;

using ktsu.Osculator.Core.Time;

/// <summary>The fields of an SP3 header the parser carries through to the parsed file.</summary>
/// <param name="Version">The format version letter.</param>
/// <param name="HasVelocities">Whether velocity records follow the positions.</param>
/// <param name="Start">The start time.</param>
/// <param name="EpochCount">How many epochs the header says follow.</param>
/// <param name="EpochIntervalSeconds">The nominal epoch spacing.</param>
/// <param name="CoordinateSystem">The reference frame.</param>
/// <param name="OrbitType">The orbit type code.</param>
/// <param name="Agency">The producing agency.</param>
/// <param name="TimeSystem">The time scale.</param>
internal sealed record Sp3Header(
	char Version,
	bool HasVelocities,
	JulianDate Start,
	int EpochCount,
	double EpochIntervalSeconds,
	string CoordinateSystem,
	string OrbitType,
	string Agency,
	string TimeSystem);
