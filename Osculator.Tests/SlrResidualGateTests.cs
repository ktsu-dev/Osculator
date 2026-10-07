// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Residuals;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Data.Slr;
using ktsu.Osculator.Data.Sp3;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// The M4 gate: an SGP4 prediction of a LAGEOS satellite against its laser-ranging orbit, resolved
/// into radial, along-track and cross-track kilometres.
/// </summary>
/// <remarks>
/// <para>
/// The end-to-end measurement here is on <strong>LAGEOS-2</strong>, not LAGEOS-1, and the reason is
/// availability rather than choice: it is the only LAGEOS pair this repository could obtain without
/// a Space-Track account, a real public element set (quoted in Orekit's LAGEOS-2 orbit-determination
/// test input) with a real ILRS prediction of the same satellite on the day before it. The two
/// satellites differ in inclination and nothing else that matters here — same sphere, same height,
/// same absence of drag — so the number this test records is the one the LAGEOS-1 gate is expected
/// to land near. <see cref="LageosGate"/> runs the LAGEOS-1 comparison against the live archives,
/// and <see cref="TheLageos1GateRunsAgainstTheLiveArchives"/> is how to run it.
/// </para>
/// <para>
/// The truth for LAGEOS-2 is a CPF prediction, good to metres over its first day, so every
/// assertion below is in kilometres. The machinery underneath is checked independently: a truth
/// built from SGP4's own output must give zero residual, an offset along the orbit must come back
/// as along-track and with the right sign, and velocities derived from the polynomial must match
/// the ones an ILRS SP3 file publishes.
/// </para>
/// </remarks>
[TestClass]
public sealed class SlrResidualGateTests
{
	/// <summary>LAGEOS-2's element set of 2016-02-14 12:14:48 UTC, as Orekit quotes it.</summary>
	private const string Lageos2Line1 = "1 22195U 92070B   16045.51027931 -.00000009  00000-0  00000+0 0  9990";

	private const string Lageos2Line2 = "2 22195  52.6508 132.9147 0137738 336.2706   1.6348  6.47294052551192";

	private static ElementSet Lageos2 => TleParser.Parse(Lageos2Line1, Lageos2Line2, "LAGEOS 2");

	/// <summary>
	/// IERS finals2000A rows for 2016-02-13 and 2016-02-14 (MJD 57431 and 57432), Bulletin A columns:
	/// pole x and y in arcseconds, UT1 − UTC in seconds. Linear between them, as the table reader does.
	/// </summary>
	private static EarthOrientation Orientation2016(JulianDate instant)
	{
		double mjd = instant.Day - 2400000.5 + instant.DayFraction;
		double t = Math.Clamp(mjd - 57431.0, 0.0, 1.0);

		return new EarthOrientation(
			-0.011897 + (t * (-0.012477 - -0.011897)),
			0.321098 + (t * (0.323274 - 0.321098)),
			0.0071291 + (t * (0.0052412 - 0.0071291)),
			IsPrediction: false);
	}

	[TestMethod]
	public void Lageos2_IsUnderAKilometreWrong_AlongTrackAboveAll()
	{
		IReadOnlyList<TruthSample<double>> truth = SlrTruth.FromCpf(CpfFile.Parse(CpfFileTests.Lageos2), DoubleStorageMath.Instance);
		IReadOnlyList<TruthResidual<double>> residuals = TruthComparison<double>.Compare(
			Lageos2, truth, Orientation2016, DoubleStorageMath.Instance);

		ResidualStatistics<double> statistics = new(DoubleStorageMath.Instance);

		foreach (TruthResidual<double> r in residuals)
		{
			statistics.Add(r.Residual);
		}

		double worst = statistics.Maximum();

		Console.WriteLine(
			$"LAGEOS-2 SGP4 vs ILRS CPF, {residuals.Count} epochs, {residuals[0].MinutesSinceEpoch / 60.0:F1} h to {residuals[^1].MinutesSinceEpoch / 60.0:F1} h from epoch:");
		Console.WriteLine(
			$"  RMS {statistics.Rms:F3} km (radial {statistics.RadialRms:F3}, along-track {statistics.AlongTrackRms:F3}, cross-track {statistics.CrossTrackRms:F3}); worst {worst:F3} km");

		Assert.HasCount(286, residuals);
		Assert.IsLessThan(0.0, residuals[^1].MinutesSinceEpoch, "every epoch is before the element set's own epoch");

		// Measured: RMS 0.445 km, worst 0.627 km, 12 to 36 hours before the epoch. Under a kilometre,
		// where the spec projected "kilometres wrong". The bounds are that with headroom either way:
		// a residual near zero would mean the truth was SGP4, and one of kilometres would mean a frame
		// or time-scale error, which is the size each of those makes.
		Assert.IsGreaterThan(0.2, statistics.Rms);
		Assert.IsLessThan(1.0, worst);

		// Orbital error is a timing error: along-track leads, as trap 5 says it should.
		Assert.IsGreaterThan(statistics.RadialRms, statistics.AlongTrackRms);
		Assert.IsGreaterThan(statistics.CrossTrackRms, statistics.AlongTrackRms);
	}

