// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Panels;

using System;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Residuals;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;

/// <summary>
/// Plots the arithmetic residual of each storage type: RIC components against time, and the
/// magnitude against the propagation interval on log axes with all four types overplotted.
/// </summary>
/// <remarks>
/// <para>
/// Every curve here is Δ_arith and nothing else. Each storage type propagates the same element set,
/// and its state is compared against a forty-digit <see cref="PreciseNumber"/> run, so the model and
/// the data are held fixed and what is left is round-off. Forty rather than the comparison harness's
/// thirty so that the thirty-digit run can be plotted too: gate 5 measured the two apart by around
/// 5e-21 km, which is what keeps its curve off the floor of a log axis and shows where "converged"
/// sits beside <see langword="double"/>.
/// </para>
/// <para>
/// The difference is taken in <see cref="PreciseNumber"/>, never in <see langword="double"/>.
/// Converting both states to <see langword="double"/> first would round each to about 1e-12 km at
/// orbital radius, which is the same size as the error being plotted for <see langword="double"/> and
/// would erase the thirty-digit curve entirely. Components stay signed through the RSW rotation and
/// become a magnitude only for the log plot.
/// </para>
/// <para>
/// The precise runs cost milliseconds each, so the sweep runs off the UI thread and the panel draws
/// whichever result is newest.
/// </para>
/// </remarks>
internal static class ResidualPlotsPanel
{
	/// <summary>The precision of the run every storage type is measured against.</summary>
	private const int ReferenceDigits = 40;

	/// <summary>The precision the comparison harness uses, plotted as the fourth storage type.</summary>
	private const int PreciseProbeDigits = 30;

	/// <summary>Samples on the linear time axis, including the epoch.</summary>
	private const int LinearSamples = 121;

	/// <summary>Samples on the logarithmic interval axis.</summary>
	private const int LogSamples = 49;

	/// <summary>The first interval on the log axis, in minutes. Zero has no place on a log axis.</summary>
	private const double FirstLogMinutes = 1.0;

	/// <summary>The default element set: the ISS as CelesTrak served it, the same one the parser tests use.</summary>
	private const string DefaultLine1 = "1 25544U 98067A   26258.88499338  .00006292  00000+0  12172-3 0  9999";

	/// <summary>The second line of the default element set.</summary>
	private const string DefaultLine2 = "2 25544  51.6310 211.2092 0004923 144.3133 215.8185 15.49128922585812";

	private static readonly string[] StorageNames =
	[
		"float",
		"double",
		"decimal",
		$"PreciseNumber ({PreciseProbeDigits} digits)",
	];

	private static string line1 = DefaultLine1;
	private static string line2 = DefaultLine2;
	private static float horizonMinutes = 1440.0f;
	private static int ricStorage = 1;
	private static string? inputError;

	private static ResidualSweep? latest;
	private static Task<ResidualSweep>? running;
	private static CancellationTokenSource? cancellation;

	/// <summary>
	/// Draws the panel for one frame.
	/// </summary>
	internal static void Draw()
	{
		CollectFinishedSweep();

		ImGui.InputText("Line 1", ref line1, 80);
		ImGui.InputText("Line 2", ref line2, 80);
		ImGui.SliderFloat("Horizon (min)", ref horizonMinutes, 10.0f, 20160.0f, "%.0f", ImGuiSliderFlags.Logarithmic);

		bool busy = running is not null;
		ImGui.BeginDisabled(busy);
		if (ImGui.Button("Compute"))
		{
			Start();
		}

		ImGui.EndDisabled();
		ImGui.SameLine();
		ImGui.TextUnformatted(Status(busy));

		if (inputError is not null)
		{
			ImGui.TextWrapped(inputError);
		}

		ResidualSweep? sweep = latest;
		if (sweep is null)
		{
			return;
		}

		ImGui.Separator();
		ImGui.Combo("RIC storage", ref ricStorage, StorageNames, StorageNames.Length);
		DrawRicPlot(sweep, ricStorage);
		DrawMagnitudePlot(sweep);
	}

	private static string Status(bool busy)
	{
		if (busy)
		{
			return "Computing...";
		}

		return latest is null
			? "Not computed yet."
			: string.Create(CultureInfo.InvariantCulture,
				$"{latest.ObjectLabel}, against PreciseNumber at {ReferenceDigits} digits, in {latest.Elapsed.TotalSeconds:F1} s");
	}

