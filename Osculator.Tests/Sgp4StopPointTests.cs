// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Checks that the propagator stops exactly where the published verification output stops.
/// </summary>
/// <remarks>
/// <para>
/// The gate-1 suite propagates to every row the published output contains, which proves the model
/// is right wherever it answers. It says nothing about where it refuses to: the reference harness
/// stops writing rows for a case the moment the model returns an error, so a case whose last row is
/// short of its stop time is a published statement that the model must refuse at the next step.
/// Without this class every error-code guard in <see cref="Sgp4{T}.Propagate"/> could be deleted and
/// the suite would still pass, returning a garbage state as a success past the point the reference
/// gave up — which is exactly what the file's "check error code" cases were constructed to catch.
/// </para>
/// <para>
/// Mutation-checked against all three guards that answer at propagation time, each deleted in turn
/// and the suite run in all three storage types: the semi-latus rectum guard (33333 then returns
/// success), the eccentricity guard (22312 and 28350 return success), and the decay check (28872,
/// 29141 and the long arc of 20413 return success under the Earth's surface).
/// </para>
/// <para>
/// One half of the eccentricity guard is not reached by anything here, and it is worth knowing which.
/// Both near-earth cases stop because drag drives the eccentricity <em>below</em> −0.001; deleting
/// only the <c>em &gt;= 1</c> half leaves every test passing. No published case pushes the
/// eccentricity up through one, so that half is pinned by nothing in the verification set.
/// </para>
/// </remarks>
[TestClass]
public sealed class Sgp4StopPointTests
{
	/// <summary>Two published minute values closer than this are the same row.</summary>
	private const double SameMinuteTolerance = 1e-6;

	/// <summary>The working precision of the arbitrary-precision run, in significant digits.</summary>
	private const int PreciseDigits = 30;

	/// <summary>
	/// Every case whose published output ends before its stop time, the last minute published, and
	/// the error the model returns one grid step later.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Keyed by start time as well as catalogue number because object 20413 appears twice, once over
	/// a published arc it completes and once three and a half years out, where it decays.
	/// </para>
	/// <para>
	/// The verification file names a code for only one of these: 33333's comment reads "check error
	/// code 4". The other codes are what this implementation returns, which matches what the
	/// physics says each case is — two near-earth objects whose drag-reduced eccentricity reaches
	/// one, and three that re-enter. 33334's single published row is a stale buffer from the
	/// reference harness rather than a state (see <see cref="Sgp4VerificationTests"/>), so its model
	/// output really ends before its first row, and it is refused at every time.
	/// </para>
	/// </remarks>
	private static readonly StopPoint[] ExpectedStopPoints =
	[
		new(22312, 54.2028672, 474.2028672, Sgp4Error.EccentricityOutOfRange),
		new(28350, 0.0, 1440.0, Sgp4Error.EccentricityOutOfRange),
		new(28872, 0.0, 50.0, Sgp4Error.Decayed),
		new(29141, 0.0, 420.0, Sgp4Error.Decayed),
		new(33333, 0.0, 20.0, Sgp4Error.SemiLatusRectumNegative),
		new(33334, 0.0, 0.0, Sgp4Error.PerturbedEccentricityOutOfRange),
		new(20413, 1844000.0, 1844340.0, Sgp4Error.Decayed),
	];

	[TestMethod]
	public void TheCasesThatStopEarlyAreExactlyTheKnownOnes()
	{
		// Pins the data as well as the model: a case that starts or stops ending early, or a stop
		// point that moves, shows up here by name rather than as an unexplained gate-1 change.
		List<string> found = [];

		foreach ((VerificationSet.Case verification, IReadOnlyList<VerificationSet.Expected> rows) in Cases())
		{
			double publishedTo = rows[^1].Minutes;

			if (System.Math.Abs(publishedTo - verification.StopMinutes) > SameMinuteTolerance)
			{
				found.Add(Describe(verification.Elements.NoradCatalogId, verification.StartMinutes, publishedTo));
			}
		}

		List<string> expected = [.. Array.ConvertAll(ExpectedStopPoints, s => Describe(s.CatalogId, s.StartMinutes, s.PublishedTo))];

		CollectionAssert.AreEquivalent(expected, found, $"Cases ending early: {string.Join(", ", found)}");
	}

