// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Panels;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Residuals;
using ktsu.Osculator.Data.Sp3;

/// <summary>
/// One TEME component of the LAGEOS-1 residual, sampled three ways.
/// </summary>
/// <param name="Name">The component's name: X, Y or Z.</param>
/// <param name="Double">The residual computed end to end in <see langword="double"/>, in metres.</param>
/// <param name="Stored">
/// The same two <see langword="double"/> states, each rounded to <see langword="float"/> before
/// they are differenced, in metres: what storing the states in <see langword="float"/> costs, with
/// no <see langword="float"/> arithmetic at all.
/// </param>
/// <param name="Float">The residual computed end to end in <see langword="float"/>, in metres.</param>
/// <param name="Step">
/// The <see langword="float"/> lattice the end-to-end residual sits on at each sample, in metres:
/// the spacing of <see langword="float"/> values at the smaller of the two states' magnitudes for
/// this component. Every <see cref="Float"/> value is a whole multiple of it.
/// </param>
internal sealed record QuantizedComponent(string Name, double[] Double, double[] Stored, double[] Float, double[] Step)
{
	/// <summary>Gets the number of distinct values the end-to-end <see langword="float"/> residual takes.</summary>
	internal int DistinctFloatLevels => new HashSet<double>(Float).Count;

	/// <summary>Gets the number of distinct values the stored-in-<see langword="float"/> residual takes.</summary>
	internal int DistinctStoredLevels => new HashSet<double>(Stored).Count;

	/// <summary>Gets the worst distance of the end-to-end <see langword="float"/> residual from the <see langword="double"/> one, in metres.</summary>
	internal double WorstFloatError => Worst(Float);

	/// <summary>Gets the worst distance of the stored-in-<see langword="float"/> residual from the <see langword="double"/> one, in metres.</summary>
	internal double WorstStoredError => Worst(Stored);

	/// <summary>Gets the largest lattice step across the samples, in metres.</summary>
	internal double LargestStep => Step.Max();

	private double Worst(double[] series)
	{
		double worst = 0.0;

		for (int i = 0; i < series.Length; i++)
		{
			worst = Math.Max(worst, Math.Abs(series[i] - Double[i]));
		}

		return worst;
	}
}

/// <summary>
/// The LAGEOS-1 residual in <see langword="float"/> and <see langword="double"/>, sampled across
/// the committed laser-ranging hour.
/// </summary>
/// <param name="Minutes">Each sample's instant, in minutes after the table's first epoch.</param>
/// <param name="Components">The three TEME components.</param>
/// <param name="MagnitudeStepMeters">
/// The spacing of <see langword="float"/> values at the orbit's mean radius, in metres: 2⁻¹⁰ km for
/// LAGEOS, which is what the largest component is held to.
/// </param>
internal sealed record FloatQuantizationResult(double[] Minutes, QuantizedComponent[] Components, double MagnitudeStepMeters);

/// <summary>
/// Spec demonstration 2: <see langword="float"/> cannot represent a residual against a
/// laser-ranging orbit, because its own lattice at the orbit's radius is coarser than the truth.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The truth is the committed ILRS LAGEOS-1 SP3 hour</strong>, embedded from the test data
/// so there is one copy of it. An ILRS combined orbit is good to two or three centimetres.
/// </para>
/// <para>
/// <strong>The element set is fitted, not published.</strong> No public LAGEOS-1 element set for
/// 2021-12-30 is in this repository, and CelesTrak serves only current ones. So <see cref="Line1"/>
/// and <see cref="Line2"/> were fitted to the same hour by least squares on SGP4's TEME positions,
/// the six mean elements free and B* zero, then written out in the two-line format: 8.6 m RMS before
/// the rounding, 17 m worst after it. What the demonstration measures is arithmetic, and that is
/// indifferent to how good the element set is: the lattice is set by the orbit's radius.
/// </para>
/// <para>
/// <strong>The Earth's orientation is ignored</strong>, as it was for the fit, so the two are
/// consistent. UT1 − UTC was about −0.1 s that day, which the fit absorbs as a turn of the node.
/// </para>
/// <para>
/// Three series per component, because they answer two different questions. <em>Stored</em> rounds
/// the <see langword="double"/> states to <see langword="float"/> and differences them: that is
/// representation alone, and it is a staircase one lattice step high. <em>Float</em> computes
/// everything in <see langword="float"/>: the propagator's own rounding lands on the same lattice
/// several steps either side, so the residual scatters across a few horizontal bands. Neither can
/// follow a residual that moves a few millimetres between samples, which is what
/// <em>Double</em> does.
/// </para>
/// </remarks>
internal static class FloatQuantization
{
	/// <summary>The fitted LAGEOS-1 element set's first line. See the class remarks.</summary>
	internal const string Line1 = "1 08820U 76039A   21364.97916667  .00000000  00000+0  00000+0 0  9998";

