// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ktsu.Osculator.Core.Forces;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// The force model beyond two-body: each force against a value computed outside this code.
/// </summary>
/// <remarks>
/// <para>
/// The harmonic references come from SHTOOLS (<c>pyshtools.gravmag.MakeGravGridPoint</c>, 4.14.1),
/// an independent Fortran implementation that sums the field by a different route: Legendre
/// functions in latitude and longitude rather than Cunningham's Cartesian recursion, with its
/// spherical components rotated to Cartesian by hand. Two implementations that share no code and
/// no formulation agreeing to eleven digits is the check. The coefficients it was given are the same
/// NGA set bundled here, at μ = 398600.4415 km³/s² and a = 6378.1363 km.
/// </para>
/// <para>
/// The drag and radiation-pressure references were worked at forty digits with <c>mpmath</c> from
/// the formulas in the classes' remarks, so they check the code against its own documentation:
/// units, signs, the co-rotating atmosphere, the band lookup.
/// </para>
/// </remarks>
[TestClass]
public sealed class ForceModelTests
{
	private static readonly DoubleStorageMath Math64 = DoubleStorageMath.Instance;

	// x, y, z (km, Earth-fixed); degree; order; aₓ, a_y, a_z (km/s²) from SHTOOLS.
	private static readonly double[][] HarmonicReferences =
	[
		[6524.834, 6862.875, 6448.296, 2, 0, 5.083839606690762e-07, 5.347225039099518e-07, -1.218246958685740e-06],
		[6524.834, 6862.875, 6448.296, 8, 8, 5.192436201056673e-07, 5.258188114887103e-07, -1.210343535537822e-06],
		[6524.834, 6862.875, 6448.296, 20, 10, 5.193119244444151e-07, 5.258010671713825e-07, -1.210325144999603e-06],
		[6524.834, 6862.875, 6448.296, 70, 70, 5.193127875802987e-07, 5.258015186810587e-07, -1.210323194609628e-06],
		[-1234.5, 5678.9, 3210.1, 2, 0, -4.256540973634599e-07, 1.958077807628473e-06, -1.199926231696120e-05],
		[-1234.5, 5678.9, 3210.1, 8, 8, -6.462351320156347e-07, 2.128639533736306e-06, -1.195526384677622e-05],
		[-1234.5, 5678.9, 3210.1, 20, 10, -5.662154790565223e-07, 2.105559778301176e-06, -1.195644737022639e-05],
		[-1234.5, 5678.9, 3210.1, 70, 70, -4.854922104977231e-07, 2.112143518537215e-06, -1.194748959878142e-05],
		[100.0, -200.0, 6900.0, 2, 0, 6.708112754367211e-07, -1.341622550873442e-06, 2.311260007851035e-05],
		[100.0, -200.0, 6900.0, 8, 8, 7.497711421576448e-07, -1.324008965728409e-06, 2.298255531467677e-05],
		[100.0, -200.0, 6900.0, 20, 10, 7.643855769498495e-07, -1.335679679253382e-06, 2.294976596404386e-05],
		[100.0, -200.0, 6900.0, 70, 70, 7.642548793929949e-07, -1.338225611658303e-06, 2.295734785078067e-05],
		[42164.0, 0.0, 0.0, 2, 0, -8.331594688456866e-09, 0.0, 0.0],
		[42164.0, 0.0, 0.0, 8, 8, -8.398655878905178e-09, -2.131062606016326e-11, 1.684900672806298e-12],
		[42164.0, 0.0, 0.0, 20, 10, -8.398655932148627e-09, -2.131059329848042e-11, 1.684914304429971e-12],
		[42164.0, 0.0, 0.0, 70, 70, -8.398655932464936e-09, -2.131059375070763e-11, 1.684914307458467e-12],
	];