	private static void Start()
	{
		ElementSet elements;
		try
		{
			elements = TleParser.Parse(line1.Trim(), line2.Trim());
		}
		catch (FormatException e)
		{
			inputError = e.Message;
			return;
		}

		inputError = null;
		cancellation?.Dispose();
		cancellation = new CancellationTokenSource();
		double horizon = horizonMinutes;
		CancellationToken token = cancellation.Token;
		running = Task.Run(() => ResidualSweep.Run(elements, horizon, token), token);
	}

	private static void CollectFinishedSweep()
	{
		Task<ResidualSweep>? task = running;
		if (task is null || !task.IsCompleted)
		{
			return;
		}

		running = null;
		if (task.IsCompletedSuccessfully)
		{
			latest = task.Result;
		}
		else if (task.Exception is not null)
		{
			inputError = task.Exception.GetBaseException().Message;
		}
	}

	private static void DrawRicPlot(ResidualSweep sweep, int storage)
	{
		StorageCurves curves = sweep.Curves[storage];
		Vector2 size = new(-1.0f, ImGui.GetContentRegionAvail().Y * 0.5f);

		if (ImPlot.BeginPlot($"RIC residual, {StorageNames[storage]}", size))
		{
			ImPlot.SetupAxes("Minutes since epoch", "km", ImPlotAxisFlags.AutoFit, ImPlotAxisFlags.AutoFit);
			Line("Radial", sweep.LinearMinutes, curves.Radial);
			Line("Along-track", sweep.LinearMinutes, curves.AlongTrack);
			Line("Cross-track", sweep.LinearMinutes, curves.CrossTrack);
			ImPlot.EndPlot();
		}
	}

	private static void DrawMagnitudePlot(ResidualSweep sweep)
	{
		if (ImPlot.BeginPlot("|residual| against interval", new Vector2(-1.0f, -1.0f)))
		{
			ImPlot.SetupAxes("Minutes since epoch", "km", ImPlotAxisFlags.AutoFit, ImPlotAxisFlags.AutoFit);
			ImPlot.SetupAxisScale(ImAxis.X1, ImPlotScale.Log10);
			ImPlot.SetupAxisScale(ImAxis.Y1, ImPlotScale.Log10);

			for (int i = 0; i < StorageNames.Length; i++)
			{
				Line(StorageNames[i], sweep.LogMinutes, sweep.Curves[i].Magnitude);
			}

			ImPlot.EndPlot();
		}
	}

	private static void Line(string label, double[] xs, double[] ys) =>
		ImPlot.PlotLine(label, ref xs[0], ref ys[0], xs.Length);

	/// <summary>
	/// The residual of one storage type against the reference. Components are on the linear time
	/// grid and the magnitude on the logarithmic one; a sample the propagation refused is NaN, which
	/// the plot draws as a gap rather than as a value.
	/// </summary>
	private sealed record StorageCurves(double[] Radial, double[] AlongTrack, double[] CrossTrack, double[] Magnitude);

