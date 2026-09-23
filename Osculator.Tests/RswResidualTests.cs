// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Generic;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Residuals;
using ktsu.Semantics.Quantities;
using ktsu.Semantics.Quantities.Units;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the RIC/RSW frame and the residual resolved in it.
/// </summary>
/// <remarks>
/// Nothing in the dimensions catches a sign or an axis order — a cross product and its negation
/// have identical dimensions — so the conventions are pinned here by construction rather than
/// trusted to the type system. Each of the three axes is checked against a state chosen so that
/// the right answer is obvious by inspection.
/// </remarks>
[TestClass]
public sealed class RswResidualTests
{
	/// <summary>A circular equatorial orbit whose frame is the identity, to within a naming.</summary>
	/// <remarks>
	/// Position along x, velocity along y. So R is x, W (= r x v) is z, and S (= W x R) is y. Every
	/// convention below reads off this state directly, which is the point of choosing it.
	/// </remarks>
	private static readonly TemeState<double> Circular = new(7000.0, 0.0, 0.0, 0.0, 7.5, 0.0);

	[TestMethod]
	public void TheAxesAreTheOnesTheNameSays()
	{
		RswBasis<double> basis = RswBasis<double>.Of(Circular, DoubleStorageMath.Instance);

		AssertAxis((1, 0, 0), basis.Radial, "radial");
		AssertAxis((0, 1, 0), basis.AlongTrack, "along-track");
		AssertAxis((0, 0, 1), basis.CrossTrack, "cross-track");
	}

	[TestMethod]
	public void ASatelliteAheadOfTheReferenceHasAPositiveAlongTrackResidual()
	{
		// The sign that matters operationally: "ahead" has to mean ahead. W x R rather than R x W
		// is the whole of this, and the dimensions are identical either way round.
		TemeState<double> ahead = Circular with { Y = Circular.Y + 1.0 };

		RswResidual<double> residual = RswResidual<double>.Between(Circular, ahead, DoubleStorageMath.Instance);

		Assert.AreEqual(1.0, residual.AlongTrack, 1e-12);
		Assert.AreEqual(0.0, residual.Radial, 1e-12);
		Assert.AreEqual(0.0, residual.CrossTrack, 1e-12);
	}

	[TestMethod]
	public void ASatelliteFurtherOutHasAPositiveRadialResidual()
	{
		TemeState<double> higher = Circular with { X = Circular.X + 1.0 };

		RswResidual<double> residual = RswResidual<double>.Between(Circular, higher, DoubleStorageMath.Instance);

		Assert.AreEqual(1.0, residual.Radial, 1e-12);
		Assert.AreEqual(0.0, residual.AlongTrack, 1e-12);
	}

	[TestMethod]
	public void ASatelliteAboveTheOrbitPlaneHasAPositiveCrossTrackResidual()
	{
		TemeState<double> above = Circular with { Z = Circular.Z + 1.0 };

		RswResidual<double> residual = RswResidual<double>.Between(Circular, above, DoubleStorageMath.Instance);

		Assert.AreEqual(1.0, residual.CrossTrack, 1e-12);
	}

