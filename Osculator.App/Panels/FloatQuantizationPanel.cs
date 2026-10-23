// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Panels;

using System;
using System.Globalization;
using System.Numerics;
using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;
using ktsu.Osculator.App.Shell;

/// <summary>
/// Spec demonstration 2: the LAGEOS-1 residual against its laser-ranging orbit, in
/// <see langword="float"/> and <see langword="double"/>, so the banding is visible.
/// </summary>
/// <remarks>
/// One plot per TEME component, because that is where the <see langword="float"/> lattice lives:
/// each component of each state is held to the spacing of <see langword="float"/> at its own
/// magnitude, and resolving the difference into radial, along-track and cross-track would mix three
/// lattices into one that is no longer evenly spaced. See <see cref="FloatQuantization"/> for what
/// the three series are and where the element set came from.
/// </remarks>
internal sealed class FloatQuantizationPanel : Panel
{
	/// <summary>The panel's title.</summary>
	internal const string PanelTitle = "Float quantization";

	/// <summary>The background job key for a computation.</summary>
	internal const string ComputeKey = "float-quantization";

	private float spacingSeconds = (float)FloatQuantization.DefaultSpacingSeconds;
	private bool requested;
	private string? status;

	/// <summary>Gets the latest finished computation.</summary>
	internal FloatQuantizationResult? Result { get; private set; }

	/// <inheritdoc/>
	protected override string Title => PanelTitle;

	/// <summary>Starts a computation at a spacing, replacing any already running.</summary>
	/// <param name="spacing">The spacing between samples, in seconds; clamped to what the panel offers.</param>
	internal void Recompute(double spacing)
	{
		spacingSeconds = (float)Math.Max(spacing, FloatQuantization.MinimumSpacingSeconds);
		Recompute();
	}

	/// <summary>Starts a computation at the current spacing, replacing any already running.</summary>
	internal void Recompute()
	{
		requested = true;
		status = "Computing…";
		double spacing = spacingSeconds;

		Work.Run(
			ComputeKey,
			token => FloatQuantization.Compute(spacing, token),
			result =>
			{
				Result = result;
				status = null;
			},
			error => status = error.GetBaseException().Message);
	}

	/// <inheritdoc/>
	protected override void Draw()
	{
		if (!requested)
		{
			Recompute();
		}

		ImGui.TextWrapped(
			"SGP4 for LAGEOS-1 minus its ILRS laser-ranging orbit, good to centimetres, over one hour of 2021-12-30. " +
			"In float a position 12,000 km out is held to the nearest 2^-10 km, so the residual can only move in whole steps of it.");

		ImGui.SetNextItemWidth(ImGui.GetFontSize() * 12.0f);
		ImGui.SliderFloat("Spacing (s)", ref spacingSeconds, (float)FloatQuantization.MinimumSpacingSeconds, 60.0f, "%.0f", ImGuiSliderFlags.AlwaysClamp);
		ImGui.SameLine();

		if (ImGui.Button("Recompute"))
		{
			Recompute();
		}

		if (status is not null)
		{
			ImGui.TextWrapped(status);
		}

		if (Result is not null)
		{
			DrawSummary(Result);
			DrawPlots(Result);
		}
	}

	/// <inheritdoc/>
	protected override void OnDismissed()
	{
		Work.Cancel(ComputeKey);
		requested = false;
	}

	private static void DrawSummary(FloatQuantizationResult result)
	{
		ImGui.TextUnformatted(string.Create(
			CultureInfo.InvariantCulture,
			$"{result.Minutes.Length} samples. Float step at the orbit's radius: {result.MagnitudeStepMeters:F4} m."));

		if (ImGui.BeginTable("float-quantization-summary", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
		{
			ImGui.TableSetupColumn("Component");
			ImGui.TableSetupColumn("Largest step (m)");
			ImGui.TableSetupColumn("Float levels");
			ImGui.TableSetupColumn("Stored levels");
			ImGui.TableSetupColumn("Worst float − double (m)");
			ImGui.TableSetupColumn("Worst stored − double (m)");
			ImGui.TableHeadersRow();

			foreach (QuantizedComponent component in result.Components)
			{
				ImGui.TableNextRow();
				Cell(component.Name);
				Cell(component.LargestStep.ToString("F4", CultureInfo.InvariantCulture));
				Cell(component.DistinctFloatLevels.ToString(CultureInfo.InvariantCulture));
				Cell(component.DistinctStoredLevels.ToString(CultureInfo.InvariantCulture));
				Cell(component.WorstFloatError.ToString("F3", CultureInfo.InvariantCulture));
				Cell(component.WorstStoredError.ToString("F3", CultureInfo.InvariantCulture));
			}

			ImGui.EndTable();
		}
	}

	private static void DrawPlots(FloatQuantizationResult result)
	{
		float height = ImGui.GetContentRegionAvail().Y / result.Components.Length;
		double[] minutes = result.Minutes;

		foreach (QuantizedComponent component in result.Components)
		{
			if (ImPlot.BeginPlot($"TEME {component.Name} residual", new Vector2(-1.0f, height)))
			{
				ImPlot.SetupAxes("Minutes after 2021-12-30 23:00 UTC", "m", ImPlotAxisFlags.AutoFit, ImPlotAxisFlags.AutoFit);
				ImPlot.SetNextMarkerStyle(ImPlotMarker.Square, 1.5f);
				ImPlot.PlotScatter("float, end to end", ref minutes[0], ref component.Float[0], minutes.Length);
				ImPlot.PlotStairs("double states stored as float", ref minutes[0], ref component.Stored[0], minutes.Length, ImPlotStairsFlags.None);
				ImPlot.PlotLine("double", ref minutes[0], ref component.Double[0], minutes.Length);
				ImPlot.EndPlot();
			}
		}
	}

	private static void Cell(string text)
	{
		ImGui.TableNextColumn();
		ImGui.TextUnformatted(text);
	}
}
