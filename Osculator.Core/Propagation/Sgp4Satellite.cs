// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System.Numerics;

/// <summary>
/// An element set with the SGP4 initialization already done.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// Initialization derives some forty coefficients from the seven orbital elements, and none of them
/// depend on the time being propagated to. Separating it from propagation is what makes a catalogue
/// sweep affordable: initialize once per object, then propagate to as many epochs as wanted.
/// </para>
/// <para>
/// The coefficients are internal. They are the model's working state rather than an API, they are
/// named after the equations in the published algorithm rather than after anything a caller would
/// recognise, and exposing them would freeze forty implementation details as public surface.
/// </para>
/// </remarks>
public sealed class Sgp4Satellite<T>
	where T : struct, INumber<T>
{
	/// <summary>Gets the NORAD catalogue number of the object this was initialized from.</summary>
	public int NoradCatalogId { get; internal set; }

	/// <summary>
	/// Gets a value indicating whether this element set selects the deep-space model.
	/// </summary>
	/// <remarks>
	/// True when the orbital period is 225 minutes or more, which is the boundary the model uses to
	/// decide that lunar-solar perturbations and Earth resonance terms can no longer be neglected.
	/// Geostationary, Molniya and most navigation satellites fall on this side of it.
	/// </remarks>
	public bool IsDeepSpace { get; internal set; }

	/// <summary>Gets the error, if any, that initialization itself detected.</summary>
	public Sgp4Error InitializationError { get; internal set; }

	// Orbital elements, in radians and radians per minute.
	internal T BStar { get; set; }
	internal T Eccentricity { get; set; }
	internal T Inclination { get; set; }
	internal T ArgumentOfPerigee { get; set; }
	internal T RightAscension { get; set; }
	internal T MeanAnomaly { get; set; }
	internal T MeanMotionKozai { get; set; }

	// Recovered mean motion and semi-major axis.
	internal T MeanMotion { get; set; }
	internal T SemiMajorAxis { get; set; }

	// Near-earth secular and periodic coefficients.
	internal bool IsSimplified { get; set; }
	internal T Aycof { get; set; }
	internal T Con41 { get; set; }
	internal T Cc1 { get; set; }
	internal T Cc4 { get; set; }
	internal T Cc5 { get; set; }
	internal T D2 { get; set; }
	internal T D3 { get; set; }
	internal T D4 { get; set; }
	internal T Delmo { get; set; }
	internal T Eta { get; set; }
	internal T ArgpDot { get; set; }
	internal T Omgcof { get; set; }
	internal T Sinmao { get; set; }
	internal T T2cof { get; set; }
	internal T T3cof { get; set; }
	internal T T4cof { get; set; }
	internal T T5cof { get; set; }
	internal T X1mth2 { get; set; }
	internal T X7thm1 { get; set; }
	internal T MDot { get; set; }
	internal T NodeDot { get; set; }
	internal T Xlcof { get; set; }
	internal T Xmcof { get; set; }
	internal T Nodecf { get; set; }

	// Deep-space secular rates, from the lunar-solar terms.
	internal T Dedt { get; set; }
	internal T Didt { get; set; }
	internal T Dmdt { get; set; }
	internal T Domdt { get; set; }
	internal T Dnodt { get; set; }

	// Deep-space solar periodic coefficients.
	internal T Se2 { get; set; }
	internal T Se3 { get; set; }
	internal T Si2 { get; set; }
	internal T Si3 { get; set; }
	internal T Sl2 { get; set; }
	internal T Sl3 { get; set; }
	internal T Sl4 { get; set; }
	internal T Sgh2 { get; set; }
	internal T Sgh3 { get; set; }
	internal T Sgh4 { get; set; }
	internal T Sh2 { get; set; }
	internal T Sh3 { get; set; }

	// Deep-space lunar periodic coefficients.
	internal T Ee2 { get; set; }
	internal T E3 { get; set; }
	internal T Xi2 { get; set; }
	internal T Xi3 { get; set; }
	internal T Xl2 { get; set; }
	internal T Xl3 { get; set; }
	internal T Xl4 { get; set; }
	internal T Xgh2 { get; set; }
	internal T Xgh3 { get; set; }
	internal T Xgh4 { get; set; }
	internal T Xh2 { get; set; }
	internal T Xh3 { get; set; }

	// Mean longitudes of the moon and the sun at epoch, which drive those periodics.
	internal T Zmol { get; set; }
	internal T Zmos { get; set; }

	// Greenwich sidereal time at epoch, which the resonance terms measure longitude against.
	internal T Gsto { get; set; }

	/// <summary>
	/// Gets which Earth resonance, if any, this orbit is close enough to for the geopotential
	/// tesseral terms to matter: 0 for none, 1 for the one-revolution-per-day synchronous case,
	/// 2 for the half-day case.
	/// </summary>
	internal int Resonance { get; set; }

	// Half-day resonance coefficients, used when Resonance is 2.
	internal T D2201 { get; set; }
	internal T D2211 { get; set; }
	internal T D3210 { get; set; }
	internal T D3222 { get; set; }
	internal T D4410 { get; set; }
	internal T D4422 { get; set; }
	internal T D5220 { get; set; }
	internal T D5232 { get; set; }
	internal T D5421 { get; set; }
	internal T D5433 { get; set; }

	// Synchronous resonance coefficients, used when Resonance is 1.
	internal T Del1 { get; set; }
	internal T Del2 { get; set; }
	internal T Del3 { get; set; }

	// The resonance integrator's starting longitude and its secular rate.
	internal T Xlamo { get; set; }
	internal T Xfact { get; set; }
}