	[TestMethod]
	public void TheFrameIsOrthonormalOnAnEccentricInclinedOrbit()
	{
		// The equatorial circular state above would pass an orthonormality check written wrongly,
		// because its answer is the identity. This one would not.
		TemeState<double> awkward = new(4821.7, -3112.4, 5908.2, 3.114, 6.402, -1.877);

		RswBasis<double> basis = RswBasis<double>.Of(awkward, DoubleStorageMath.Instance);

		Assert.AreEqual(1.0, Dot(basis.Radial, basis.Radial), 1e-14);
		Assert.AreEqual(1.0, Dot(basis.AlongTrack, basis.AlongTrack), 1e-14);
		Assert.AreEqual(1.0, Dot(basis.CrossTrack, basis.CrossTrack), 1e-14);
		Assert.AreEqual(0.0, Dot(basis.Radial, basis.AlongTrack), 1e-14);
		Assert.AreEqual(0.0, Dot(basis.Radial, basis.CrossTrack), 1e-14);
		Assert.AreEqual(0.0, Dot(basis.AlongTrack, basis.CrossTrack), 1e-14);

		// Right-handed: R x S = W. Left-handed would satisfy every assertion above.
		(double X, double Y, double Z) rCrossS = (
			(basis.Radial.Y * basis.AlongTrack.Z) - (basis.Radial.Z * basis.AlongTrack.Y),
			(basis.Radial.Z * basis.AlongTrack.X) - (basis.Radial.X * basis.AlongTrack.Z),
			(basis.Radial.X * basis.AlongTrack.Y) - (basis.Radial.Y * basis.AlongTrack.X));

		Assert.AreEqual(1.0, Dot(rCrossS, basis.CrossTrack), 1e-14);
	}

	[TestMethod]
	public void TheAlongTrackAxisIsNotTheVelocityDirectionOnAnEccentricOrbit()
	{
		// Documented on RswBasis as a trap, so it is measured rather than asserted. S is
		// perpendicular to R by construction; the velocity is not, wherever the orbit is eccentric,
		// and the two differ by the flight path angle.
		TemeState<double> circular = Circular;
		TemeState<double> eccentric = new(9200.0, 0.0, 0.0, 2.9, 5.4, 0.0);

		Assert.AreEqual(1.0, AlongTrackAgreementWithVelocity(circular), 1e-12,
			"A circular orbit is the case where the two do coincide.");

		double agreement = AlongTrackAgreementWithVelocity(eccentric);

		Assert.IsLessThan(0.99, agreement,
			$"S and the velocity direction agree to {agreement}, which would make the two interchangeable.");
	}

	[TestMethod]
	public void ATimingErrorShowsUpAsTheVelocityResolvedInTheFrame()
	{
		// The property the frame exists for. A small timing offset moves the satellite along its
		// own trajectory, so to first order the residual is the velocity times the offset — and
		// resolving that in the frame is what turns a number that changes all the way round the
		// orbit into one that does not.
		//
		// First order, so the tolerance has to track the second-order term. At a one-second offset
		// the acceleration contributes about 0.003 km, which is a tenth of a percent of the
		// along-track component but nine percent of the much smaller radial one — so a tolerance
		// that looked generous against the residual's magnitude failed on radial. A tenth of a
		// second shrinks it a hundredfold, which is what makes 1e-4 km a fair test rather than a
		// loosened one.
		const double offsetSeconds = 0.1;
		Sgp4Satellite<double> satellite = FirstVerificationCase();
		TemeState<double> reference = StateAt(satellite, 60.0);
		TemeState<double> later = StateAt(satellite, 60.0 + (offsetSeconds / 60.0));

		RswResidual<double> residual = RswResidual<double>.Between(reference, later, DoubleStorageMath.Instance);
		RswBasis<double> basis = RswBasis<double>.Of(reference, DoubleStorageMath.Instance);

		(double radialRate, double alongTrackRate, double crossTrackRate) = basis.Resolve(
			reference.VelocityX, reference.VelocityY, reference.VelocityZ);

		Assert.AreEqual(radialRate * offsetSeconds, residual.Radial, 1e-4, "radial");
		Assert.AreEqual(alongTrackRate * offsetSeconds, residual.AlongTrack, 1e-4, "along-track");
		// Cross-track is deliberately not asserted here. It should be zero — the stated velocity is
		// perpendicular to the orbit normal by the definition of that normal — and it measurably is
		// not, for a reason that belongs to SGP4 rather than to this frame. See the next test.
	}