	/// <summary>
	/// One completed sweep: the residual of every storage type at every sample.
	/// </summary>
	private sealed record ResidualSweep(
		string ObjectLabel,
		double[] LinearMinutes,
		double[] LogMinutes,
		StorageCurves[] Curves,
		TimeSpan Elapsed)
	{
		/// <summary>
		/// Propagates the element set in every storage type and resolves each against the reference.
		/// </summary>
		/// <param name="elements">The element set.</param>
		/// <param name="horizonMinutes">The last sample, in minutes since epoch.</param>
		/// <param name="token">Cancels the sweep.</param>
		/// <returns>The sweep.</returns>
		internal static ResidualSweep Run(ElementSet elements, double horizonMinutes, CancellationToken token)
		{
			long started = System.Diagnostics.Stopwatch.GetTimestamp();
			double[] linear = new double[LinearSamples];
			for (int i = 0; i < LinearSamples; i++)
			{
				linear[i] = horizonMinutes * i / (LinearSamples - 1);
			}

			double[] logarithmic = new double[LogSamples];
			double ratio = Math.Log(horizonMinutes / FirstLogMinutes);
			for (int i = 0; i < LogSamples; i++)
			{
				logarithmic[i] = FirstLogMinutes * Math.Exp(ratio * i / (LogSamples - 1));
			}

			double[] minutes = [.. linear, .. logarithmic];
			PreciseStorageMath referenceMath = new(ReferenceDigits);

			// Five independent propagations, each with its own satellite and its own math instance.
			// Nothing is shared but the element set, which is immutable.
			TemeState<PreciseNumber>?[] reference = [];
			TemeState<PreciseNumber>?[][] probes = new TemeState<PreciseNumber>?[StorageNames.Length][];
			Parallel.Invoke(
				new ParallelOptions { CancellationToken = token },
				() => reference = Propagate(elements, minutes, referenceMath, token),
				() => probes[0] = Propagate(elements, minutes, FloatStorageMath.Instance, token),
				() => probes[1] = Propagate(elements, minutes, DoubleStorageMath.Instance, token),
				() => probes[2] = Propagate(elements, minutes, DecimalStorageMath.Instance, token),
				() => probes[3] = Propagate(elements, minutes, new PreciseStorageMath(PreciseProbeDigits), token));

			StorageCurves[] curves = new StorageCurves[StorageNames.Length];
			for (int s = 0; s < curves.Length; s++)
			{
				curves[s] = Resolve(reference, probes[s], referenceMath);
			}

			return new ResidualSweep(
				string.IsNullOrEmpty(elements.ObjectName)
					? elements.NoradCatalogId.ToString(CultureInfo.InvariantCulture)
					: elements.ObjectName,
				linear,
				logarithmic,
				curves,
				System.Diagnostics.Stopwatch.GetElapsedTime(started));
		}

		/// <summary>
		/// Propagates in one storage type and widens each state into <see cref="PreciseNumber"/>.
		/// </summary>
		/// <remarks>
		/// Widening a <see langword="float"/> or <see langword="double"/> goes through its shortest
		/// round-tripping text rather than its exact binary value. The two differ by under half an
		/// ulp of the type itself, which is below the error being plotted for that type.
		/// </remarks>
		private static TemeState<PreciseNumber>?[] Propagate<T>(
			ElementSet elements, double[] minutes, IStorageMath<T> math, CancellationToken token)
			where T : struct, INumber<T>
		{
			Sgp4Satellite<T> satellite = Sgp4<T>.Initialize(elements, math);
			TemeState<PreciseNumber>?[] states = new TemeState<PreciseNumber>?[minutes.Length];

			for (int i = 0; i < minutes.Length; i++)
			{
				token.ThrowIfCancellationRequested();
				Sgp4Result<T> result = Sgp4<T>.Propagate(satellite, T.CreateChecked(minutes[i]), math);
				if (result.IsSuccess)
				{
					TemeState<T> s = result.State;
					states[i] = new TemeState<PreciseNumber>(
						Widen(s.X), Widen(s.Y), Widen(s.Z), Widen(s.VelocityX), Widen(s.VelocityY), Widen(s.VelocityZ));
				}
			}

			return states;
		}

		private static PreciseNumber Widen<T>(T value)
			where T : struct, INumber<T> => value.ToPreciseNumber();

		private static StorageCurves Resolve(
			TemeState<PreciseNumber>?[] reference, TemeState<PreciseNumber>?[] probe, PreciseStorageMath math)
		{
			double[] radial = new double[LinearSamples];
			double[] alongTrack = new double[LinearSamples];
			double[] crossTrack = new double[LinearSamples];
			double[] magnitude = new double[LogSamples];

			for (int i = 0; i < reference.Length; i++)
			{
				double r = double.NaN;
				double a = double.NaN;
				double c = double.NaN;

				if (reference[i] is TemeState<PreciseNumber> truth && probe[i] is TemeState<PreciseNumber> test)
				{
					RswResidual<PreciseNumber> residual = RswResidual<PreciseNumber>.Between(truth, test, math);
					r = residual.Radial.To<double>();
					a = residual.AlongTrack.To<double>();
					c = residual.CrossTrack.To<double>();
				}

				if (i < LinearSamples)
				{
					radial[i] = r;
					alongTrack[i] = a;
					crossTrack[i] = c;
				}
				else
				{
					// Each component is already a small difference, so squaring in double loses
					// nothing that matters. An exact zero has no place on a log axis and is dropped.
					double m = Math.Sqrt((r * r) + (a * a) + (c * c));
					magnitude[i - LinearSamples] = m > 0.0 ? m : double.NaN;
				}
			}

			return new StorageCurves(radial, alongTrack, crossTrack, magnitude);
		}
	}
}
