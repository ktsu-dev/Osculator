// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Residuals;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Data.Horizons;
using ktsu.Osculator.Data.Sp3;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// The decomposition against truth that is not another element set: a laser-ranging SP3 orbit, a
/// Horizons vector, and SGP4's own output (issue #124).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The LAGEOS-1 element set here was fitted, not published,</strong> and that is said in the
/// fixture rather than left to be discovered. The committed SP3 excerpt covers one hour on
/// 2021-12-30, and no historical NORAD set for that hour is in the repository or reachable from the
/// environment this was written in (CelesTrak serves only the current set, and Space-Track needs an
/// account). So SGP4 was fitted by least squares to the excerpt's first eleven epochs — 23:00 to
/// 23:20, positions only, B* held at zero — and the result written out through
/// <see cref="TleWriter"/>, so the set below carries the two-line format's quantization exactly as a
/// published one would. It is a legitimate SGP4 element set for LAGEOS-1: inclination 109.86°,
/// eccentricity 0.0045, 6.387 rev/day, all within a few thousandths of the catalogue's. What it is not
/// is the set the 18th Space Defense Squadron issued, so Δ_model against it is the error of a
/// twenty-minute fit carried forty minutes past its data, not the error an operator would have seen.
/// </para>
/// <para>
/// Measured, 23:00 → 00:00 (60 min), 64 samples, 30-digit reference:
/// </para>
/// <code>
///   Δ_model              0.102 km         (R −0.065  S +0.078  W −0.004)
///   Δ_data   ensemble    0.011 km
///            orientation 2.7e-6 km        (pole y leads, for a final IERS row)
///   Δ_arith  float       2.6e-3 km
///   Δ_arith  double      1.5e-11 km
///   Δ_arith  decimal     1.8e-10 km
///   Δ_arith  precise     0, every digit
/// </code>
/// </remarks>
[TestClass]
public sealed class DecompositionTruthTests
{
	private const int ReferenceDigits = 30;

	/// <summary>The SP3 identifier of LAGEOS-1.</summary>
	private const string Lageos = "L51";

	/// <summary>Seconds after the excerpt's start of its last epoch, 2021-12-31 00:00 UTC.</summary>
	private const double LastEpochSeconds = 3600.0;

	/// <summary>
	/// LAGEOS-1, fitted by least squares to the first twenty minutes of the SP3 excerpt; see the class
	/// remarks for why it is fitted and what that does and does not mean.
	/// </summary>
	private static readonly ElementSet LageosElements = TleParser.Parse(
		"1 08820U 76039A   21364.95833333  .00000000  00000+0  00000+0 0    07",
		"2 08820 109.8553 341.2712 0045222 290.8390 202.8238  6.38668388    08",
		"LAGEOS 1");

	/// <summary>
	/// The Earth's orientation at 2021-12-30 23:30 UTC, from the IERS <c>finals2000A</c> rows for
	/// MJD 59578 and 59579, interpolated linearly. Over the hour UT1 − UTC changes by 13 µs, six
	/// millimetres of rotation, so one value serves the whole excerpt.
	/// </summary>
	/// <remarks>
	/// The rows, as the IERS writes them: 59578 x 0.057738 ± 0.000030″, y 0.274728 ± 0.000036″,
	/// UT1 − UTC −0.1101027 ± 0.0000021 s; 59579 x 0.056257, y 0.275943, UT1 − UTC −0.1104179.
	/// </remarks>
	private static readonly EarthOrientation EndOf2021 = new(
		PoleXArcseconds: 0.056288,
		PoleYArcseconds: 0.275918,
		Ut1MinusUtcSeconds: -0.1104114,
		IsPrediction: false,
		PoleXSigmaArcseconds: 0.000030,
		PoleYSigmaArcseconds: 0.000036,
		Ut1MinusUtcSigmaSeconds: 0.0000021);

	private static readonly PreciseStorageMath Reference = new(ReferenceDigits);

	private static readonly string[] OrientationTerms = ["Ut1MinusUtc", "PoleX", "PoleY"];