	[TestMethod]
	public void DoubleStopsWherePublishedOutputStops() =>
		AssertStopPoints(
			e => Sgp4<double>.Initialize(e, DoubleStorageMath.Instance),
			(s, minutes) => Sgp4<double>.Propagate(s, minutes, DoubleStorageMath.Instance).Error);

	[TestMethod]
	public void DecimalStopsWherePublishedOutputStops() =>
		AssertStopPoints(
			e => Sgp4<decimal>.Initialize(e, DecimalStorageMath.Instance),
			(s, minutes) => Sgp4<decimal>.Propagate(s, (decimal)minutes, DecimalStorageMath.Instance).Error);

	[TestMethod]
	public void PreciseNumberStopsWherePublishedOutputStops()
	{
		PreciseStorageMath precise = new(PreciseDigits);

		AssertStopPoints(
			e => Sgp4<PreciseNumber>.Initialize(e, precise),
			(s, minutes) => Sgp4<PreciseNumber>.Propagate(s, minutes.ToPreciseNumber(), precise).Error);
	}

	/// <summary>
	/// For every known stop point, asserts the last published minute succeeds and the next grid step
	/// returns the expected error.
	/// </summary>
	/// <typeparam name="TSatellite">The initialized satellite type.</typeparam>
	/// <param name="initialize">Initializes a satellite in the storage type under test.</param>
	/// <param name="propagate">Propagates it to a time in minutes and returns the error code.</param>
	private static void AssertStopPoints<TSatellite>(
		Func<Core.Elements.ElementSet, TSatellite> initialize,
		Func<TSatellite, double, Sgp4Error> propagate)
	{
		List<VerificationSet.Case> cases = VerificationSet.ReadCases();
		List<string> failures = [];

		foreach (StopPoint stop in ExpectedStopPoints)
		{
			VerificationSet.Case verification = cases.Find(c => c.Elements.NoradCatalogId == stop.CatalogId && System.Math.Abs(c.StartMinutes - stop.StartMinutes) < SameMinuteTolerance)
				?? throw new InvalidOperationException($"{Describe(stop.CatalogId, stop.StartMinutes, stop.PublishedTo)} is no longer in the verification file.");

			TSatellite satellite = initialize(verification.Elements);

			// The last published row has to be a state, or the stop is in the wrong place the other
			// way. The one exception is the harness artifact, whose row was never a model output.
			if (stop.Error != Sgp4Error.PerturbedEccentricityOutOfRange)
			{
				Sgp4Error atLastRow = propagate(satellite, stop.PublishedTo);

				if (atLastRow != Sgp4Error.None)
				{
					failures.Add($"{stop.CatalogId} at its last published minute {stop.PublishedTo}: {atLastRow}, expected a state");
				}
			}

			double next = System.Math.Min(stop.PublishedTo + verification.StepMinutes, verification.StopMinutes);
			Sgp4Error atNextStep = propagate(satellite, next);

			if (atNextStep != stop.Error)
			{
				failures.Add($"{stop.CatalogId} at {next.ToString(CultureInfo.InvariantCulture)} min: {atNextStep}, expected {stop.Error}");
			}
		}

		Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
	}

	private static IEnumerable<(VerificationSet.Case Case, IReadOnlyList<VerificationSet.Expected> Rows)> Cases()
	{
		List<VerificationSet.Case> cases = VerificationSet.ReadCases();
		List<IReadOnlyList<VerificationSet.Expected>> blocks = VerificationSet.ReadExpected();

		Assert.AreEqual(cases.Count, blocks.Count, "Case count and expected-block count disagree; the two files are out of step.");

		for (int i = 0; i < cases.Count; i++)
		{
			yield return (cases[i], blocks[i]);
		}
	}

	private static string Describe(int catalogId, double startMinutes, double publishedTo) =>
		string.Create(CultureInfo.InvariantCulture, $"{catalogId} from {startMinutes} published to {publishedTo}");

	/// <summary>Where one verification case's published output ends early, and why.</summary>
	/// <param name="CatalogId">The catalogue number.</param>
	/// <param name="StartMinutes">The case's start time, distinguishing two runs of one object.</param>
	/// <param name="PublishedTo">The last minute the published output has a row for.</param>
	/// <param name="Error">The error the model returns at the next grid step.</param>
	private sealed record StopPoint(int CatalogId, double StartMinutes, double PublishedTo, Sgp4Error Error);
}