	[TestMethod]
	public void Sgp4sStatedVelocityIsNotTheDerivativeOfItsStatedPosition()
	{
		// Found by the test above failing a cross-track assertion that looked unarguable: the
		// cross-track axis is built from r x v, so the stated velocity has no cross-track component
		// by construction, and yet differencing two propagated positions produces one.
		//
		// It is linear in the offset, not quadratic — measured at 1, 0.5, 0.25 and 0.125 seconds,
		// where the ratio to the offset held at 9.61e-4 km/s across the whole range while the ratio
		// to its square moved by a factor of eight. So it is a velocity, not an acceleration: the
		// model's reported velocity is not the exact time derivative of its reported position,
		// because the periodic corrections' own time derivatives are only partly carried into the
		// velocity formulas. A central difference confirms it directly, disagreeing with the stated
		// velocity by about 1.2e-3 km/s.
		//
		// This matters here beyond being a curiosity. It is around a metre per second of Δ_model,
		// which is seven orders of magnitude above double's arithmetic error — so a residual built
		// by differencing positions and one built from the stated velocity are different
		// measurements, and which is used has to be a decision rather than an accident.
		Sgp4Satellite<double> satellite = FirstVerificationCase();
		TemeState<double> reference = StateAt(satellite, 60.0);
		List<double> ratesPerSecond = [];

		foreach (double seconds in (double[])[1.0, 0.5, 0.25, 0.125])
		{
			TemeState<double> later = StateAt(satellite, 60.0 + (seconds / 60.0));
			RswResidual<double> residual = RswResidual<double>.Between(reference, later, DoubleStorageMath.Instance);

			ratesPerSecond.Add(residual.CrossTrack / seconds);
		}

		foreach (double rate in ratesPerSecond)
		{
			Assert.AreEqual(ratesPerSecond[0], rate, 1e-6,
				"Linear in the offset: an out-of-plane velocity, not an out-of-plane acceleration.");
		}

		Assert.IsGreaterThan(1e-4, System.Math.Abs(ratesPerSecond[0]),
			"The inconsistency is real and around a metre per second, not round-off.");
	}

	[TestMethod]
	public void HowMuchOfATimingErrorIsAlongTrackIsAQuestionAboutEccentricity()
	{
		// "A timing error is purely along-track" is the usual shorthand and it is exactly true only
		// where the orbit is locally circular. The first verification case is Vanguard 1 at e≈0.19,
		// so its timing residual has a real radial part, and pinning that here is what stops the
		// shorthand being written into the library as though it were the general case.
		Sgp4Satellite<double> satellite = FirstVerificationCase();
		double worstRadialShare = 0.0;

		for (double minutes = 0.0; minutes <= 240.0; minutes += 5.0)
		{
			TemeState<double> reference = StateAt(satellite, minutes);
			TemeState<double> later = StateAt(satellite, minutes + (1.0 / 60.0));
			RswResidual<double> residual = RswResidual<double>.Between(reference, later, DoubleStorageMath.Instance);
			double magnitude = residual.Magnitude(DoubleStorageMath.Instance).In(Units.Kilometer);

			worstRadialShare = System.Math.Max(worstRadialShare, System.Math.Abs(residual.Radial) / magnitude);
		}

		Assert.IsGreaterThan(0.05, worstRadialShare,
			$"An eccentric orbit's timing residual is not purely along-track; the worst radial share here is {worstRadialShare:F3}.");
	}

	[TestMethod]
	public void TheResidualIsResolvedInTheReferencesFrameAndNotSomeAverage()
	{
		// Swapping the two states is not a sign flip, because the frame goes with the reference.
		// If it were exactly a sign flip the frame would be shared, and a residual would mean
		// something different from what it is documented to mean.
		Sgp4Satellite<double> satellite = FirstVerificationCase();
		TemeState<double> a = StateAt(satellite, 60.0);
		TemeState<double> b = StateAt(satellite, 75.0);

		RswResidual<double> forward = RswResidual<double>.Between(a, b, DoubleStorageMath.Instance);
		RswResidual<double> backward = RswResidual<double>.Between(b, a, DoubleStorageMath.Instance);

		Assert.AreNotEqual(forward.Radial, -backward.Radial, 1e-6);
	}