	/// <summary>The decomposition against the excerpt's last epoch, computed once for the class.</summary>
	private static readonly Lazy<DecompositionResult<PreciseNumber>> AgainstSp3 = new(() =>
		Decomposition<PreciseNumber>.Compute(
			LageosElements,
			LageosTruth(Reference).At(LastEpochSeconds.ToPreciseNumber(), EndOf2021),
			Reference,
			new DecompositionOptions(Samples: 64)));

	[TestMethod]
	public void AgainstSp3_EveryTermIsReportedForEveryStorageType()
	{
		DecompositionResult<PreciseNumber> result = AgainstSp3.Value;

		Assert.IsTrue(result.IsSuccess, $"SGP4 refused: {result.Error}");
		Assert.AreEqual(TruthSource.EarthFixed, result.Truth.Source);
		Assert.AreEqual(60.0, double.CreateChecked(result.HorizonMinutes), 1e-3);

		Type[] expected = [typeof(float), typeof(double), typeof(decimal), typeof(PreciseNumber)];
		Assert.HasCount(expected.Length, result.Arithmetic);

		for (int i = 0; i < expected.Length; i++)
		{
			Assert.AreEqual(expected[i], result.Arithmetic[i].StorageType);
			Assert.IsTrue(result.Arithmetic[i].IsSuccess, $"{expected[i].Name} refused: {result.Arithmetic[i].Error}");
		}

		Assert.AreEqual(64, result.Data.Samples);
		Assert.AreEqual(0, result.Data.Refusals);

		Console.WriteLine(
			$"Δ_model {Magnitude(result.Model):E3} km (R {Km(result.Model.Radial):F4} S {Km(result.Model.AlongTrack):F4} W {Km(result.Model.CrossTrack):F4}), "
			+ $"Δ_data {Km(result.Data.RmsMagnitude):E3} km + orientation {result.OrientationKilometers:E3} km = {result.DataKilometers:E3} km, "
			+ $"Δ_arith float {Magnitude(result.Arithmetic[0].Residual):E3}, double {Magnitude(result.Arithmetic[1].Residual):E3}, "
			+ $"decimal {Magnitude(result.Arithmetic[2].Residual):E3} km");
	}

	[TestMethod]
	public void AgainstSp3_TheReferenceAgainstItselfIsZeroToEveryDigit()
	{
		// Gate 5, against the new kind of truth: the truth enters only Δ_model, so the harness check
		// has to come back exactly as it does against an element set.
		Assert.IsTrue(AgainstSp3.Value.ReferenceAgainstItself.IsExactlyZero);
	}

	[TestMethod]
	public void AgainstSp3_TheTermsSeparate()
	{
		DecompositionResult<PreciseNumber> result = AgainstSp3.Value;

		double model = Magnitude(result.Model);
		double data = result.DataKilometers;
		double arithDouble = Magnitude(result.Arithmetic[1].Residual);

		// The fitted set holds the orbit to a few metres across its own twenty minutes and drifts to
		// about a hundred metres by the end of the hour, mostly in-plane: radial and along-track.
		Assert.IsTrue(model is > 0.05 and < 0.2, $"Δ_model read {model:E3} km.");
		Assert.IsTrue(Math.Abs(Km(result.Model.CrossTrack)) < Math.Abs(Km(result.Model.AlongTrack)));

		// Above the data term, which is above double's arithmetic by many orders.
		Assert.IsTrue(data < model, $"Δ_data {data:E3} km is not below Δ_model {model:E3} km.");
		Assert.IsTrue(arithDouble < data * 1e-6, $"Δ_arith(double) {arithDouble:E3} km against Δ_data {data:E3} km.");
	}