	[TestMethod]
	public void Egm96_BundledCoefficients_AreThePublishedOnes()
	{
		// Spot values from the NGA's EGM96 coefficient file, in its own digits.
		GravityField field = Egm96.Load();
		Assert.AreEqual(70, field.MaximumDegree);
		Assert.AreEqual(-0.484165371736e-3, Parse(field.Cosine(2, 0)));
		Assert.AreEqual(-0.186987635955e-9, Parse(field.Cosine(2, 1)));
		Assert.AreEqual(0.119528012031e-8, Parse(field.Sine(2, 1)));
		Assert.AreEqual(0.243914352398e-5, Parse(field.Cosine(2, 2)));
		Assert.AreEqual(-0.140016683654e-5, Parse(field.Sine(2, 2)));
		Assert.AreEqual(0.957254173792e-6, Parse(field.Cosine(3, 0)));
		Assert.AreEqual("0.0", field.Sine(5, 0));
		Assert.AreEqual(-4.70375138826e-10, Parse(field.Cosine(70, 70)));
		Assert.AreEqual(-6.48306137833e-10, Parse(field.Sine(70, 70)));
	}

	[TestMethod]
	public void GravityField_ParsesTheNgaLayout_AndRefusesWhatIsNot()
	{
		// FORTRAN exponents and trailing sigma columns, as the published file writes them.
		const string Text = """
			# comment
			    2    0 -0.484165371736D-03  0.000000000000D+00  0.35610635D-10  0.00000000D+00
			    2    2  0.243914352398D-05 -0.140016683654D-05  0.53739154D-10  0.54353269D-10
			    3    0  0.957254173792D-06  0.000000000000D+00  0.18094237D-10  0.00000000D+00
			""";
		using StringReader reader = new(Text);
		GravityField field = Egm96.Parse(reader, 2);
		Assert.AreEqual(-0.484165371736e-3, Parse(field.Cosine(2, 0)));
		Assert.AreEqual(-0.140016683654e-5, Parse(field.Sine(2, 2)));
		Assert.AreEqual("0", field.Cosine(2, 1), "a pair the file leaves out is zero");
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => field.Cosine(3, 0), "degree 3 was above the requested maximum");

