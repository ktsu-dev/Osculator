// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Residuals;

using System;
using System.Collections.Generic;
using System.Numerics;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Semantics.Quantities;

/// <summary>
/// How far an element set's prediction has drifted from a later element set for the same object,
/// at the later set's epoch.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="Earlier">The element set propagated forward: the prediction.</param>
/// <param name="Later">The element set taken as the reference, evaluated at its own epoch.</param>
/// <param name="HorizonMinutes">How far the earlier set was propagated, in minutes.</param>
/// <param name="Error">
/// Why either propagation failed, or <see cref="Sgp4Error.None"/>. The earlier set's error wins
/// when both fail, because it is the one carried over the longer arc.
/// </param>
/// <param name="Residual">
/// The prediction minus the reference, in the reference's RIC frame. Meaningful only when
/// <paramref name="Error"/> is <see cref="Sgp4Error.None"/>.
/// </param>
/// <remarks>
/// <para>
/// <strong>This is model error and data error together, not SGP4's error.</strong> The later
/// element set is not the truth: it is another fit of the same model to later observations, written
/// in the same quantized digits. So the residual mixes Δ_model (SGP4 is a curve fit, and the
/// earlier fit has had the horizon to wander from the orbit), Δ_data on both ends (each set is one
/// of a band that would have been written identically), and the fit residual of each set against
/// the observations behind it. Separating those is M5's decomposition, not this type's.
/// </para>
/// <para>
/// <strong>The rate components use SGP4's stated velocity, for both states.</strong> CLAUDE.md
/// domain trap 13: the model's velocity is not the exact time derivative of its position, by about
/// a metre per second. Differencing positions would answer a different question — "how fast is the
/// position residual changing" — and would need a third propagation per state to answer it. The
/// stated velocity is what an element-set consumer receives and what every published comparison
/// quotes, so it is the one used here, deliberately. The position components are unaffected by the
/// choice.
/// </para>
/// </remarks>
public readonly record struct ElementSetDivergence<T>(
	ElementSet Earlier,
	ElementSet Later,
	T HorizonMinutes,
	Sgp4Error Error,
	RswResidual<T> Residual)
	where T : struct, INumber<T>
{
	/// <summary>Gets a value indicating whether both propagations produced a usable state.</summary>
	public bool IsSuccess => Error == Sgp4Error.None;

	/// <summary>Gets the horizon as a typed duration.</summary>
	public Duration<T> Horizon => Duration<T>.FromMinute(HorizonMinutes);

	/// <summary>
	/// Gets the position residual as a signed vector whose components are radial, along-track and
	/// cross-track, in that order.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A <see cref="Displacement3D{T}"/> rather than three magnitudes, because each component is a
	/// signed difference (domain trap 1). Its X, Y and Z are R, S and W — not TEME axes.
	/// </para>
	/// <para>
	/// Built in metres through the component initializers because the vector forms have no
	/// <c>FromKilometer</c> in the Semantics version this repository pins (Semantics#237).
	/// </para>
	/// </remarks>
	public Displacement3D<T> PositionResidual
	{
		get
		{
			T metersPerKilometer = T.CreateChecked(1000);
			return new Displacement3D<T>
			{
				X = Residual.Radial * metersPerKilometer,
				Y = Residual.AlongTrack * metersPerKilometer,
				Z = Residual.CrossTrack * metersPerKilometer,
			};
		}
	}

	/// <summary>
	/// Gets the velocity residual as a signed vector whose components are radial, along-track and
	/// cross-track, in that order.
	/// </summary>
	public Velocity3D<T> VelocityResidual
	{
		get
		{
			T metersPerKilometer = T.CreateChecked(1000);
			return new Velocity3D<T>
			{
				X = Residual.RadialRate * metersPerKilometer,
				Y = Residual.AlongTrackRate * metersPerKilometer,
				Z = Residual.CrossTrackRate * metersPerKilometer,
			};
		}
	}
}