	[TestMethod]
	public void AgainstSp3_TheEarthOrientationIsFoldedIntoTheDataTerm()
	{
		// Spec §11: SP3 truth is Earth-fixed, so the IERS's uncertainty about where the Earth was
		// pointing moves the truth itself. It belongs to Δ_data and is reported there, one row per
		// parameter, rather than being left inside Δ_model where nothing could separate it out.
		DecompositionResult<PreciseNumber> result = AgainstSp3.Value;

		Assert.HasCount(3, result.Orientation);
		CollectionAssert.AreEquivalent(
			OrientationTerms,
			new[] { result.Orientation[0].Term, result.Orientation[1].Term, result.Orientation[2].Term });

		// Millimetres, for a final row. A sigma of 2.1 µs of UT1 is 1.5e-10 rad of the Earth's turn,
		// and 36 µas of pole is 1.7e-10 rad: at LAGEOS's 12,300 km each is about two millimetres, so
		// here the pole leads rather than UT1 as it does for a forecast. Measured: 2.7 mm combined.
		// A forecast row a year out would put metres here, which is the case this term exists for.
		Assert.IsTrue(result.OrientationKilometers is > 1e-6 and < 1e-5, $"Orientation term read {result.OrientationKilometers:E3} km.");

		double ensemble = Km(result.Data.RmsMagnitude);
		double combined = Math.Sqrt((ensemble * ensemble) + (result.OrientationKilometers * result.OrientationKilometers));
		Assert.AreEqual(combined, result.DataKilometers, 1e-15);
		Assert.IsTrue(result.DataKilometers > ensemble);
	}

	[TestMethod]
	public void AnInertialTruthCarriesNoOrientationTerm()
	{
		TruthState<double> truth = TruthState<double>.FromTeme(
			LaterBy(LageosElements.EpochJulianDate, 30.0),
			Propagate(LageosElements, 30.0));

		DecompositionResult<double> result = Decomposition<double>.Compute(LageosElements, truth, DoubleStorageMath.Instance, new DecompositionOptions(Samples: 8));

		Assert.IsEmpty(result.Orientation);
		Assert.AreEqual(0.0, result.OrientationKilometers);
	}

	[TestMethod]
	public void ATruthBuiltFromSgp4sOwnOutputGivesNoModelError()
	{
		// The model term measured against the model itself. Anything left is the harness: the horizon
		// computed two ways, and nothing else.
		JulianDate target = LaterBy(LageosElements.EpochJulianDate, 45.0);
		TruthState<double> truth = TruthState<double>.FromTeme(target, Propagate(LageosElements, 45.0));

		DecompositionResult<double> result = Decomposition<double>.Compute(LageosElements, truth, DoubleStorageMath.Instance, new DecompositionOptions(Samples: 8));

		Assert.IsTrue(result.IsSuccess);
		Assert.AreEqual(TruthSource.Teme, result.Truth.Source);
		Assert.IsTrue(Magnitude(result.Model) < 1e-9, $"Δ_model read {Magnitude(result.Model):E3} km.");
		Assert.IsTrue(result.ReferenceAgainstItself.IsExactlyZero);
	}

	[TestMethod]
	public void ATruthRoundTrippedThroughTheItrfGivesNoModelError()
	{
		// SGP4's own state, rotated out to the ITRF with a real orientation and back in through the
		// SP3 path. What comes back is the frame layer's round trip: zero to the last few digits. A
		// sign flipped in either rotation, or an orientation applied one way and not the other, would
		// leave hundreds of metres here.
		JulianDate target = LaterBy(LageosElements.EpochJulianDate, 45.0);
		ItrfState<double> itrf = EarthFixedFrame<double>.ToItrf(Propagate(LageosElements, 45.0), target, EndOf2021, DoubleStorageMath.Instance);
		TruthState<double> truth = TruthState<double>.FromItrf(target, itrf, EndOf2021, DoubleStorageMath.Instance);

		DecompositionResult<double> result = Decomposition<double>.Compute(LageosElements, truth, DoubleStorageMath.Instance, new DecompositionOptions(Samples: 8));

		Assert.IsTrue(Magnitude(result.Model) < 1e-9, $"Δ_model read {Magnitude(result.Model):E3} km.");
		Assert.HasCount(3, result.Orientation);
	}