	[TestMethod]
	public void TruthBuiltFromSgp4ItselfLeavesNoResidual()
	{
		ElementSet elements = Lageos2;
		Sgp4Satellite<double> satellite = Sgp4<double>.Initialize(elements, DoubleStorageMath.Instance);
		List<TruthSample<double>> truth = [];

		for (int minutes = -600; minutes <= 600; minutes += 120)
		{
			JulianDate instant = Offset(elements.EpochJulianDate, minutes);
			TemeState<double> teme = Sgp4<double>.Propagate(satellite, minutes, DoubleStorageMath.Instance).State;
			truth.Add(new TruthSample<double>(
				instant,
				EarthFixedFrame<double>.ToItrf(teme, instant, Orientation2016(instant), DoubleStorageMath.Instance)));
		}

		IReadOnlyList<TruthResidual<double>> residuals = TruthComparison<double>.Compare(
			elements, truth, Orientation2016, DoubleStorageMath.Instance);

		foreach (TruthResidual<double> r in residuals)
		{
			// Not zero, because the instant goes through a two-part Julian date and back to minutes,
			// which rounds it by around a nanosecond: 2e-8 km at 5.7 km/s, measured. A frame error
			// would be metres at the least.
			Assert.AreEqual(0.0, r.Residual.Magnitude(DoubleStorageMath.Instance).Value, 1e-7);
			Assert.AreEqual(0.0, r.Residual.AlongTrackRate, 1e-12);
		}

		Assert.AreEqual(-600.0, residuals[0].MinutesSinceEpoch, 1e-6);
	}

	[TestMethod]
	public void ATruthThatIsBehindThePredictionReadsAsPositiveAlongTrack()
	{
		// Truth taken half a minute earlier on the same orbit is about 170 km behind it at LAGEOS's
		// 5.7 km/s, so SGP4 minus truth is that far forward: positive along-track, and nothing
		// cross-track. A residual taken the wrong way round flips the sign. The radial part is not
		// zero: 170 km along a 12,270 km radius falls away from the tangent by s²/2r, about 1.2 km,
		// and the residual is a chord.
		ElementSet elements = Lageos2;
		Sgp4Satellite<double> satellite = Sgp4<double>.Initialize(elements, DoubleStorageMath.Instance);
		JulianDate instant = Offset(elements.EpochJulianDate, 60.0);
		TemeState<double> earlier = Sgp4<double>.Propagate(satellite, 59.5, DoubleStorageMath.Instance).State;

		TruthSample<double> truth = new(
			instant,
			EarthFixedFrame<double>.ToItrf(earlier, instant, Orientation2016(instant), DoubleStorageMath.Instance));

		RswResidual<double> residual = TruthComparison<double>.Compare(
			elements, [truth], Orientation2016, DoubleStorageMath.Instance)[0].Residual;

		Assert.IsGreaterThan(160.0, residual.AlongTrack);
		Assert.IsLessThan(180.0, residual.AlongTrack);
		Assert.IsLessThan(1.5, Math.Abs(residual.Radial));
		Assert.IsLessThan(0.01, Math.Abs(residual.CrossTrack));
	}