	[TestMethod]
	public void AStateWithNoOrbitCarriesNoFrame()
	{
		Assert.ThrowsExactly<ArgumentException>(
			() => RswBasis<double>.Of(new TemeState<double>(0, 0, 0, 1, 0, 0), DoubleStorageMath.Instance));

		// Position and velocity parallel: a purely radial trajectory, which has no orbit normal.
		Assert.ThrowsExactly<ArgumentException>(
			() => RswBasis<double>.Of(new TemeState<double>(7000, 0, 0, 1.5, 0, 0), DoubleStorageMath.Instance));
	}

	[TestMethod]
	public void TheTypedAccessorsKeepTheSignThatTheMagnitudeFormsWouldHaveLost()
	{
		// Domain trap 1, demonstrated rather than described. The residual is a difference, and the
		// V0 forms define subtraction as T.Abs(a - b), so a residual built from Length or Speed is
		// silently unsigned — which loses the distinction between ahead and behind entirely.
		TemeState<double> behind = Circular with { Y = Circular.Y - 4.0 };
		RswResidual<double> residual = RswResidual<double>.Between(Circular, behind, DoubleStorageMath.Instance);

		Assert.AreEqual(-4.0, residual.AlongTrackDisplacement.In(Units.Kilometer), 1e-12);

		Length<double> reference = Length<double>.FromKilometer(7000.0);
		Length<double> test = Length<double>.FromKilometer(6996.0);

		Assert.AreEqual(4.0, (reference - test).In(Units.Kilometer), 1e-12,
			"V0 subtraction is the absolute difference, which is why the accessor above is a V1.");
	}

	[TestMethod]
	public void TheVelocityAccessorsConvertKilometresPerSecondCorrectly()
	{
		// The library has no kilometre-per-second factory, so this conversion is written by hand
		// and is therefore worth pinning.
		RswResidual<double> residual = new(0, 0, 0, -0.25, 1.5, 0);

		Assert.AreEqual(-250.0, residual.RadialVelocity.In(Units.MeterPerSecond), 1e-9);
		Assert.AreEqual(1500.0, residual.AlongTrackVelocity.In(Units.MeterPerSecond), 1e-9);
	}

	private static double AlongTrackAgreementWithVelocity(TemeState<double> state)
	{
		RswBasis<double> basis = RswBasis<double>.Of(state, DoubleStorageMath.Instance);
		double speed = System.Math.Sqrt(
			(state.VelocityX * state.VelocityX)
			+ (state.VelocityY * state.VelocityY)
			+ (state.VelocityZ * state.VelocityZ));

		return Dot(basis.AlongTrack, (state.VelocityX / speed, state.VelocityY / speed, state.VelocityZ / speed));
	}

	private static double Dot((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
		(a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

	private static void AssertAxis((double X, double Y, double Z) expected, (double X, double Y, double Z) actual, string name)
	{
		Assert.AreEqual(expected.X, actual.X, 1e-14, $"{name} x");
		Assert.AreEqual(expected.Y, actual.Y, 1e-14, $"{name} y");
		Assert.AreEqual(expected.Z, actual.Z, 1e-14, $"{name} z");
	}

	private static Sgp4Satellite<double> FirstVerificationCase()
	{
		List<VerificationSet.Case> cases = VerificationSet.ReadCases();
		return Sgp4<double>.Initialize(cases[0].Elements, DoubleStorageMath.Instance);
	}

	private static TemeState<double> StateAt(Sgp4Satellite<double> satellite, double minutes)
	{
		Sgp4Result<double> result = Sgp4<double>.Propagate(satellite, minutes, DoubleStorageMath.Instance);
		Assert.IsTrue(result.IsSuccess, $"Propagation failed at {minutes} min: {result.Error}");
		return result.State;
	}
}