		using StringReader orderAboveDegree = new("2 3 1.0 1.0");
		using StringReader notANumber = new("2 0 x 1.0");
		Assert.ThrowsExactly<FormatException>(() => Egm96.Parse(orderAboveDegree, 2), "order above degree");
		Assert.ThrowsExactly<FormatException>(() => Egm96.Parse(notANumber, 2), "not a number");
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Egm96.Load(71));
	}

	[TestMethod]
	public void SphericalHarmonics_MatchAnIndependentImplementation()
	{
		// Four points — mid-latitude LEO, a second LEO, near the pole (where a latitude-longitude
		// formulation is at its worst and Cunningham's has no singularity), and geostationary — at
		// J₂ alone, a full 8×8, a truncated 20×10, and the whole bundled 70×70.
		GravityField field = Egm96.Load();
		double worst = 0.0;
		foreach (double[] row in HarmonicReferences)
		{
			SphericalHarmonicGravity<double> gravity = new(field, (int)row[3], (int)row[4], new EarthRotation<double>(0.0, 0.0), Math64);
			(double x, double y, double z) = gravity.BodyFixed(row[0], row[1], row[2]);
			double error = Distance(x - row[5], y - row[6], z - row[7]) / Distance(row[5], row[6], row[7]);
			worst = Math.Max(worst, error);
			Assert.IsLessThan(
				1e-11,
				error,
				$"({row[0]}, {row[1]}, {row[2]}) at {row[3]}×{row[4]}: relative error {error:E2}");
		}

		Console.WriteLine($"worst relative difference from SHTOOLS: {worst:E2}");
	}

	[TestMethod]
	public void J2Alone_IsTheClosedForm()
	{
		// a = −(3/2) J₂ μ R² / r⁵ · (x (1 − 5z²/r²), y (1 − 5z²/r²), z (3 − 5z²/r²)), with
		// J₂ = −√5 C̄₂₀. A formula that shares nothing with the recursion but the coefficient.
		GravityField field = Egm96.Load(2);
		double mu = Parse(Egm96.GravitationalParameter);
		double radius = Parse(Egm96.ReferenceRadius);
		double j2 = -Math.Sqrt(5.0) * Parse(field.Cosine(2, 0));
		SphericalHarmonicGravity<double> gravity = new(field, 2, 0, new EarthRotation<double>(0.0, 0.0), Math64);

		foreach ((double x, double y, double z) in new[] { (7000.0, 0.0, 0.0), (-1234.5, 5678.9, 3210.1), (10.0, 20.0, -6900.0) })
		{
			double r2 = (x * x) + (y * y) + (z * z);
			double k = -1.5 * j2 * mu * radius * radius / (r2 * r2 * Math.Sqrt(r2));
			double zz = 5.0 * z * z / r2;
			(double ax, double ay, double az) = gravity.BodyFixed(x, y, z);
			double expected = Distance(k * x * (1 - zz), k * y * (1 - zz), k * z * (3 - zz));
			double error = Distance(ax - (k * x * (1 - zz)), ay - (k * y * (1 - zz)), az - (k * z * (3 - zz)));
			Assert.IsLessThan(1e-13, error / expected, $"({x}, {y}, {z})");
		}
	}

	[TestMethod]
	public void SphericalHarmonics_TurnWithTheEarth()
	{
		// The same Earth-fixed point, reached in the inertial frame after the Earth has turned
		// through θ₀ + ωt, has to feel the same acceleration turned the same way. Getting the
		// rotation's sense wrong reads the field at a point 2θ away in longitude.
		GravityField field = Egm96.Load(8);
		double omega = EarthRotation<double>.At(new Core.Time.JulianDate(2461041.5, 0.25), 0.0, Math64).RateRadiansPerSecond;
		EarthRotation<double> rotation = new(1.0, omega);
		SphericalHarmonicGravity<double> gravity = new(field, 8, 8, rotation, Math64);
		double[] row = HarmonicReferences[5];

		const double Seconds = 1000.0;
		double theta = rotation.AngleAt(Seconds, Math64);
		Assert.AreEqual(1.0 + (omega * Seconds), theta, 1e-15);

		double c = Math.Cos(theta);
		double s = Math.Sin(theta);
		CartesianState<double> inertial = new((c * row[0]) - (s * row[1]), (s * row[0]) + (c * row[1]), row[2], 0.0, 0.0, 0.0);
		CartesianAcceleration<double> a = gravity.Acceleration(Seconds, inertial);
		double ex = (c * row[5]) - (s * row[6]);
		double ey = (s * row[5]) + (c * row[6]);
		double error = Distance(a.X - ex, a.Y - ey, a.Z - row[7]) / Distance(row[5], row[6], row[7]);
		Assert.IsLessThan(1e-11, error);
	}

	[TestMethod]
	public void J2_RegressesTheNode_AtTheTextbookRate()
	{
		// dΩ/dt = −(3/2) n J₂ (R/p)² cos i, the secular rate every orbit-design book quotes. A sign
		// error in the harmonics turns regression into advance; a missing factor shows as a ratio.
		// Integrated for fifteen whole orbits of a circular ISS-like orbit so the short-period terms
		// mostly cancel. The osculating and mean semi-major axes differ by about J₂R²/a ≈ 7 km, worth
		// a few tenths of a percent in the rate, so the bound is 1 %. Measured: see the output.
		GravityField field = Egm96.Load(2);
		double mu = Parse(Egm96.GravitationalParameter);
		double radius = Parse(Egm96.ReferenceRadius);
		double j2 = -Math.Sqrt(5.0) * Parse(field.Cosine(2, 0));
		CombinedForceModel<double> forces = new(
			Math64,
			new TwoBody<double>(mu, Math64),
			new SphericalHarmonicGravity<double>(field, 2, 0, new EarthRotation<double>(0.0, 7.292115e-5), Math64));

		const double A = 6778.0;
		double inclination = 51.6 * Math.PI / 180.0;
		double speed = Math.Sqrt(mu / A);
		CartesianState<double> initial = new(A, 0.0, 0.0, 0.0, speed * Math.Cos(inclination), speed * Math.Sin(inclination));
		double n = Math.Sqrt(mu / (A * A * A));
		double duration = 15.0 * 2.0 * Math.PI / n;

		CowellResult<double> result = new Cowell<double>(forces, Math64).Propagate(initial, duration, CowellTolerance.Default with { Relative = 1e-11 });

		double drift = Node(result.State) - Node(initial);
		double expected = -1.5 * n * j2 * (radius / A) * (radius / A) * Math.Cos(inclination) * duration;
		double ratio = drift / expected;
		Console.WriteLine($"node drift {drift * 180.0 / Math.PI:F4}° against {expected * 180.0 / Math.PI:F4}°, ratio {ratio:F5}");
		Assert.AreEqual(1.0, ratio, 0.01);
	}

	[TestMethod]
	public void ExponentialAtmosphere_IsContinuousAcrossEveryBand()
	{
		// Each band's base density run up its scale height to the next band's base altitude has to
		// land on the next band's base density. Checks the transcription: one wrong digit in a
		// density or a scale height breaks a boundary.
		ExponentialAtmosphere<double> atmosphere = new(Math64);
		Assert.AreEqual(28, atmosphere.BandCount);
		for (int band = 0; band + 1 < atmosphere.BandCount; band++)
		{
			double next = atmosphere.BaseAltitude(band + 1);
			double carried = atmosphere.BaseDensity(band) * Math.Exp(-(next - atmosphere.BaseAltitude(band)) / atmosphere.ScaleHeight(band));
			Assert.AreEqual(1.0, carried / atmosphere.BaseDensity(band + 1), 1.5e-3, $"boundary at {next} km");
		}

		Assert.AreEqual(3.725e-12, atmosphere.Density(400.0), 1e-24);
		Assert.AreEqual(1.225, atmosphere.Density(0.0), 1e-15);
		Assert.AreEqual(3.019e-15 * Math.Exp(-200.0 / 268.0), atmosphere.Density(1200.0), 1e-27, "the last band continues");
	}

	[TestMethod]
	public void Drag_MatchesTheWorkedValue_AgainstACoRotatingAtmosphere()
	{
		// r = (6778, 0, 0) km, v = (0, 7.6686, 0.5) km/s, C_D A/m = 0.01 m²/kg. Worked at forty
		// digits: altitude 399.863 km, ρ = 3.7345737545e-12 kg/m³, relative velocity
		// (0, 7.6686 − ω·6778, 0.5).
		AtmosphericDrag<double> drag = new(new ExponentialAtmosphere<double>(Math64), 0.01, 7.292115146706979e-5, Math64);
		CartesianAcceleration<double> a = drag.Acceleration(0.0, new(6778.0, 0.0, 0.0, 0.0, 7.6686, 0.5));
		Assert.AreEqual(0.0, a.X);
		Assert.AreEqual(-9.634455102144768e-10, a.Y, 1e-23);
		Assert.AreEqual(-6.714523229664961e-11, a.Z, 1e-24);

		// Pinned at the two places the first-order ellipsoid radius is exact.
		Assert.AreEqual(6778.0 - 6378.137, drag.Altitude(6778.0, 0.0, 0.0), 1e-9);
		Assert.AreEqual(6400.0 - 6356.752314245, drag.Altitude(0.0, 0.0, 6400.0), 1e-6);
	}

	[TestMethod]
	public void SolarRadiationPressure_MatchesTheWorkedValue_AndSwitchesOffInShadow()
	{
		// Sun at one AU along x, C_R A/m = 0.02 m²/kg; worked at forty digits.
		IBodyEphemeris<double> sun = new FixedBody(149597870.7, 0.0, 0.0);
		SolarRadiationPressure<double> srp = new(sun, 0.02, Math64);
		CartesianAcceleration<double> a = srp.Acceleration(0.0, new(0.0, 7000.0, 1000.0, 0.0, 0.0, 0.0));
		Assert.AreEqual(-9.119999969436346e-11, a.X, 1e-25);
		Assert.AreEqual(4.267440404554797e-15, a.Y, 1e-29);
		Assert.AreEqual(6.096343435078281e-16, a.Z, 1e-30);

		Assert.AreEqual(new CartesianAcceleration<double>(0.0, 0.0, 0.0), srp.Acceleration(0.0, new(-7000.0, 6300.0, 0.0, 0.0, 0.0, 0.0)), "behind the Earth, inside the cylinder");
		Assert.AreNotEqual(0.0, srp.Acceleration(0.0, new(-7000.0, 6400.0, 0.0, 0.0, 0.0, 0.0)).X, "behind the Earth, outside the cylinder");
		Assert.AreNotEqual(0.0, srp.Acceleration(0.0, new(7000.0, 0.0, 0.0, 0.0, 0.0, 0.0)).X, "on the sunlit side");
		Assert.AreNotEqual(0.0, new SolarRadiationPressure<double>(sun, 0.02, Math64, shadow: false).Acceleration(0.0, new(-7000.0, 0.0, 0.0, 0.0, 0.0, 0.0)).X, "shadow switched off");
	}

	[TestMethod]
	public void ThirdBody_BattinsForm_IsTheDirectForm()
	{
		// At fifty digits the direct difference μ(d/|d|³ − s/|s|³) has digits to spare for its
		// cancellation, so it is the reference; Battin's form is what the class computes.
		PreciseStorageMath math = new(50);
		foreach ((double sx, double sy, double sz, string mu) in new[]
		{
			(-1.2e8, 8.1e7, 3.5e7, "132712440041.279419"),
			(3.1e5, -2.2e5, 1.1e5, "4902.800118"),
		})
		{
			PreciseNumber m = PreciseNumber.Parse(mu, CultureInfo.InvariantCulture);
			ThirdBodyGravity<PreciseNumber> body = new(m, new FixedBodyPrecise(sx, sy, sz), math);
			CartesianState<PreciseNumber> state = new(P(6778.0), P(1200.0), P(-300.0), P(0.0), P(0.0), P(0.0));
			CartesianAcceleration<PreciseNumber> a = body.Acceleration(PreciseNumber.Zero, state);

			PreciseNumber dx = P(sx) - state.X;
			PreciseNumber dy = P(sy) - state.Y;
			PreciseNumber dz = P(sz) - state.Z;
			PreciseNumber d = math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
			PreciseNumber s = math.Sqrt((P(sx) * P(sx)) + (P(sy) * P(sy)) + (P(sz) * P(sz)));
			PreciseNumber d3 = math.ToWorkingPrecision(d * d * d);
			PreciseNumber s3 = math.ToWorkingPrecision(s * s * s);
			PreciseNumber ex = m * ((dx / d3) - (P(sx) / s3));
			PreciseNumber ey = m * ((dy / d3) - (P(sy) / s3));
			PreciseNumber ez = m * ((dz / d3) - (P(sz) / s3));

			double error = Distance(double.CreateChecked(a.X - ex), double.CreateChecked(a.Y - ey), double.CreateChecked(a.Z - ez)) / Distance(double.CreateChecked(ex), double.CreateChecked(ey), double.CreateChecked(ez));
			Assert.IsLessThan(1e-40, error, $"body at ({sx}, {sy}, {sz})");
		}
	}

	[TestMethod]
	public void ThirdBody_InDouble_KeepsTheDigitsTheDirectFormLoses()
	{
		// The reason for Battin's form, measured: for the Sun the direct form in double loses
		// several digits to its subtraction, and Battin's does not.
		PreciseStorageMath math = new(50);
		(double sx, double sy, double sz) = (-1.2e8, 8.1e7, 3.5e7);
		CartesianState<double> state = new(6778.0, 1200.0, -300.0, 0.0, 0.0, 0.0);
		double mu = 132712440041.279419;

		CartesianAcceleration<PreciseNumber> reference = new ThirdBodyGravity<PreciseNumber>(
			PreciseNumber.Parse("132712440041.279419", CultureInfo.InvariantCulture), new FixedBodyPrecise(sx, sy, sz), math)
			.Acceleration(PreciseNumber.Zero, new(P(6778.0), P(1200.0), P(-300.0), P(0.0), P(0.0), P(0.0)));
		double rx = double.CreateChecked(reference.X);
		double ry = double.CreateChecked(reference.Y);
		double rz = double.CreateChecked(reference.Z);

		CartesianAcceleration<double> battin = new ThirdBodyGravity<double>(mu, new FixedBody(sx, sy, sz), Math64).Acceleration(0.0, state);
		double dx = sx - state.X;
		double dy = sy - state.Y;
		double dz = sz - state.Z;
		double d3 = Math.Pow(Distance(dx, dy, dz), 3);
		double s3 = Math.Pow(Distance(sx, sy, sz), 3);
		(double nx, double ny, double nz) = (mu * ((dx / d3) - (sx / s3)), mu * ((dy / d3) - (sy / s3)), mu * ((dz / d3) - (sz / s3)));

		double magnitude = Distance(rx, ry, rz);
		double battinError = Distance(battin.X - rx, battin.Y - ry, battin.Z - rz) / magnitude;
		double directError = Distance(nx - rx, ny - ry, nz - rz) / magnitude;
		Console.WriteLine($"relative error in double: Battin {battinError:E2}, direct {directError:E2}");
		Assert.IsLessThan(1e-15, battinError);
		Assert.IsGreaterThan(100.0 * battinError, directError);
	}

	[TestMethod]
	public void TabulatedEphemeris_InterpolatesASmoothOrbit_AndRefusesToExtrapolate()
	{
		// A circular orbit of the Moon's radius and period, sampled hourly, read back half-way
		// between samples. Nine points through a ninth of an hour-sampled month is far inside what
		// an eighth-degree polynomial fits.
		const double Radius = 384400.0;
		double rate = 2.0 * Math.PI / (27.321661 * 86400.0);
		List<double> times = [];
		List<BodyPosition<double>> samples = [];
		for (int hour = 0; hour <= 48; hour++)
		{
			double t = hour * 3600.0;
			times.Add(t);
			samples.Add(new(Radius * Math.Cos(rate * t), Radius * Math.Sin(rate * t), 0.0));
		}

		TabulatedEphemeris<double> ephemeris = new(times, samples, Math64);
		double worst = 0.0;
		for (double t = 0.0; t <= 48 * 3600.0; t += 1800.0 + 7.0)
		{
			BodyPosition<double> p = ephemeris.PositionAt(t);
			worst = Math.Max(worst, Distance(p.X - (Radius * Math.Cos(rate * t)), p.Y - (Radius * Math.Sin(rate * t)), p.Z));
		}

		Console.WriteLine($"worst interpolation error {worst:E2} km");
		Assert.IsLessThan(1e-6, worst);
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ephemeris.PositionAt(-1.0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ephemeris.PositionAt((48 * 3600.0) + 1.0));
		Assert.ThrowsExactly<ArgumentException>(() => new TabulatedEphemeris<double>([0.0, 0.0, 1.0], [default, default, default], Math64, 2));
	}

	[TestMethod]
	public void CombinedForceModel_SumsWhatIsSwitchedOn()
	{
		IForceModel<double> one = new Constant(1.0, 2.0, 3.0);
		IForceModel<double> two = new Constant(10.0, 20.0, 30.0);
		CombinedForceModel<double> combined = new(Math64, one, two);
		CartesianState<double> state = new(7000.0, 0.0, 0.0, 0.0, 7.5, 0.0);

		Assert.AreEqual(new CartesianAcceleration<double>(11.0, 22.0, 33.0), combined.Acceleration(0.0, state));
		combined.Terms[1].Enabled = false;
		Assert.AreEqual(new CartesianAcceleration<double>(1.0, 2.0, 3.0), combined.Acceleration(0.0, state));
		combined.Terms[0].Enabled = false;
		Assert.AreEqual(new CartesianAcceleration<double>(0.0, 0.0, 0.0), combined.Acceleration(0.0, state));
	}

	private static double Node(CartesianState<double> s)
	{
		double hx = (s.Y * s.VelocityZ) - (s.Z * s.VelocityY);
		double hy = (s.Z * s.VelocityX) - (s.X * s.VelocityZ);
		return Math.Atan2(hx, -hy);
	}

	private static double Distance(double x, double y, double z) => Math.Sqrt((x * x) + (y * y) + (z * z));

	private static double Parse(string literal) => double.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture);

	private static PreciseNumber P(double value) => value.ToPreciseNumber();

	private sealed class FixedBody(double x, double y, double z) : IBodyEphemeris<double>
	{
		public BodyPosition<double> PositionAt(double secondsSinceEpoch) => new(x, y, z);
	}

	private sealed class FixedBodyPrecise(double x, double y, double z) : IBodyEphemeris<PreciseNumber>
	{
		public BodyPosition<PreciseNumber> PositionAt(PreciseNumber secondsSinceEpoch) => new(P(x), P(y), P(z));
	}

	private sealed class Constant(double x, double y, double z) : IForceModel<double>
	{
		public CartesianAcceleration<double> Acceleration(double secondsSinceEpoch, CartesianState<double> state) => new(x, y, z);
	}
}