	[TestMethod]
	public void TheSp3TruthIsTheTableAtATabulatedEpoch()
	{
		// At a tabulated epoch the interpolation returns the table, so the only thing between the file
		// and the truth is the rotation. Rotated back out it has to read the file's own digits.
		Sp3File file = Sp3Parser.Parse(Sp3ParserTests.Lageos);
		Sp3Record row = file.RecordsOf(Lageos)[15];
		TruthState<double> truth = LageosTruth(DoubleStorageMath.Instance).At(row.SecondsSinceStart, EndOf2021);

		ItrfState<double> back = EarthFixedFrame<double>.ToItrf(truth.State, truth.EpochJulianDate, EndOf2021, DoubleStorageMath.Instance);

		Assert.AreEqual(row.X, back.X, 1e-9);
		Assert.AreEqual(row.Y, back.Y, 1e-9);
		Assert.AreEqual(row.Z, back.Z, 1e-9);
		Assert.AreEqual(row.Velocity!.Value.X, back.VelocityX, 1e-12);
		Assert.AreEqual(row.Velocity!.Value.Y, back.VelocityY, 1e-12);
		Assert.AreEqual(row.Velocity!.Value.Z, back.VelocityZ, 1e-12);
		Assert.AreEqual(new DateTime(2021, 12, 30, 23, 30, 0, DateTimeKind.Utc), truth.Epoch);
	}

	[TestMethod]
	public void AnSp3TableWithoutVelocitiesIsRefused()
	{
		Sp3File file = Sp3Parser.Parse(Sp3ParserTests.Lageos);

		Assert.ThrowsExactly<ArgumentException>(() =>
			new Sp3Truth<double>(file.Start, file.Tabulate<double>(Lageos), [], DoubleStorageMath.Instance));
	}

	[TestMethod]
	public void ATruthBeforeTheElementSetIsRefused()
	{
		TruthState<double> truth = TruthState<double>.FromTeme(
			LaterBy(LageosElements.EpochJulianDate, -10.0),
			Propagate(LageosElements, -10.0));

		Assert.ThrowsExactly<ArgumentException>(() =>
			Decomposition<double>.Compute(LageosElements, truth, DoubleStorageMath.Instance));
	}

	[TestMethod]
	public void AHorizonsSpacecraftTableRunsEndToEnd()
	{
		// A Horizons response in the service's layout, for a spacecraft, whose rows are SGP4's own
		// states rotated into the GCRF at exact TT instants: so the whole path — parse, TT to UTC
		// through the leap-second table, GCRF to TEME, decompose — has a known answer, which is no
		// model error. The rows are three hours apart, a step Horizons' nine-decimal Julian date writes
		// exactly; at the minute a row's time is rounded by up to 43 µs, a quarter of a metre.
		HorizonsEphemeris table = HorizonsVectorTable.Parse(HorizonsSample.Wrap(HorizonsSample.Report(
			target: "LAGEOS-1 (spacecraft) (-8820)  {source: fitted}",
			rows: SpacecraftRows())));

		Assert.AreEqual(HorizonsTimeScale.Tdb, table.TimeScale);
		Assert.AreEqual("ICRF", table.ReferenceFrame);
		Assert.HasCount(4, table.Vectors);

		foreach (HorizonsStateVector row in table.Vectors)
		{
			TruthState<double> truth = TruthState<double>.FromGcrf(
				row.Epoch,
				new GcrfState<double>(row.X, row.Y, row.Z, row.VelocityX, row.VelocityY, row.VelocityZ),
				CelestialPoleOffsets.Ignored,
				LeapSeconds.BuiltIn,
				DoubleStorageMath.Instance);

			DecompositionResult<double> result = Decomposition<double>.Compute(LageosElements, truth, DoubleStorageMath.Instance, new DecompositionOptions(Samples: 8));

			Assert.IsTrue(result.IsSuccess);
			Assert.AreEqual(TruthSource.Celestial, result.Truth.Source);
			Assert.IsEmpty(result.Orientation);
			Assert.IsTrue(Magnitude(result.Model) < 1e-6, $"Δ_model at {row.CalendarDate} read {Magnitude(result.Model):E3} km.");
		}

		// The first row is midnight TT on the 31st, which is 69.184 s earlier in UTC (37 s of leap
		// seconds and the fixed 32.184 s).
		TruthState<double> first = TruthState<double>.FromGcrf(
			table.Vectors[0].Epoch, default, CelestialPoleOffsets.Ignored, LeapSeconds.BuiltIn, DoubleStorageMath.Instance);
		Assert.AreEqual(new DateTime(2021, 12, 30, 23, 58, 50, 816, DateTimeKind.Utc), first.Epoch);
	}

