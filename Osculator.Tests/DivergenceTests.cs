// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Residuals;
using ktsu.Osculator.Data.CelesTrak;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
using ktsu.Semantics.Quantities.Units;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// The M2 gate: an archived element set propagated to a later set's epoch and compared against it.
/// </summary>
/// <remarks>
/// <para>
/// The archive under <c>Data/iss-snapshots</c> is three real ISS element sets exactly as CelesTrak
/// served them, laid out the way <see cref="SnapshotStore"/> writes them, so the whole path — store,
/// OMM reader, SGP4, RIC frame — runs as it does in the application. They are the same three
/// responses already committed verbatim elsewhere in this suite (<c>OmmJsonTests</c>,
/// <c>TleParserTests</c>, <c>CelesTrakClientTests</c>); nothing here is synthetic.
/// </para>
/// <para>
/// What the number means: model error and data error together. The later set is not truth, it is
/// another fit of the same model; see <see cref="ElementSetDivergence{T}"/>.
/// </para>
/// </remarks>
[TestClass]
public sealed class DivergenceTests
{
	private const int Iss = 25544;

	[TestMethod]
	public void TheArchiveHoldsThreeRealIssSetsOldestFirst()
	{
		IReadOnlyList<ElementSet> history = IssHistory();

		Assert.HasCount(3, history);
		Assert.AreEqual(new DateTime(2026, 9, 15, 8, 51, 14, DateTimeKind.Utc), Truncate(history[0].Epoch));
		Assert.AreEqual(new DateTime(2026, 9, 15, 21, 14, 23, DateTimeKind.Utc), Truncate(history[1].Epoch));
		Assert.AreEqual(new DateTime(2026, 9, 22, 20, 26, 37, DateTimeKind.Utc), Truncate(history[2].Epoch));
	}

	[TestMethod]
	public void TheIssDivergesByElevenKilometresOverAWeek()
	{
		// The M2 number, end to end. Measured, then pinned to a metre so a change anywhere on the
		// path — store, reader, propagator, frame, or the order of the subtraction — shows up here.
		//
		//   2026-09-15 08:51 -> 15 21:14   0.516 d   R +0.0123  S  +0.2639  W +0.1004   |r|  0.283 km
		//   2026-09-15 21:14 -> 22 20:26   6.967 d   R +0.1543  S -10.8103  W +1.4979   |r| 10.915 km
		//
		// Eleven kilometres in a week, inside the 5 to 20 km the spec projects for Δ_model in LEO
		// over that arc, and two hundred times the 0.056 km Δ_data measures for one element set.
		IReadOnlyList<ElementSetDivergence<double>> pairs = Divergence<double>.Consecutive(IssHistory(), DoubleStorageMath.Instance);

		Assert.HasCount(2, pairs);

		ElementSetDivergence<double> halfDay = pairs[0];
		Assert.IsTrue(halfDay.IsSuccess, halfDay.Error.ToString());
		Assert.AreEqual(0.5161, halfDay.HorizonMinutes / 1440.0, 1e-4);
		Assert.AreEqual(0.0123, halfDay.Residual.Radial, 1e-3);
		Assert.AreEqual(0.2639, halfDay.Residual.AlongTrack, 1e-3);
		Assert.AreEqual(0.1004, halfDay.Residual.CrossTrack, 1e-3);
		Assert.AreEqual(0.2827, Kilometres(halfDay), 1e-3);

		ElementSetDivergence<double> week = pairs[1];
		Assert.IsTrue(week.IsSuccess, week.Error.ToString());
		Assert.AreEqual(6.9668, week.HorizonMinutes / 1440.0, 1e-4);
		Assert.AreEqual(0.1543, week.Residual.Radial, 1e-3);
		Assert.AreEqual(-10.8103, week.Residual.AlongTrack, 1e-3);
		Assert.AreEqual(1.4979, week.Residual.CrossTrack, 1e-3);
		Assert.AreEqual(10.9146, Kilometres(week), 1e-3);
	}

	[TestMethod]
	public void AlongTrackDominatesEveryPair()
	{
		// The reason residuals are resolved in RIC at all (domain trap 5): orbit error is mostly a
		// timing error. On the half-day pair the margin over cross-track is only 2.6x, so this is a
		// property of the data, asserted, rather than something the frame guarantees.
		foreach (ElementSetDivergence<double> pair in Divergence<double>.Consecutive(IssHistory(), DoubleStorageMath.Instance))
		{
			double along = Math.Abs(pair.Residual.AlongTrack);

			Assert.IsGreaterThan(Math.Abs(pair.Residual.Radial), along, $"radial over {pair.HorizonMinutes / 1440.0:F2} d");
			Assert.IsGreaterThan(Math.Abs(pair.Residual.CrossTrack), along, $"cross-track over {pair.HorizonMinutes / 1440.0:F2} d");
		}
	}

	[TestMethod]
	public void APredictionAheadOfTheReferenceReadsAsPositiveAlongTrack()
	{
		// The RswResidualTests convention, carried through the whole routine: the residual is the
		// prediction minus the reference, so a prediction further along the orbit is positive. A
		// hundredth of a degree more mean anomaly at the earlier epoch is a phase lead of about
		// 6780 km x 1.745e-4 rad = 1.18 km, and almost nothing else. Swapping which state is the
		// reference turns this negative, and it is the test that catches it.
		IReadOnlyList<ElementSet> history = IssHistory();
		ElementSet earlier = history[1];
		ElementSet ahead = earlier with { MeanAnomaly = earlier.MeanAnomaly + 0.01 };

		ElementSetDivergence<double> asWritten = Divergence<double>.Between(earlier, history[2], DoubleStorageMath.Instance);
		ElementSetDivergence<double> nudged = Divergence<double>.Between(ahead, history[2], DoubleStorageMath.Instance);

		double lead = nudged.Residual.AlongTrack - asWritten.Residual.AlongTrack;

		Assert.AreEqual(1.18, lead, 0.05, "along-track lead from a mean anomaly nudge");
		Assert.AreEqual(0.0, nudged.Residual.Radial - asWritten.Residual.Radial, 0.05, "radial barely moves");
	}

