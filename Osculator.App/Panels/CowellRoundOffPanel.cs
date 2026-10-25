// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Panels;

using System;
using System.Globalization;
using System.Numerics;
using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;
using ktsu.Osculator.App.Shell;

/// <summary>
/// Demonstration 4: round-off accumulated over a thirty-day numerically integrated arc, in every
/// storage type, against a thirty-digit run of the same integration.
/// </summary>
/// <remarks>
/// <para>
/// An analytic propagator such as SGP4 evaluates each instant from scratch, so its round-off does not
/// depend on how far ahead it looks. An integrator reaches day thirty through every step before it,
/// and each one rounds. The spec projected that the error would random-walk and grow as √t; measured,
/// it grows as roughly the square of the arc, because a rounding error in the state is an error in
/// the orbit's energy and so in its period. The panel shows both: each type's measured exponent, and
/// the spec's √t drawn through double's first day.
/// </para>
/// <para>
/// The measurement is <see cref="CowellRoundOffArc"/>, the same integration
/// <c>CowellRoundOffTests</c> runs. It takes several seconds, nearly all of it in the two
/// PreciseNumber runs, so it starts from a button and runs through <see cref="Panel.Work"/>.
/// </para>
/// </remarks>
internal sealed class CowellRoundOffPanel : Panel
{
	/// <summary>The panel's title.</summary>
	internal const string PanelTitle = "Cowell round-off";

	/// <summary>The background job key for the measurement.</summary>
	internal const string MeasureKey = "cowell-round-off";

	private readonly CowellRoundOffProgress progress = new();

	private string? status;

	/// <summary>Gets the latest finished measurement.</summary>
	internal CowellRoundOffResult? Result { get; private set; }

	/// <summary>Gets a value indicating whether a measurement is running.</summary>
	internal bool IsMeasuring => Work.IsRunning(MeasureKey);

	/// <inheritdoc/>
	protected override string Title => PanelTitle;

	/// <summary>
	/// Starts the measurement, replacing one already running.
	/// </summary>
	internal void Measure()
	{
		status = null;
		Work.Run(
			MeasureKey,
			token => CowellRoundOffArc.Measure(progress, token),
			measured => Result = measured,
			error => status = error.GetBaseException().Message);
	}

	/// <inheritdoc/>
	protected override void Draw()
	{
		ImGui.TextWrapped(string.Create(
			CultureInfo.InvariantCulture,
			$"A two-body orbit at 7000 km integrated for thirty days by Cowell's method, in fixed {CowellRoundOffArc.StepSeconds} s steps, in every storage type. Each line is that run's distance from a {CowellRoundOffArc.ReferenceDigits}-digit run of the same integration: the model, the inputs and the steps are identical, so only the arithmetic differs."));

		DrawControls();

		if (status is not null)
		{
			ImGui.TextWrapped(status);
		}

		if (Result is not null)
		{
			ImGui.Separator();
			DrawTable(Result);
			DrawPlot(Result);
		}
	}

	/// <inheritdoc/>
	protected override void OnDismissed() => Work.Cancel(MeasureKey);

	private static string Kilometres(double km) =>
		km.ToString("0.000e+00", CultureInfo.InvariantCulture);

	private static void DrawTable(CowellRoundOffResult result)
	{
		const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersV | ImGuiTableFlags.SizingFixedFit;

		if (!ImGui.BeginTable("cowell-round-off", 6, flags))
		{
			return;
		}

		ImGui.TableSetupColumn("Storage type");
		ImGui.TableSetupColumn("Day 1 (km)");
		ImGui.TableSetupColumn("Day 30 (km)");
		ImGui.TableSetupColumn("Measured growth");
		ImGui.TableSetupColumn("Spec projected");
		ImGui.TableSetupColumn("Run time");
		ImGui.TableHeadersRow();

		CultureInfo culture = CultureInfo.InvariantCulture;

		foreach (StorageArc run in result.Runs)
		{
			ImGui.TableNextRow();
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(run.StorageName);
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(Kilometres(run.Distances[CowellRoundOffArc.DayOneIndex]));
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(Kilometres(run.Distances[^1]));
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(double.IsFinite(run.Exponent) ? string.Create(culture, $"t^{run.Exponent:F2}") : "—");
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(string.Create(culture, $"t^{CowellRoundOffArc.SpecExponent:F2} (√t)"));
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(MeasuredDurations.Show(string.Create(culture, $"{run.Seconds:F2} s")));
		}

		ImGui.EndTable();

		if (!MeasuredDurations.Hidden)
		{
			ImGui.TextDisabled(string.Create(
				culture,
				$"The {CowellRoundOffArc.ReferenceDigits}-digit reference took {result.ReferenceSeconds:F2} s."));
		}
	}

	private static void DrawPlot(CowellRoundOffResult result)
	{
		if (!ImPlot.BeginPlot("Distance from the reference", new Vector2(-1.0f, -1.0f)))
		{
			return;
		}

		ImPlot.SetupAxes("Days integrated", "km", ImPlotAxisFlags.AutoFit, ImPlotAxisFlags.AutoFit);
		ImPlot.SetupAxisScale(ImAxis.X1, ImPlotScale.Log10);
		ImPlot.SetupAxisScale(ImAxis.Y1, ImPlotScale.Log10);

		double[] days = CowellRoundOffArc.CheckpointDays;

		foreach (StorageArc run in result.Runs)
		{
			Line(run.StorageName, days, run.Distances);
		}

		Line("spec: √t through double's day 1", days, SquareRootFrom(days, result.Runs[1].Distances[CowellRoundOffArc.DayOneIndex]));

		ImPlot.EndPlot();
	}

	/// <summary>The spec's projection: a random walk anchored at one measured day-1 distance.</summary>
	private static double[] SquareRootFrom(double[] days, double dayOne)
	{
		double[] projected = new double[days.Length];
		for (int i = 0; i < days.Length; i++)
		{
			projected[i] = dayOne * Math.Pow(days[i], CowellRoundOffArc.SpecExponent);
		}

		return projected;
	}

	private static void Line(string label, double[] xs, double[] ys) =>
		ImPlot.PlotLine(label, ref xs[0], ref ys[0], xs.Length);

	private void DrawControls()
	{
		if (IsMeasuring)
		{
			long total = progress.Total;
			long done = progress.Done;
			ImGui.ProgressBar(
				total == 0 ? 0.0f : (float)done / total,
				new Vector2(-1.0f, 0.0f),
				string.Create(CultureInfo.InvariantCulture, $"{done:N0} of {total:N0} steps integrated"));

			if (ImGui.Button("Cancel"))
			{
				Work.Cancel(MeasureKey);
				status = "Measurement cancelled.";
			}

			return;
		}

		if (ImGui.Button(Result is null ? "Integrate thirty days" : "Integrate again"))
		{
			Measure();
		}
	}
}