	/// <summary>The fitted LAGEOS-1 element set's second line.</summary>
	internal const string Line2 = "2 08820 109.8553 341.2788 0045189 290.8554 250.7026  6.38667063    01";

	/// <summary>The default spacing between samples, in seconds.</summary>
	internal const double DefaultSpacingSeconds = 15.0;

	/// <summary>
	/// The closest spacing the panel offers, in seconds: 361 samples over the hour. ImPlot spends a
	/// dozen vertices on each anti-aliased segment, and at 721 samples the three plots' lines and
	/// stairs outgrow the 65,536 vertices one ImGui draw list can index with 16-bit indices, past
	/// which ImGui aborts the process. Measured: 721 samples aborts and 361 does not.
	/// </summary>
	internal const double MinimumSpacingSeconds = 10.0;

	private const string ResourceName = "ktsu.Osculator.App.Data.lageos1-excerpt.sp3";

	private const double MetersPerKilometer = 1000.0;

	private const double SecondsPerMinute = 60.0;

	private static readonly Lazy<Sp3File> Excerpt = new(LoadExcerpt, LazyThreadSafetyMode.ExecutionAndPublication);

	/// <summary>Gets the fitted LAGEOS-1 element set.</summary>
	internal static ElementSet Elements => TleParser.Parse(Line1, Line2, "LAGEOS 1");

	/// <summary>Gets the committed laser-ranging hour.</summary>
	internal static Sp3File Truth => Excerpt.Value;

	/// <summary>
	/// Samples the residual across the table in both storage types.
	/// </summary>
	/// <param name="spacingSeconds">The spacing between samples, in seconds; at least one. The panel offers no closer than <see cref="MinimumSpacingSeconds"/>.</param>
	/// <param name="cancellation">Cancels between the two runs.</param>
	/// <returns>The three series per component.</returns>
	internal static FloatQuantizationResult Compute(double spacingSeconds, CancellationToken cancellation)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(spacingSeconds, 1.0);

		Sp3File truth = Truth;
		ElementSet elements = Elements;
		IReadOnlyList<Sp3Record> records = truth.RecordsOf(truth.Satellites[0]);
		double last = records[^1].SecondsSinceStart;
		List<double> seconds = [];

		for (double s = records[0].SecondsSinceStart; s <= last; s += spacingSeconds)
		{
			seconds.Add(s);
		}

		IReadOnlyList<TabulatedTruthSample<double>> inDouble = Run(elements, truth, seconds, DoubleStorageMath.Instance);
		cancellation.ThrowIfCancellationRequested();
		IReadOnlyList<TabulatedTruthSample<float>> inFloat = Run(elements, truth, seconds, FloatStorageMath.Instance);

		double[] minutes = new double[seconds.Count];
		QuantizedComponent[] components =
		[
			new("X", new double[seconds.Count], new double[seconds.Count], new double[seconds.Count], new double[seconds.Count]),
			new("Y", new double[seconds.Count], new double[seconds.Count], new double[seconds.Count], new double[seconds.Count]),
			new("Z", new double[seconds.Count], new double[seconds.Count], new double[seconds.Count], new double[seconds.Count]),
		];
		double radius = 0.0;

		for (int i = 0; i < seconds.Count; i++)
		{
			minutes[i] = seconds[i] / SecondsPerMinute;
			TemeState<double> predicted = inDouble[i].Predicted;
			TemeState<double> reference = inDouble[i].Truth;
			Fill(components[0], i, predicted.X, reference.X, inFloat[i].Predicted.X, inFloat[i].Truth.X);
			Fill(components[1], i, predicted.Y, reference.Y, inFloat[i].Predicted.Y, inFloat[i].Truth.Y);
			Fill(components[2], i, predicted.Z, reference.Z, inFloat[i].Predicted.Z, inFloat[i].Truth.Z);
			radius += Math.Sqrt((reference.X * reference.X) + (reference.Y * reference.Y) + (reference.Z * reference.Z));
		}