	[TestMethod]
	public void IgnoringTheEarthsOrientationMovesTheAnswer()
	{
		// The orientation is a required argument for a reason, and this is the reason measured: the
		// same comparison with the bulletin ignored is hundreds of metres different.
		IReadOnlyList<TruthSample<double>> truth = SlrTruth.FromCpf(CpfFile.Parse(CpfFileTests.Lageos2), DoubleStorageMath.Instance);
		IReadOnlyList<TruthResidual<double>> measured = TruthComparison<double>.Compare(
			Lageos2, truth, Orientation2016, DoubleStorageMath.Instance);
		IReadOnlyList<TruthResidual<double>> ignored = TruthComparison<double>.Compare(
			Lageos2, truth, _ => EarthOrientation.Ignored, DoubleStorageMath.Instance);

		double largest = measured.Zip(ignored, (a, b) => Math.Abs(a.Residual.AlongTrack - b.Residual.AlongTrack)).Max();

		Console.WriteLine($"Ignoring the bulletin moves the along-track residual by up to {largest * 1000.0:F1} m");

		Assert.IsGreaterThan(0.01, largest);
	}

	[TestMethod]
	public void DerivedVelocitiesMatchTheOnesAnIlrsOrbitPublishes()
	{
		Sp3File orbit = Sp3Parser.Parse(Sp3ParserTests.Lageos);
		IReadOnlyList<TruthSample<double>> published = SlrTruth.FromSp3(orbit, "L51", DoubleStorageMath.Instance);

		List<(JulianDate Instant, double Seconds, double X, double Y, double Z)> positions = [.. orbit.RecordsOf("L51")
			.Select(r => (r.Instant, r.SecondsSinceStart, r.X, r.Y, r.Z))];
		TruthSample<double>[] derived = SlrTruth.Differenced(positions, DoubleStorageMath.Instance);

		Assert.HasCount(published.Count - 2, derived);

		double worst = 0.0;

		for (int i = 0; i < derived.Length; i++)
		{
			ItrfState<double> a = published[i + 1].State;
			ItrfState<double> b = derived[i].State;

			Assert.AreEqual(published[i + 1].Instant, derived[i].Instant);
			worst = Math.Max(worst, Math.Abs(a.VelocityX - b.VelocityX));
			worst = Math.Max(worst, Math.Abs(a.VelocityY - b.VelocityY));
			worst = Math.Max(worst, Math.Abs(a.VelocityZ - b.VelocityZ));
		}

		Console.WriteLine($"Derived against published velocity, LAGEOS-1: {worst * 1e6:F2} mm/s worst");

		// The file writes velocity to 1e-7 km/s (dm/s with six decimals) and position to a millimetre.
		Assert.IsLessThan(1e-5, worst);
	}

	[TestMethod]
	public void AnOrbitOnGpsTimeIsRefused()
	{
		Sp3File igs = Sp3Parser.Parse(Sp3ParserTests.Igs);

		FormatException refused = Assert.ThrowsExactly<FormatException>(
			() => SlrTruth.FromSp3(igs, "G01", DoubleStorageMath.Instance));

		StringAssert.Contains(refused.Message, "GPS");
	}

	[TestMethod]
	public void TheElementSetHeldIsTheLatestFittedBeforeTheInstant()
	{
		ElementSet sunday = WithEpoch(JulianDate.FromCalendar(2021, 12, 26, 6, 0, 0.0));
		ElementSet tuesday = WithEpoch(JulianDate.FromCalendar(2021, 12, 28, 18, 0, 0.0));
		ElementSet afterwards = WithEpoch(JulianDate.FromCalendar(2021, 12, 30, 6, 0, 0.0));
		JulianDate start = JulianDate.FromCalendar(2021, 12, 30, 0, 0, 0.0);

		Assert.AreSame(tuesday, LageosGate.LatestBefore([afterwards, sunday, tuesday], start));
		Assert.AreSame(afterwards, LageosGate.LatestBefore([afterwards], JulianDate.FromCalendar(2021, 12, 30, 6, 0, 0.0)));
		Assert.IsNull(LageosGate.LatestBefore([afterwards], start));
	}

	[TestMethod]
	public void TheDayIsMidnightToMidnight()
	{
		IReadOnlyList<TruthSample<double>> truth = SlrTruth.FromSp3(
			Sp3Parser.Parse(Sp3ParserTests.Lageos), "L51", DoubleStorageMath.Instance);

		// The excerpt runs from 23:00 on the 30th to midnight, which is the 31st.
		Assert.HasCount(30, LageosGate.WithinDay(truth, new DateOnly(2021, 12, 30)));
		Assert.HasCount(1, LageosGate.WithinDay(truth, new DateOnly(2021, 12, 31)));
		Assert.ThrowsExactly<InvalidOperationException>(() => LageosGate.WithinDay(truth, new DateOnly(2022, 1, 1)));
	}

