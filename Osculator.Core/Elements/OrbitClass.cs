// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Elements;

/// <summary>
/// The conventional region of near-Earth space an orbit belongs to.
/// </summary>
/// <remarks>
/// <see cref="OrbitGeometry.Classify(double, double)"/> says exactly where each boundary falls.
/// </remarks>
public enum OrbitClass
{
	/// <summary>The element set does not describe a closed orbit, so it has no class.</summary>
	Unclassified,

	/// <summary>Low Earth orbit: the whole orbit lies below 2,000 km altitude.</summary>
	Leo,

	/// <summary>Medium Earth orbit: above low Earth orbit and faster than geosynchronous.</summary>
	Meo,

	/// <summary>Geosynchronous: one revolution per sidereal day, to within one percent.</summary>
	Geo,

	/// <summary>Highly elliptical: eccentricity of at least 0.25, wherever the orbit sits.</summary>
	Heo,

	/// <summary>Slower than geosynchronous and not highly elliptical, such as the graveyard belt.</summary>
	BeyondGeo,
}