		return new FloatQuantizationResult(minutes, components, Step((float)(radius / seconds.Count)) * MetersPerKilometer);
	}

	/// <summary>
	/// The spacing of <see langword="float"/> values at a magnitude: the distance from it to the next
	/// value away from zero.
	/// </summary>
	/// <param name="value">The value.</param>
	/// <returns>The spacing, in the value's own units.</returns>
	internal static double Step(float value)
	{
		float magnitude = MathF.Abs(value);
		return (double)MathF.BitIncrement(magnitude) - magnitude;
	}

	/// <summary>
	/// The lattice two nearby <see langword="float"/> values' difference sits on: the spacing at the
	/// smaller magnitude, since both are whole multiples of it.
	/// </summary>
	/// <param name="a">One value.</param>
	/// <param name="b">The other.</param>
	/// <returns>The spacing.</returns>
	internal static double Step(float a, float b) => Step(MathF.Min(MathF.Abs(a), MathF.Abs(b)));

	/// <summary>Builds the position and velocity tables of the file's one satellite in a storage type.</summary>
	/// <typeparam name="T">The storage type.</typeparam>
	/// <param name="file">The file.</param>
	/// <returns>The two tables, on the file's epochs.</returns>
	internal static (IReadOnlyList<TabulatedPosition<T>> Positions, IReadOnlyList<TabulatedPosition<T>> Velocities) Tables<T>(Sp3File file)
		where T : struct, INumber<T>
	{
		Ensure.NotNull(file);

		string satellite = file.Satellites[0];
		IReadOnlyList<Sp3Record> records = file.RecordsOf(satellite);
		TabulatedPosition<T>[] velocities = new TabulatedPosition<T>[records.Count];

		for (int i = 0; i < records.Count; i++)
		{
			Sp3Velocity velocity = records[i].Velocity
				?? throw new InvalidOperationException($"Epoch {i} of {satellite} has no velocity record.");

			velocities[i] = new TabulatedPosition<T>(
				T.CreateChecked(records[i].SecondsSinceStart),
				T.CreateChecked(velocity.X),
				T.CreateChecked(velocity.Y),
				T.CreateChecked(velocity.Z));
		}

		return (file.Tabulate<T>(satellite), velocities);
	}

	/// <summary>Runs the comparison in one storage type, ignoring the Earth's orientation as the fit did.</summary>
	/// <typeparam name="T">The storage type.</typeparam>
	/// <param name="elements">The element set.</param>
	/// <param name="truth">The table.</param>
	/// <param name="seconds">The sample instants.</param>
	/// <param name="math">The storage type's functions.</param>
	/// <returns>The samples.</returns>
	internal static IReadOnlyList<TabulatedTruthSample<T>> Run<T>(ElementSet elements, Sp3File truth, IReadOnlyList<double> seconds, IStorageMath<T> math)
		where T : struct, INumber<T>
	{
		Ensure.NotNull(truth);

		(IReadOnlyList<TabulatedPosition<T>> positions, IReadOnlyList<TabulatedPosition<T>> velocities) = Tables<T>(truth);
		return TabulatedTruthResidual<T>.Compute(elements, positions, velocities, truth.Start, seconds, _ => EarthOrientation.Ignored, math);
	}

	private static void Fill(QuantizedComponent component, int i, double predicted, double truth, float predictedInFloat, float truthInFloat)
	{
		component.Double[i] = (predicted - truth) * MetersPerKilometer;
		component.Stored[i] = ((double)(float)predicted - (float)truth) * MetersPerKilometer;
		component.Float[i] = ((double)predictedInFloat - truthInFloat) * MetersPerKilometer;
		component.Step[i] = Step(predictedInFloat, truthInFloat) * MetersPerKilometer;
	}

	private static Sp3File LoadExcerpt()
	{
		using Stream stream = typeof(FloatQuantization).Assembly.GetManifestResourceStream(ResourceName)
			?? throw new InvalidOperationException($"The {ResourceName} resource is missing.");
		using StreamReader reader = new(stream);
		return Sp3Parser.Parse(reader.ReadToEnd());
	}
}
