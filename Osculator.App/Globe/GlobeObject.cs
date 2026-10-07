// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Globe;

using System;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// Which residual the globe colours its objects by.
/// </summary>
internal enum GlobeResidual
{
	/// <summary>|r(float) − r(double)|: what seven digits cost, which is up to 55 km and never reported.</summary>
	FloatVersusDouble,

	/// <summary>|r(decimal) − r(double)|: what twelve extra digits buy, which is almost nothing.</summary>
	DecimalVersusDouble,
}

/// <summary>
/// One object on the globe: its element set and the satellites initialized from it.
/// </summary>
/// <remarks>
/// <para>
/// The residual is Δ_arith, the one term the repository can measure for any object without an
/// independent observation. It is position in one storage type against position in
/// <see langword="double"/> at the same instant from the same element set, so the element set and
/// the model cancel and what is left is arithmetic. The headline decomposition, with Δ_model and
/// Δ_data beside it, is a later routine; when it lands, it becomes another member of
/// <see cref="GlobeResidual"/> rather than a different panel.
/// </para>
/// <para>
/// <c>PreciseNumber</c> is not offered: one initialization costs about 47 ms, which is a frame
/// budget three times over for a single object, and its arithmetic error is zero by construction
/// at the scale this colour bar can show.
/// </para>
/// </remarks>
internal sealed class GlobeObject
{
	private readonly Lazy<Sgp4Satellite<float>> asFloat;
	private readonly Lazy<Sgp4Satellite<decimal>> asDecimal;

	/// <summary>
	/// Initializes a new instance of the <see cref="GlobeObject"/> class.
	/// </summary>
	/// <param name="elements">The element set.</param>
	internal GlobeObject(ElementSet elements)
	{
		Elements = Ensure.NotNull(elements);
		AsDouble = Sgp4<double>.Initialize(elements, DoubleStorageMath.Instance);
		asFloat = new(() => Sgp4<float>.Initialize(elements, FloatStorageMath.Instance));
		asDecimal = new(() => Sgp4<decimal>.Initialize(elements, DecimalStorageMath.Instance));
	}

	/// <summary>Gets the element set.</summary>
	internal ElementSet Elements { get; }

	/// <summary>Gets the satellite in <see langword="double"/>, which places the object on the map.</summary>
	internal Sgp4Satellite<double> AsDouble { get; }

	/// <summary>Gets the name to show.</summary>
	internal string Name => string.IsNullOrWhiteSpace(Elements.ObjectName)
		? Elements.NoradCatalogId.ToString(System.Globalization.CultureInfo.InvariantCulture)
		: Elements.ObjectName;

	/// <summary>
	/// Locates the object.
	/// </summary>
	/// <param name="instantUtc">The instant, on the UTC scale.</param>
	/// <param name="point">Where it is.</param>
	/// <returns><see langword="false"/> when the model reports an error at that instant.</returns>
	internal bool TryLocate(DateTime instantUtc, out TrackPoint point) =>
		GroundTrack.TryLocate(Elements, AsDouble, instantUtc, out point);

	/// <summary>
	/// The arithmetic residual at an instant, in kilometres.
	/// </summary>
	/// <param name="residual">Which storage type to compare against <see langword="double"/>.</param>
	/// <param name="instantUtc">The instant, on the UTC scale.</param>
	/// <returns>
	/// The distance between the two positions, or <see langword="null"/> when either propagation
	/// failed. A failure in only the lower-precision type is not reported as a residual, because
	/// it has no position to measure from; <c>float</c>'s defining failure is that it almost
	/// never does fail, and returns a confident wrong answer instead.
	/// </returns>
	internal double? ResidualKilometers(GlobeResidual residual, DateTime instantUtc)
	{
		double minutes = (instantUtc - Elements.Epoch).TotalMinutes;
		Sgp4Result<double> reference = Sgp4<double>.Propagate(AsDouble, minutes, DoubleStorageMath.Instance);

		if (!reference.IsSuccess)
		{
			return null;
		}

		return residual switch
		{
			GlobeResidual.FloatVersusDouble => Distance(reference.State, Propagate(asFloat.Value, minutes, FloatStorageMath.Instance)),
			GlobeResidual.DecimalVersusDouble => Distance(reference.State, Propagate(asDecimal.Value, minutes, DecimalStorageMath.Instance)),
			_ => throw new ArgumentOutOfRangeException(nameof(residual), residual, "Unknown residual."),
		};
	}

	private static TemeState<double>? Propagate<T>(Sgp4Satellite<T> satellite, double minutes, IStorageMath<T> math)
		where T : struct, System.Numerics.INumber<T>
	{
		if (satellite.InitializationError != Sgp4Error.None)
		{
			return null;
		}

		Sgp4Result<T> result = Sgp4<T>.Propagate(satellite, T.CreateChecked(minutes), math);

		if (!result.IsSuccess)
		{
			return null;
		}

		TemeState<T> s = result.State;
		return new TemeState<double>(
			double.CreateChecked(s.X),
			double.CreateChecked(s.Y),
			double.CreateChecked(s.Z),
			double.CreateChecked(s.VelocityX),
			double.CreateChecked(s.VelocityY),
			double.CreateChecked(s.VelocityZ));
	}

	private static double? Distance(TemeState<double> reference, TemeState<double>? test)
	{
		if (test is not TemeState<double> other)
		{
			return null;
		}

		double dx = other.X - reference.X;
		double dy = other.Y - reference.Y;
		double dz = other.Z - reference.Z;
		return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
	}
}