/// <summary>
/// Measures how far one element set's prediction drifts from a later element set's.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// The measurement M2 is gated on, and the one CelesTrak's history makes possible: propagate an
/// archived element set to a later set's epoch, evaluate the later set at its own epoch, and resolve
/// the difference in the later set's frame. See <see cref="ElementSetDivergence{T}"/> for what the
/// number does and does not mean.
/// </remarks>
public static class Divergence<T>
	where T : struct, INumber<T>
{
	/// <summary>The minutes in a day.</summary>
	private const int MinutesPerDay = 1440;

	/// <summary>
	/// Propagates <paramref name="earlier"/> to <paramref name="later"/>'s epoch and compares.
	/// </summary>
	/// <param name="earlier">The element set to propagate: the prediction.</param>
	/// <param name="later">The element set to compare against, at its own epoch.</param>
	/// <param name="math">The storage type's transcendentals.</param>
	/// <returns>The divergence, which carries an error rather than throwing when SGP4 refuses.</returns>
	/// <exception cref="ArgumentNullException">An argument is null.</exception>
	/// <exception cref="ArgumentException">
	/// The two sets describe different objects, or <paramref name="later"/> is not after
	/// <paramref name="earlier"/>.
	/// </exception>
	public static ElementSetDivergence<T> Between(ElementSet earlier, ElementSet later, IStorageMath<T> math)
	{
		Ensure.NotNull(earlier);
		Ensure.NotNull(later);
		Ensure.NotNull(math);

		if (earlier.NoradCatalogId != later.NoradCatalogId)
		{
			throw new ArgumentException(
				$"Catalogue numbers differ ({earlier.NoradCatalogId} and {later.NoradCatalogId}); a divergence compares one object with itself.",
				nameof(later));
		}

		T horizon = MinutesBetween(earlier, later);

		if (horizon <= T.Zero)
		{
			throw new ArgumentException(
				$"The later element set's epoch ({later.Epoch:O}) is not after the earlier one's ({earlier.Epoch:O}).",
				nameof(later));
		}

		Sgp4Result<T> prediction = Sgp4<T>.Propagate(Sgp4<T>.Initialize(earlier, math), horizon, math);
		Sgp4Result<T> reference = Sgp4<T>.Propagate(Sgp4<T>.Initialize(later, math), T.Zero, math);

		if (!prediction.IsSuccess || !reference.IsSuccess)
		{
			Sgp4Error error = prediction.IsSuccess ? reference.Error : prediction.Error;
			return new ElementSetDivergence<T>(earlier, later, horizon, error, default);
		}

		return new ElementSetDivergence<T>(
			earlier,
			later,
			horizon,
			Sgp4Error.None,
			RswResidual<T>.Between(reference.State, prediction.State, math));
	}

	/// <summary>
	/// Measures every consecutive pair in an object's history.
	/// </summary>
	/// <param name="history">
	/// One object's element sets, oldest first, as <c>SnapshotStore.History</c> returns them.
	/// </param>
	/// <param name="math">The storage type's transcendentals.</param>
	/// <returns>One divergence per consecutive pair: one fewer than the history holds, or none.</returns>
	/// <exception cref="ArgumentNullException">An argument is null.</exception>
	/// <exception cref="ArgumentException">
	/// The history mixes objects, or is not in strictly increasing epoch order.
	/// </exception>
	/// <remarks>
	/// Consecutive pairs rather than every pair against the first, because that is the comparison an
	/// operator makes — "how wrong was the last set by the time the next one arrived" — and because
	/// its horizon is the update interval rather than something that grows with the archive.
	/// </remarks>
	public static IReadOnlyList<ElementSetDivergence<T>> Consecutive(IReadOnlyList<ElementSet> history, IStorageMath<T> math)
	{
		Ensure.NotNull(history);
		Ensure.NotNull(math);

		List<ElementSetDivergence<T>> divergences = [];

		for (int i = 1; i < history.Count; i++)
		{
			divergences.Add(Between(history[i - 1], history[i], math));
		}

		return divergences;
	}

	/// <summary>The time from one epoch to another, in minutes.</summary>
	/// <remarks>
	/// Taken from the two-part Julian dates part by part, so neither epoch is ever collapsed into one
	/// <see cref="double"/> near 2.46 million, where an ulp is 48 µs (domain trap 4). The whole-day
	/// difference is an exact integer, and the fractions are each exact in <typeparamref name="T"/>,
	/// so the only rounding is in <typeparamref name="T"/>'s own arithmetic.
	/// </remarks>
	private static T MinutesBetween(ElementSet earlier, ElementSet later)
	{
		T days = T.CreateChecked(later.EpochJulianDate.Day - earlier.EpochJulianDate.Day);
		T fraction = T.CreateChecked(later.EpochJulianDate.DayFraction) - T.CreateChecked(earlier.EpochJulianDate.DayFraction);

		return (days + fraction) * T.CreateChecked(MinutesPerDay);
	}
}
