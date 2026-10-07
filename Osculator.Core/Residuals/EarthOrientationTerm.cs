// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Residuals;

using System;
using System.Collections.Generic;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;

/// <summary>
/// How far a state's Earth-fixed position is uncertain purely because the Earth's orientation is
/// only known to within the IERS's stated sigmas.
/// </summary>
/// <remarks>
/// <para>
/// This is the part of <strong>Δ_data</strong> that is not in the element set. A residual taken in
/// the ITRF — against SP3 truth, or against a later element set rotated to the ground — carries
/// every metre the orientation is uncertain by, and nothing downstream can tell that apart from
/// the model being wrong. Spec §11 names it as the term that would silently contaminate Δ_model;
/// measuring it is how it stays out.
/// </para>
/// <para>
/// <strong>One term at a time, then in quadrature,</strong> for the same reasons as
/// <see cref="DataTerm"/>: the IERS solves for the pole and for UT1 − UTC from different
/// techniques, so their errors are close enough to independent, and one combined perturbation
/// would measure a corner of the band rather than its radius.
/// </para>
/// <para>
/// <strong>Plus and minus one sigma, halved.</strong> Each contribution is half the distance
/// between the positions rotated with the value one sigma either side. The rotation is linear in
/// the angle at these sizes — a sigma of 0.025 s is 4e-7 radians of the Earth's turn — so this is
/// the same as one-sided, and it is symmetric by construction rather than by that argument.
/// </para>
/// <para>
/// <strong>A final row's UT1 term is at the edge of what the sidereal angle resolves.</strong> A
/// final sigma is about 26 µs, and while <c>EarthFixedFrame.SiderealAngle</c> adds UT1 − UTC into
/// one <see langword="double"/> Julian date (#51) it resolves about 40 µs, so that term reads up to
/// half again too large. It is a centimetre either way; the fix belongs to the sidereal angle, not
/// here, and this measurement sharpens on its own when that lands.
/// </para>
/// <para>
/// <see langword="double"/> rather than the generic storage type, as in <see cref="DataTerm"/>:
/// this is a yardstick, and the quantity measured is centimetres to metres, nine orders above the
/// arithmetic error of the type measuring it.
/// </para>
/// </remarks>
public static class EarthOrientationTerm
{
	/// <summary>
	/// One orientation parameter's contribution.
	/// </summary>
	/// <param name="Term">The parameter that was perturbed.</param>
	/// <param name="Sigma">
	/// The IERS's stated one-sigma uncertainty, in the parameter's own units: seconds for
	/// UT1 − UTC, arcseconds for the pole.
	/// </param>
	/// <param name="PositionKilometers">
	/// How far the ITRF position moves per sigma of that parameter.
	/// </param>
	public readonly record struct Contribution(string Term, double Sigma, double PositionKilometers);

	/// <summary>
	/// Measures the orientation term for one state at one instant.
	/// </summary>
	/// <param name="state">The state, in TEME.</param>
	/// <param name="epoch">The instant the state is for, on the UTC scale.</param>
	/// <param name="orientation">The orientation, with its sigmas, from an IERS bulletin.</param>
	/// <param name="math">The transcendental functions.</param>
	/// <returns>Each parameter's contribution, largest first.</returns>
	/// <exception cref="ArgumentException">
	/// A sigma is unknown (<see cref="double.NaN"/>) or negative. An unknown sigma is refused rather
	/// than read as zero, because zero would claim the orientation is known exactly, and the whole
	/// point is that it is not.
	/// </exception>
	public static IReadOnlyList<Contribution> Measure(
		TemeState<double> state,
		JulianDate epoch,
		EarthOrientation orientation,
		IStorageMath<double> math)
	{
		Ensure.NotNull(math);

		RequireKnown(orientation.Ut1MinusUtcSigmaSeconds, "UT1 - UTC", nameof(orientation));
		RequireKnown(orientation.PoleXSigmaArcseconds, "pole x", nameof(orientation));
		RequireKnown(orientation.PoleYSigmaArcseconds, "pole y", nameof(orientation));

		double ut1Sigma = orientation.Ut1MinusUtcSigmaSeconds;
		double xSigma = orientation.PoleXSigmaArcseconds;
		double ySigma = orientation.PoleYSigmaArcseconds;

		List<Contribution> contributions =
		[
			new("Ut1MinusUtc", ut1Sigma, HalfSpread(
				state, epoch, math,
				orientation with { Ut1MinusUtcSeconds = orientation.Ut1MinusUtcSeconds + ut1Sigma },
				orientation with { Ut1MinusUtcSeconds = orientation.Ut1MinusUtcSeconds - ut1Sigma })),
			new("PoleX", xSigma, HalfSpread(
				state, epoch, math,
				orientation with { PoleXArcseconds = orientation.PoleXArcseconds + xSigma },
				orientation with { PoleXArcseconds = orientation.PoleXArcseconds - xSigma })),
			new("PoleY", ySigma, HalfSpread(
				state, epoch, math,
				orientation with { PoleYArcseconds = orientation.PoleYArcseconds + ySigma },
				orientation with { PoleYArcseconds = orientation.PoleYArcseconds - ySigma })),
		];

		contributions.Sort((a, b) => b.PositionKilometers.CompareTo(a.PositionKilometers));

		return contributions;
	}

	/// <summary>
	/// Combines per-parameter contributions into one number.
	/// </summary>
	/// <param name="contributions">The contributions, from <see cref="Measure"/>.</param>
	/// <returns>The orientation term, in kilometres.</returns>
	public static double CombineInQuadrature(IReadOnlyList<Contribution> contributions)
	{
		Ensure.NotNull(contributions);

		double sumOfSquares = 0.0;

		for (int i = 0; i < contributions.Count; i++)
		{
			sumOfSquares += contributions[i].PositionKilometers * contributions[i].PositionKilometers;
		}

		return System.Math.Sqrt(sumOfSquares);
	}

	private static void RequireKnown(double sigma, string name, string parameterName)
	{
		if (double.IsNaN(sigma) || sigma < 0.0)
		{
			throw new ArgumentException(
				$"The {name} sigma is {sigma}; an orientation with no stated uncertainty cannot be measured, and reading it as zero would claim it is known exactly.",
				parameterName);
		}
	}

	private static double HalfSpread(
		TemeState<double> state,
		JulianDate epoch,
		IStorageMath<double> math,
		EarthOrientation plus,
		EarthOrientation minus)
	{
		ItrfState<double> a = EarthFixedFrame<double>.ToItrf(state, epoch, plus, math);
		ItrfState<double> b = EarthFixedFrame<double>.ToItrf(state, epoch, minus, math);

		double dx = a.X - b.X;
		double dy = a.Y - b.Y;
		double dz = a.Z - b.Z;

		return System.Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) / 2.0;
	}
}