	/// <summary>Builds the four rows of the Horizons fixture, three hours apart from midnight TT on 2021-12-31.</summary>
	private static string SpacecraftRows()
	{
		StringBuilder rows = new();

		for (int k = 0; k < 4; k++)
		{
			JulianDate tt = new(2459579.5, k * 0.125);
			JulianDate utc = new(tt.Day, tt.DayFraction - (69.184 / TimeScales.SecondsPerDay));
			double minutes = (utc.Day - LageosElements.EpochJulianDate.Day + utc.DayFraction - LageosElements.EpochJulianDate.DayFraction) * 1440.0;
			GcrfState<double> s = GcrfFrame<double>.FromTeme(Propagate(LageosElements, minutes), tt, CelestialPoleOffsets.Ignored, DoubleStorageMath.Instance);

			rows.Append(CultureInfo.InvariantCulture, $"{tt.Day + tt.DayFraction:F9}, A.D. 2021-Dec-31 {k * 3:D2}:00:00.0000, ")
				.Append(CultureInfo.InvariantCulture, $"{E(s.X)}, {E(s.Y)}, {E(s.Z)}, {E(s.VelocityX)}, {E(s.VelocityY)}, {E(s.VelocityZ)},")
				.AppendLine();
		}

		return rows.ToString();

		static string E(double value) => value.ToString("0.000000000000000E+00", CultureInfo.InvariantCulture);
	}

	/// <summary>Reads the committed LAGEOS-1 excerpt as a truth in a storage type.</summary>
	private static Sp3Truth<T> LageosTruth<T>(IStorageMath<T> math)
		where T : struct, System.Numerics.INumber<T>
	{
		Sp3File file = Sp3Parser.Parse(Sp3ParserTests.Lageos);
		IReadOnlyList<Sp3Record> records = file.RecordsOf(Lageos);
		List<TabulatedPosition<T>> velocities = new(records.Count);

		foreach (Sp3Record r in records)
		{
			Sp3Velocity v = r.Velocity!.Value;
			velocities.Add(new TabulatedPosition<T>(T.CreateChecked(r.SecondsSinceStart), T.CreateChecked(v.X), T.CreateChecked(v.Y), T.CreateChecked(v.Z)));
		}

		// ILRS orbits are written in UTC, which is the scale the truth wants, so the file's start is
		// used as it stands.
		Assert.AreEqual("UTC", file.TimeSystem);
		return new Sp3Truth<T>(file.Start, file.Tabulate<T>(Lageos), velocities, math);
	}

	/// <summary>Propagates an element set in double.</summary>
	private static TemeState<double> Propagate(ElementSet elements, double minutes) =>
		Sgp4<double>.Propagate(Sgp4<double>.Initialize(elements, DoubleStorageMath.Instance), minutes, DoubleStorageMath.Instance).State;

	/// <summary>An instant some minutes after another, formed without collapsing the date.</summary>
	private static JulianDate LaterBy(JulianDate from, double minutes) => new(from.Day, from.DayFraction + (minutes / 1440.0));

	private static double Km(PreciseNumber value) => double.CreateChecked(value);

	private static double Magnitude(RswResidual<PreciseNumber> r) =>
		Math.Sqrt((Km(r.Radial) * Km(r.Radial)) + (Km(r.AlongTrack) * Km(r.AlongTrack)) + (Km(r.CrossTrack) * Km(r.CrossTrack)));

	private static double Magnitude(RswResidual<double> r) =>
		Math.Sqrt((r.Radial * r.Radial) + (r.AlongTrack * r.AlongTrack) + (r.CrossTrack * r.CrossTrack));
}
