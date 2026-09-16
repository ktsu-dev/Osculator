// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System;
using System.Numerics;
using ktsu.Osculator.Core.Elements;

/// <summary>
/// The SGP4 analytical propagator, generic over the numeric storage type.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// SGP4 is the model element sets are fitted to. It is not the physics — it is a curve fit to a
/// truncated force model, accurate to roughly a kilometre at epoch and degrading by one to three
/// kilometres a day in low Earth orbit. Computing it in more digits does not make it more accurate,
/// and the point of running it over four storage types is to measure how little the arithmetic
/// contributes next to that.
/// </para>
/// <para>
/// Written from the published algorithm — Spacetrack Report #3 (1980) with the corrections in
/// <em>Revisiting Spacetrack Report #3</em> (AIAA 2006-6753) — and verified against that paper's
/// test vectors, which are committed under <c>Osculator.Tests/Data/</c>. Nothing here is derived
/// from a reference implementation; see the attribution note beside the data.
/// </para>
/// <para>
/// <strong>Only the near-earth model is implemented.</strong> An orbital period of 225 minutes or
/// more selects the deep-space model, whose lunar-solar and resonance terms are a separate body of
/// work; those element sets initialize but report
/// <see cref="Sgp4Error.DeepSpaceNotImplemented"/> rather than returning a wrong answer quietly.
/// </para>
/// </remarks>
public static class Sgp4<T>
	where T : struct, INumber<T>
{
	/// <summary>The minutes-per-day figure the model's time unit is defined against.</summary>
	private const double MinutesPerDay = 1440.0;

	/// <summary>The orbital period, in minutes, at or above which the deep-space model applies.</summary>
	private const double DeepSpacePeriodMinutes = 225.0;

	/// <summary>
	/// Performs the one-off initialization an element set needs before it can be propagated.
	/// </summary>
	/// <param name="elements">The element set, in the units the wire format uses.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The initialized satellite, ready for any number of propagations.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="elements"/> or <paramref name="math"/> is null.</exception>
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Maintainability", "CA1502:Avoid excessive complexity",
		Justification = "One published closed-form initialization. Splitting it would scatter forty mutually dependent coefficients across helpers that mean nothing apart, and make it harder to check against the paper it comes from.")]
	public static Sgp4Satellite<T> Initialize(ElementSet elements, IStorageMath<T> math)
	{
		Ensure.NotNull(elements);
		Ensure.NotNull(math);

		T two = N(2);
		T twoPi = math.Pi * two;
		T x2o3 = two / N(3);

		Sgp4Satellite<T> sat = new()
		{
			NoradCatalogId = elements.NoradCatalogId,
			BStar = N(elements.BStar),
			Eccentricity = N(elements.Eccentricity),
			Inclination = Radians(elements.Inclination, math),
			ArgumentOfPerigee = Radians(elements.ArgumentOfPericenter, math),
			RightAscension = Radians(elements.RightAscensionOfAscendingNode, math),
			MeanAnomaly = Radians(elements.MeanAnomaly, math),
			MeanMotionKozai = N(elements.MeanMotion) * twoPi / N(MinutesPerDay),
		};

		T xke = Wgs72<T>.Xke(math);
		T j2 = Wgs72<T>.J2;
		T ecco = sat.Eccentricity;
		T inclo = sat.Inclination;

		// Recover the unperturbed ("un-Kozai'd") mean motion. The element set carries the Kozai
		// mean motion, and feeding that straight in is a classic error worth a few kilometres.
		T eccsq = ecco * ecco;
		T omeosq = T.One - eccsq;
		T rteosq = math.Sqrt(omeosq);
		T cosio = math.Cos(inclo);
		T cosio2 = cosio * cosio;

		T ak = math.Pow(xke / sat.MeanMotionKozai, x2o3);
		T d1 = N(0.75) * j2 * ((N(3) * cosio2) - T.One) / (rteosq * omeosq);
		T del = d1 / (ak * ak);
		T adel = ak * (T.One - (del * del) - (del * ((T.One / N(3)) + (N(134) * del * del / N(81)))));
		del = d1 / (adel * adel);
		sat.MeanMotion = sat.MeanMotionKozai / (T.One + del);

		T ao = math.Pow(xke / sat.MeanMotion, x2o3);
		T sinio = math.Sin(inclo);
		T po = ao * omeosq;
		T con42 = T.One - (N(5) * cosio2);
		sat.Con41 = -con42 - cosio2 - cosio2;
		T posq = po * po;
		T rp = ao * (T.One - ecco);

		sat.SemiMajorAxis = math.Pow(sat.MeanMotion / xke, -x2o3);

		// An orbital period at or beyond the deep-space boundary selects a model this does not have.
		T periodMinutes = twoPi / sat.MeanMotion;
		sat.IsDeepSpace = periodMinutes >= N(DeepSpacePeriodMinutes);

		if (omeosq < T.Zero || sat.MeanMotion < T.Zero)
		{
			sat.InitializationError = Sgp4Error.EccentricityOutOfRange;
			return sat;
		}

		sat.IsSimplified = rp < (N(220) / Wgs72<T>.RadiusEarthKm) + T.One;

		// Atmospheric drag: the model thins its own atmosphere below 156 km and gives up below 98.
		T sfour = (N(78) / Wgs72<T>.RadiusEarthKm) + T.One;
		T qzms24 = Pow4((N(120) - N(78)) / Wgs72<T>.RadiusEarthKm);
		T perigeeKm = (rp - T.One) * Wgs72<T>.RadiusEarthKm;

		if (perigeeKm < N(156))
		{
			sfour = perigeeKm - N(78);

			if (perigeeKm < N(98))
			{
				sfour = N(20);
			}

			qzms24 = Pow4((N(120) - sfour) / Wgs72<T>.RadiusEarthKm);
			sfour = (sfour / Wgs72<T>.RadiusEarthKm) + T.One;
		}

		T pinvsq = T.One / posq;
		T tsi = T.One / (ao - sfour);
		sat.Eta = ao * ecco * tsi;
		T etasq = sat.Eta * sat.Eta;
		T eeta = ecco * sat.Eta;
		T psisq = T.Abs(T.One - etasq);
		T coef = qzms24 * Pow4(tsi);
		T coef1 = coef / math.Pow(psisq, N(3.5));

		T cc2 = coef1 * sat.MeanMotion * ((ao * (T.One + (N(1.5) * etasq) + (eeta * (N(4) + etasq))))
			+ (N(0.375) * j2 * tsi / psisq * sat.Con41 * (N(8) + (N(3) * etasq * (N(8) + etasq)))));
		sat.Cc1 = sat.BStar * cc2;

		T cc3 = T.Zero;

		if (ecco > N(1e-4))
		{
			cc3 = -two * coef * tsi * Wgs72<T>.J3OverJ2 * sat.MeanMotion * sinio / ecco;
		}

		sat.X1mth2 = T.One - cosio2;

		sat.Cc4 = two * sat.MeanMotion * coef1 * ao * omeosq * ((sat.Eta * (two + (N(0.5) * etasq)))
			+ (ecco * (N(0.5) + (two * etasq)))
			- (j2 * tsi / (ao * psisq) * ((-N(3) * sat.Con41 * (T.One - (two * eeta) + (etasq * (N(1.5) - (N(0.5) * eeta)))))
				+ (N(0.75) * sat.X1mth2 * ((two * etasq) - (eeta * (T.One + etasq))) * math.Cos(two * sat.ArgumentOfPerigee)))));

		sat.Cc5 = two * coef1 * ao * omeosq * (T.One + (N(2.75) * (etasq + eeta)) + (eeta * etasq));

		T cosio4 = cosio2 * cosio2;
		T temp1 = N(1.5) * j2 * pinvsq * sat.MeanMotion;
		T temp2 = N(0.5) * temp1 * j2 * pinvsq;
		T temp3 = -N(0.46875) * Wgs72<T>.J4 * pinvsq * pinvsq * sat.MeanMotion;

		sat.MDot = sat.MeanMotion + (N(0.5) * temp1 * rteosq * sat.Con41)
			+ (N(0.0625) * temp2 * rteosq * (N(13) - (N(78) * cosio2) + (N(137) * cosio4)));

		sat.ArgpDot = (-N(0.5) * temp1 * con42)
			+ (N(0.0625) * temp2 * (N(7) - (N(114) * cosio2) + (N(395) * cosio4)))
			+ (temp3 * (N(3) - (N(36) * cosio2) + (N(49) * cosio4)));

		T xhdot1 = -temp1 * cosio;
		sat.NodeDot = xhdot1 + (((N(0.5) * temp2 * (N(4) - (N(19) * cosio2)))
			+ (two * temp3 * (N(3) - (N(7) * cosio2)))) * cosio);

		sat.Omgcof = sat.BStar * cc3 * math.Cos(sat.ArgumentOfPerigee);
		sat.Xmcof = T.Zero;

		if (ecco > N(1e-4))
		{
			sat.Xmcof = -x2o3 * coef * sat.BStar / eeta;
		}

		sat.Nodecf = N(3.5) * omeosq * xhdot1 * sat.Cc1;
		sat.T2cof = N(1.5) * sat.Cc1;

		// A retrograde orbit at exactly 180 degrees would divide by zero here.
		T oneMinusCos = T.Abs(cosio + T.One);
		T guard = N(1.5e-12);
		sat.Xlcof = -N(0.25) * Wgs72<T>.J3OverJ2 * sinio * (N(3) + (N(5) * cosio))
			/ (oneMinusCos > guard ? T.One + cosio : guard);

		sat.Aycof = -N(0.5) * Wgs72<T>.J3OverJ2 * sinio;

		T delmotemp = T.One + (sat.Eta * math.Cos(sat.MeanAnomaly));
		sat.Delmo = delmotemp * delmotemp * delmotemp;
		sat.Sinmao = math.Sin(sat.MeanAnomaly);
		sat.X7thm1 = (N(7) * cosio2) - T.One;

		if (!sat.IsSimplified)
		{
			T cc1sq = sat.Cc1 * sat.Cc1;
			sat.D2 = N(4) * ao * tsi * cc1sq;
			T temp = sat.D2 * tsi * sat.Cc1 / N(3);
			sat.D3 = ((N(17) * ao) + sfour) * temp;
			sat.D4 = N(0.5) * temp * ao * tsi * ((N(221) * ao) + (N(31) * sfour)) * sat.Cc1;
			sat.T3cof = sat.D2 + (two * cc1sq);
			sat.T4cof = N(0.25) * ((N(3) * sat.D3) + (sat.Cc1 * ((N(12) * sat.D2) + (N(10) * cc1sq))));
			sat.T5cof = N(0.2) * ((N(3) * sat.D4) + (N(12) * sat.Cc1 * sat.D3) + (N(6) * sat.D2 * sat.D2)
				+ (N(15) * cc1sq * ((two * sat.D2) + cc1sq)));
		}

		return sat;
	}

	/// <summary>
	/// Propagates an initialized element set to a time expressed in minutes since its epoch.
	/// </summary>
	/// <param name="satellite">The initialized satellite.</param>
	/// <param name="minutesSinceEpoch">Minutes since the element set's epoch; may be negative.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The TEME state, or the reason one could not be produced.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="satellite"/> or <paramref name="math"/> is null.</exception>
	[System.Diagnostics.CodeAnalysis.SuppressMessage(
		"Maintainability", "CA1502:Avoid excessive complexity",
		Justification = "One published closed-form propagation. Splitting it would scatter mutually dependent intermediates across helpers that mean nothing apart, and make it harder to check against the paper it comes from.")]
	public static Sgp4Result<T> Propagate(Sgp4Satellite<T> satellite, T minutesSinceEpoch, IStorageMath<T> math)
	{
		Ensure.NotNull(satellite);
		Ensure.NotNull(math);

		if (satellite.InitializationError != Sgp4Error.None)
		{
			return new(satellite.InitializationError, default);
		}

		if (satellite.IsDeepSpace)
		{
			return new(Sgp4Error.DeepSpaceNotImplemented, default);
		}

		T two = N(2);
		T twoPi = math.Pi * two;
		T x2o3 = two / N(3);
		T xke = Wgs72<T>.Xke(math);
		T j2 = Wgs72<T>.J2;
		T t = minutesSinceEpoch;

		// Secular gravity and atmospheric drag.
		T xmdf = satellite.MeanAnomaly + (satellite.MDot * t);
		T argpdf = satellite.ArgumentOfPerigee + (satellite.ArgpDot * t);
		T nodedf = satellite.RightAscension + (satellite.NodeDot * t);
		T argpm = argpdf;
		T mm = xmdf;
		T t2 = t * t;
		T nodem = nodedf + (satellite.Nodecf * t2);
		T tempa = T.One - (satellite.Cc1 * t);
		T tempe = satellite.BStar * satellite.Cc4 * t;
		T templ = satellite.T2cof * t2;

		if (!satellite.IsSimplified)
		{
			T delomg = satellite.Omgcof * t;
			T delmtemp = T.One + (satellite.Eta * math.Cos(xmdf));
			T delm = satellite.Xmcof * ((delmtemp * delmtemp * delmtemp) - satellite.Delmo);
			T temp = delomg + delm;
			mm = xmdf + temp;
			argpm = argpdf - temp;
			T t3 = t2 * t;
			T t4 = t3 * t;
			tempa = tempa - (satellite.D2 * t2) - (satellite.D3 * t3) - (satellite.D4 * t4);
			tempe += satellite.BStar * satellite.Cc5 * (math.Sin(mm) - satellite.Sinmao);
			templ = templ + (satellite.T3cof * t3) + (t4 * (satellite.T4cof + (t * satellite.T5cof)));
		}

		T nm = satellite.MeanMotion;
		T em = satellite.Eccentricity;
		T inclm = satellite.Inclination;

		if (nm <= T.Zero)
		{
			return new(Sgp4Error.MeanMotionNotPositive, default);
		}

		T am = math.Pow(xke / nm, x2o3) * tempa * tempa;
		nm = xke / math.Pow(am, N(1.5));
		em -= tempe;

		if (em >= T.One || em < N(-0.001))
		{
			return new(Sgp4Error.EccentricityOutOfRange, default);
		}

		if (em < N(1e-6))
		{
			em = N(1e-6);
		}

		mm += satellite.MeanMotion * templ;
		T xlm = mm + argpm + nodem;

		nodem %= twoPi;
		argpm %= twoPi;
		xlm %= twoPi;
		mm = (xlm - argpm - nodem) % twoPi;

		T sinim = math.Sin(inclm);
		T cosim = math.Cos(inclm);

		// Long-period periodics.
		T ep = em;
		T xincp = inclm;
		T argpp = argpm;
		T nodep = nodem;
		T mp = mm;
		T sinip = sinim;
		T cosip = cosim;

		T axnl = ep * math.Cos(argpp);
		T temp0 = T.One / (am * (T.One - (ep * ep)));
		T aynl = (ep * math.Sin(argpp)) + (temp0 * satellite.Aycof);
		T xl = mp + argpp + nodep + (temp0 * satellite.Xlcof * axnl);

		// Kepler's equation, solved by Newton iteration on the eccentric longitude.
		T u = (xl - nodep) % twoPi;
		T eo1 = u;
		T tem5 = N(9999.9);
		T sineo1 = T.Zero;
		T coseo1 = T.Zero;
		T tolerance = N(1e-12);
		T limit = N(0.95);

		for (int iteration = 0; iteration < 10 && T.Abs(tem5) >= tolerance; iteration++)
		{
			sineo1 = math.Sin(eo1);
			coseo1 = math.Cos(eo1);
			tem5 = T.One - (coseo1 * axnl) - (sineo1 * aynl);
			tem5 = (u - (aynl * coseo1) + (axnl * sineo1) - eo1) / tem5;

			if (T.Abs(tem5) >= limit)
			{
				tem5 = tem5 > T.Zero ? limit : -limit;
			}

			eo1 += tem5;
		}

		// Short-period periodics.
		T ecose = (axnl * coseo1) + (aynl * sineo1);
		T esine = (axnl * sineo1) - (aynl * coseo1);
		T el2 = (axnl * axnl) + (aynl * aynl);
		T pl = am * (T.One - el2);

		if (pl < T.Zero)
		{
			return new(Sgp4Error.SemiLatusRectumNegative, default);
		}

		T rl = am * (T.One - ecose);
		T rdotl = math.Sqrt(am) * esine / rl;
		T rvdotl = math.Sqrt(pl) / rl;
		T betal = math.Sqrt(T.One - el2);
		T halfChord = esine / (T.One + betal);
		T sinu = am / rl * (sineo1 - aynl - (axnl * halfChord));
		T cosu = am / rl * (coseo1 - axnl + (aynl * halfChord));
		T su = math.Atan2(sinu, cosu);
		T sin2u = (cosu + cosu) * sinu;
		T cos2u = T.One - (two * sinu * sinu);
		T temp4 = T.One / pl;
		T temp1 = N(0.5) * j2 * temp4;
		T temp2 = temp1 * temp4;

		T mrt = (rl * (T.One - (N(1.5) * temp2 * betal * satellite.Con41)))
			+ (N(0.5) * temp1 * satellite.X1mth2 * cos2u);
		su -= N(0.25) * temp2 * satellite.X7thm1 * sin2u;
		T xnode = nodep + (N(1.5) * temp2 * cosip * sin2u);
		T xinc = xincp + (N(1.5) * temp2 * cosip * sinip * cos2u);
		T mvt = rdotl - (nm * temp1 * satellite.X1mth2 * sin2u / xke);
		T rvdot = rvdotl + (nm * temp1 * ((satellite.X1mth2 * cos2u) + (N(1.5) * satellite.Con41)) / xke);

		// Orientation vectors.
		T sinsu = math.Sin(su);
		T cossu = math.Cos(su);
		T snod = math.Sin(xnode);
		T cnod = math.Cos(xnode);
		T sini = math.Sin(xinc);
		T cosi = math.Cos(xinc);
		T xmx = -snod * cosi;
		T xmy = cnod * cosi;
		T ux = (xmx * sinsu) + (cnod * cossu);
		T uy = (xmy * sinsu) + (snod * cossu);
		T uz = sini * sinsu;
		T vx = (xmx * cossu) - (cnod * sinsu);
		T vy = (xmy * cossu) - (snod * sinsu);
		T vz = sini * cossu;

		T radius = Wgs72<T>.RadiusEarthKm;

		// The model works in Earth radii and its own time unit, so velocity converts by
		// radius * xke / 60 rather than radius / 60. Dropping xke leaves position correct and
		// velocity wrong by a factor of 13.45 — which is what the verification suite caught.
		T speed = radius * xke / N(60);

		TemeState<T> state = new(
			mrt * ux * radius,
			mrt * uy * radius,
			mrt * uz * radius,
			((mvt * ux) + (rvdot * vx)) * speed,
			((mvt * uy) + (rvdot * vy)) * speed,
			((mvt * uz) + (rvdot * vz)) * speed);

		return mrt < T.One ? new(Sgp4Error.Decayed, state) : new(Sgp4Error.None, state);
	}

	/// <summary>Converts a degree value carried by an element set into radians.</summary>
	/// <param name="degrees">The angle in degrees.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The angle in radians.</returns>
	private static T Radians(double degrees, IStorageMath<T> math) => N(degrees) * math.Pi / N(180);

	/// <summary>Raises a value to the fourth power by multiplication.</summary>
	/// <param name="value">The value.</param>
	/// <returns>The fourth power.</returns>
	private static T Pow4(T value)
	{
		T squared = value * value;
		return squared * squared;
	}

	/// <summary>Converts a literal into the storage type.</summary>
	/// <param name="value">The literal.</param>
	/// <returns>The value in <typeparamref name="T"/>.</returns>
	private static T N(double value) => T.CreateChecked(value);
}