	/// <summary>
	/// The LAGEOS-1 gate itself, against CDDIS and Space-Track. Inconclusive unless the environment
	/// names a day and a Space-Track account whose password, and an Earthdata token, are in the OS
	/// credential store.
	/// </summary>
	/// <remarks>
	/// <code>
	/// OSCULATOR_LIVE_DAY=2021-12-30 OSCULATOR_SPACETRACK_USER=you@example.com \
	///   dotnet test -c Release --filter "FullyQualifiedName~TheLageos1GateRunsAgainstTheLiveArchives"
	/// </code>
	/// Run the test executable with <c>--show-stdout All</c> to see the figures on a pass.
	/// </remarks>
	/// <returns>A task.</returns>
	[TestMethod]
	[TestCategory("Live")]
	public async System.Threading.Tasks.Task TheLageos1GateRunsAgainstTheLiveArchives()
	{
		string? dayText = Environment.GetEnvironmentVariable("OSCULATOR_LIVE_DAY");
		string? user = Environment.GetEnvironmentVariable("OSCULATOR_SPACETRACK_USER");

		if (string.IsNullOrEmpty(dayText) || string.IsNullOrEmpty(user))
		{
			Assert.Inconclusive("Set OSCULATOR_LIVE_DAY and OSCULATOR_SPACETRACK_USER to run the LAGEOS-1 gate against the live archives.");
			return;
		}

		DateOnly day = DateOnly.Parse(dayText, System.Globalization.CultureInfo.InvariantCulture);
		string cacheRoot = System.IO.Path.Join(System.IO.Path.GetTempPath(), "osculator-live");

		using System.Net.Http.HttpClient web = new();
		using System.Net.Http.HttpClient spaceTrackHttp = Data.SpaceTrack.SpaceTrackClient.CreateHttpClient();
		using Data.SpaceTrack.SpaceTrackClient spaceTrack = new(
			spaceTrackHttp,
			new Data.CelesTrak.ResponseCache(System.IO.Path.Join(cacheRoot, "spacetrack"), TimeSpan.FromHours(12), TimeProvider.System),
			new Data.SpaceTrack.SpaceTrackRateLimiter(TimeProvider.System),
			new Data.SpaceTrack.OsCredentialStore(user));
		Data.Cddis.CddisClient cddis = new(
			web,
			new Data.CelesTrak.ResponseCache(System.IO.Path.Join(cacheRoot, "cddis"), TimeSpan.FromHours(12), TimeProvider.System),
			new Data.Cddis.OsCredentialStore());
		Data.Iers.EarthOrientationTable bulletin = await new Data.Iers.IersClient(
			web,
			new Data.CelesTrak.ResponseCache(System.IO.Path.Join(cacheRoot, "iers"), TimeSpan.FromDays(1), TimeProvider.System)).GetTableAsync().ConfigureAwait(false);

		SlrGateResult result = await LageosGate.RunAsync(spaceTrack, cddis, bulletin, day).ConfigureAwait(false);

		ResidualStatistics<double> statistics = new(DoubleStorageMath.Instance);

		foreach (TruthResidual<double> r in result.Residuals)
		{
			statistics.Add(r.Residual);
		}

		Console.WriteLine($"LAGEOS-1 on {day:yyyy-MM-dd}: element set {result.Elements.ElementSetNumber} of {result.Elements.Epoch:u} against {result.TruthSource}");
		Console.WriteLine(
			$"  {result.Residuals.Count} epochs, RMS {statistics.Rms:F3} km (radial {statistics.RadialRms:F3}, along-track {statistics.AlongTrackRms:F3}, cross-track {statistics.CrossTrackRms:F3}); worst {statistics.Maximum():F3} km");

		Assert.IsNotEmpty(result.Residuals);
	}

	private static JulianDate Offset(JulianDate epoch, double minutes) =>
		new(epoch.Day, epoch.DayFraction + (minutes / 1440.0));

	private static ElementSet WithEpoch(JulianDate epoch) => Lageos2 with { EpochJulianDate = epoch };
}