	[TestMethod]
	public void TheTypedFormsKeepTheSign()
	{
		// Domain trap 1. The week's along-track residual is negative — the prediction is behind — and
		// the vector forms carry that through, where a V0 subtraction would have lost it.
		ElementSetDivergence<double> week = Divergence<double>.Consecutive(IssHistory(), DoubleStorageMath.Instance)[1];

		Assert.AreEqual(week.Residual.Radial * 1000.0, week.PositionResidual.X, 1e-9);
		Assert.AreEqual(week.Residual.AlongTrack * 1000.0, week.PositionResidual.Y, 1e-9);
		Assert.AreEqual(week.Residual.CrossTrack * 1000.0, week.PositionResidual.Z, 1e-9);
		Assert.IsLessThan(0.0, week.PositionResidual.Y);

		Assert.AreEqual(week.Residual.RadialRate * 1000.0, week.VelocityResidual.X, 1e-12);
		Assert.AreEqual(week.Residual.AlongTrackRate * 1000.0, week.VelocityResidual.Y, 1e-12);
		Assert.AreEqual(week.Residual.CrossTrackRate * 1000.0, week.VelocityResidual.Z, 1e-12);

		Assert.AreEqual(week.HorizonMinutes, week.Horizon.In(Units.Minute), 1e-9);
	}

	[TestMethod]
	public void TheHorizonIsTheGapBetweenTheEpochs()
	{
		IReadOnlyList<ElementSet> history = IssHistory();
		ElementSetDivergence<double> span = Divergence<double>.Between(history[0], history[2], DoubleStorageMath.Instance);

		// DateTime truncates to its 100 ns tick, so it agrees with the two-part Julian dates to
		// about 2e-9 minutes and no better.
		Assert.AreEqual((history[2].Epoch - history[0].Epoch).TotalMinutes, span.HorizonMinutes, 1e-7);
	}

	[TestMethod]
	public void TheArithmeticIsNotWhatDiverges()
	{
		// The repository's central claim, at the scale of this measurement: double and a 30-digit
		// reference agree on the week's divergence to 1.7e-9 km, ten orders of magnitude below the
		// 10.8 km being measured. More digits do not make the prediction any better.
		IReadOnlyList<ElementSet> history = IssHistory();

		ElementSetDivergence<double> inDouble = Divergence<double>.Between(history[1], history[2], DoubleStorageMath.Instance);
		ElementSetDivergence<PreciseNumber> inPrecise = Divergence<PreciseNumber>.Between(history[1], history[2], new PreciseStorageMath(30));

		Assert.IsTrue(inPrecise.IsSuccess, inPrecise.Error.ToString());

		double difference = Math.Abs(inDouble.Residual.AlongTrack - inPrecise.Residual.AlongTrack.To<double>());

		Assert.IsLessThan(1e-7, difference, $"double and 30 digits differ by {difference:E2} km");
		Assert.IsGreaterThan(1e8, Math.Abs(inDouble.Residual.AlongTrack) / difference);
	}

	[TestMethod]
	public void APropagationFailureIsReportedNotThrown()
	{
		IReadOnlyList<ElementSet> history = IssHistory();
		ElementSet hyperbolic = history[1] with { Eccentricity = 1.5 };

		ElementSetDivergence<double> result = Divergence<double>.Between(hyperbolic, history[2], DoubleStorageMath.Instance);

		Assert.IsFalse(result.IsSuccess);
		Assert.AreEqual(Sgp4Error.EccentricityOutOfRange, result.Error);
	}

	[TestMethod]
	public void PairsThatAreNotEarlierThenLaterForOneObjectAreRefused()
	{
		IReadOnlyList<ElementSet> history = IssHistory();

		Assert.ThrowsExactly<ArgumentException>(
			() => Divergence<double>.Between(history[2], history[1], DoubleStorageMath.Instance));
		Assert.ThrowsExactly<ArgumentException>(
			() => Divergence<double>.Between(history[1], history[1], DoubleStorageMath.Instance));
		Assert.ThrowsExactly<ArgumentException>(
			() => Divergence<double>.Between(history[1], history[2] with { NoradCatalogId = 25545 }, DoubleStorageMath.Instance));
	}

	[TestMethod]
	public void AHistoryTooShortToPairHasNoDivergences()
	{
		Assert.IsEmpty(Divergence<double>.Consecutive([], DoubleStorageMath.Instance));
		Assert.IsEmpty(Divergence<double>.Consecutive([IssHistory()[0]], DoubleStorageMath.Instance));
	}

	private static IReadOnlyList<ElementSet> IssHistory() =>
		new SnapshotStore(Path.Join(VerificationSet.DataDirectory, "iss-snapshots")).History(Iss);

	private static double Kilometres(ElementSetDivergence<double> divergence) =>
		divergence.Residual.Magnitude(DoubleStorageMath.Instance).In(Units.Kilometer);

	private static DateTime Truncate(DateTime instant) =>
		new(instant.Ticks - (instant.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
}
