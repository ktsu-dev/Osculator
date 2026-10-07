// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Panels;

using System;
using System.Collections.Generic;
using System.Globalization;
using Hexa.NET.ImGui;
using ktsu.ImGui.Widgets;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Residuals;

/// <summary>
/// Shows every field of one element set beside the step it is written to and what that step is
/// worth in metres at a chosen horizon.
/// </summary>
/// <remarks>
/// <para>
/// This is <see cref="DataTerm"/> laid out one field per row. Each field's figure is the position
/// shift from perturbing that field alone by half its quantization step, doubled to read per whole
/// step: the linearised ∂r/∂field in metres per step. The combined figure at the top is the data term
/// itself, in quadrature over half steps, exactly as <see cref="DataTerm.CombineInQuadrature"/>
/// reports it.
/// </para>
/// <para>
/// The orbital fields are editable, and the sensitivities follow the edit, so a reader can see that
/// mean motion never leads however it is nudged. Two fields are deliberately not measured: the epoch,
/// which <see cref="DataTerm"/> does not perturb yet, and the second derivative of mean motion,
/// which SGP4 never reads and so contributes exactly nothing.
/// </para>
/// <para>
/// Until the catalogue panel supplies a selection through <see cref="Select"/>, the panel shows an
/// ISS element set that is also committed in the parser tests.
/// </para>
/// </remarks>
internal static class ElementInspectorPanel
{
	/// <summary>The window title, and the id the shell docks the panel under.</summary>
	internal const string Title = "Element inspector";

	private const string DefaultLine1 = "1 25544U 98067A   26258.88499338  .00006292  00000+0  12172-3 0  9999";
	private const string DefaultLine2 = "2 25544  51.6310 211.2092 0004923 144.3133 215.8185 15.49128922585812";
	private const double MinutesPerDay = 1440.0;
	private const double MetersPerKilometer = 1000.0;

	private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

	private static readonly ImGuiWidgets.PropertyGridOptions GridOptions = new()
	{
		DoubleFormat = "%.12g",
		LabelColumnWeight = 0.35f,
	};

	private static ElementSet asWritten = TleParser.Parse(DefaultLine1, DefaultLine2, "ISS (ZARYA)");
	private static ElementSet current = asWritten;
	private static float horizonDays = 1.0f;
	private static Measurement? measurement;

	/// <summary>
	/// Shows an element set, typically the one selected in the catalogue.
	/// </summary>
	/// <param name="elements">The element set as written.</param>
	internal static void Select(ElementSet elements)
	{
		Ensure.NotNull(elements);

		asWritten = elements;
		current = elements;
		measurement = null;
	}

	/// <summary>
	/// Restores the panel's start-up state.
	/// </summary>
	internal static void ResetState()
	{
		Select(TleParser.Parse(DefaultLine1, DefaultLine2, "ISS (ZARYA)"));
		horizonDays = 1.0f;
	}

	/// <summary>
	/// Draws the panel's contents for one frame.
	/// </summary>
	internal static void Draw()
	{
		ImGui.TextUnformatted($"{current.ObjectName}  ({current.NoradCatalogId.ToString(Culture)})");

		if (ImGui.SliderFloat("Horizon (days)", ref horizonDays, 0.0f, 30.0f, "%.2f"))
		{
			measurement = null;
		}

		ImGui.SameLine();
		if (ImGui.Button("Reset to as written"))
		{
			current = asWritten;
			measurement = null;
		}

		Measurement shown = measurement ??= Measure(current, horizonDays);

		ImGui.TextUnformatted(shown.Error is { } error
			? $"Data term: {error}"
			: $"Data term at {Format(horizonDays, "F2")} days: {FormatMeters(shown.CombinedMeters)} (half-step, in quadrature)");
		ImGui.Separator();

		using ImGuiWidgets.PropertyGrid grid = new("elementInspector", GridOptions);

		DrawIdentity(grid);

		DrawEpoch(grid);

		bool changed = false;
		changed |= DrawField(grid, "MEAN_MOTION", "rev/day", current.MeanMotion, ElementFieldQuantization.MeanMotion, nameof(ElementSet.MeanMotion),
			static (e, v) => e with { MeanMotion = v });
		changed |= DrawField(grid, "ECCENTRICITY", string.Empty, current.Eccentricity, ElementFieldQuantization.Eccentricity, nameof(ElementSet.Eccentricity),
			static (e, v) => e with { Eccentricity = v });
		changed |= DrawField(grid, "INCLINATION", "deg", current.Inclination, ElementFieldQuantization.Inclination, nameof(ElementSet.Inclination),
			static (e, v) => e with { Inclination = v });
		changed |= DrawField(grid, "RA_OF_ASC_NODE", "deg", current.RightAscensionOfAscendingNode, ElementFieldQuantization.RightAscensionOfAscendingNode,
			nameof(ElementSet.RightAscensionOfAscendingNode), static (e, v) => e with { RightAscensionOfAscendingNode = v });
		changed |= DrawField(grid, "ARG_OF_PERICENTER", "deg", current.ArgumentOfPericenter, ElementFieldQuantization.ArgumentOfPericenter,
			nameof(ElementSet.ArgumentOfPericenter), static (e, v) => e with { ArgumentOfPericenter = v });
		changed |= DrawField(grid, "MEAN_ANOMALY", "deg", current.MeanAnomaly, ElementFieldQuantization.MeanAnomaly, nameof(ElementSet.MeanAnomaly),
			static (e, v) => e with { MeanAnomaly = v });
		changed |= DrawField(grid, "BSTAR", "1/Earth radii", current.BStar, ElementFieldQuantization.StepForExponentialField(current.BStar), "BStar",
			static (e, v) => e with { BStar = v });
		changed |= DrawField(grid, "MEAN_MOTION_DOT", "rev/day^2", current.MeanMotionDot, ElementFieldQuantization.MeanMotionDot, nameof(ElementSet.MeanMotionDot),
			static (e, v) => e with { MeanMotionDot = v });
		DrawMeanMotionDdot(grid);

		if (changed)
		{
			measurement = null;
		}
	}

	/// <summary>
	/// The data term for the element set on screen, keyed by <see cref="DataTerm.Contribution.Field"/>.
	/// </summary>
	/// <param name="PerField">Each measured field's half-step position shift, in kilometres.</param>
	/// <param name="CombinedMeters">The combined term, in metres.</param>
	/// <param name="Error">Why there is no measurement, or <see langword="null"/> when there is one.</param>
	private readonly record struct Measurement(IReadOnlyDictionary<string, double> PerField, double CombinedMeters, string? Error);

	private static Measurement Measure(ElementSet elements, float days)
	{
		try
		{
			IReadOnlyList<DataTerm.Contribution> contributions = DataTerm.Measure(elements, days * MinutesPerDay, DoubleStorageMath.Instance);
			Dictionary<string, double> perField = [];

			foreach (DataTerm.Contribution contribution in contributions)
			{
				perField[contribution.Field] = contribution.PositionKilometers;
			}

			return new Measurement(perField, DataTerm.CombineInQuadrature(contributions) * MetersPerKilometer, null);
		}
		catch (ArgumentException ex)
		{
			return new Measurement(new Dictionary<string, double>(), double.NaN, ex.Message);
		}
	}

	private static void DrawIdentity(ImGuiWidgets.PropertyGrid grid)
	{
		using ImGuiWidgets.PropertyGrid.SectionScope section = grid.Section("Identity (not quantized orbit fields)", defaultOpen: false);

		ReadOnlyRow(grid, "OBJECT_NAME", current.ObjectName);
		ReadOnlyRow(grid, "OBJECT_ID", current.ObjectId);
		ReadOnlyRow(grid, "NORAD_CAT_ID", current.NoradCatalogId.ToString(Culture));
		ReadOnlyRow(grid, "ELEMENT_SET_NO", current.ElementSetNumber.ToString(Culture));
		ReadOnlyRow(grid, "REV_AT_EPOCH", current.RevolutionAtEpoch.ToString(Culture));
	}

	private static void DrawEpoch(ImGuiWidgets.PropertyGrid grid)
	{
		using ImGuiWidgets.PropertyGrid.SectionScope section = grid.Section("EPOCH");

		ReadOnlyRow(grid, "Value##EPOCH", current.Epoch.ToString("yyyy-MM-ddTHH:mm:ss.ffffff", Culture));
		ReadOnlyRow(grid, "Step##EPOCH", $"{Format(ElementFieldQuantization.EpochDays, "G3")} day ({FormatSeconds(ElementFieldQuantization.EpochDays * 86400.0)})");
		ReadOnlyRow(grid, "dr per step##EPOCH", "not measured yet: the data term does not perturb the epoch");
	}

	private static void DrawMeanMotionDdot(ImGuiWidgets.PropertyGrid grid)
	{
		using ImGuiWidgets.PropertyGrid.SectionScope section = grid.Section("MEAN_MOTION_DDOT");

		ReadOnlyRow(grid, "Value##MEAN_MOTION_DDOT", $"{Format(current.MeanMotionDdot, "G6")} rev/day^3");
		ReadOnlyRow(grid, "Step##MEAN_MOTION_DDOT", Format(ElementFieldQuantization.StepForExponentialField(current.MeanMotionDdot), "G3"));
		ReadOnlyRow(grid, "dr per step##MEAN_MOTION_DDOT", "0 m: SGP4 never reads this field");
	}

	private static bool DrawField(
		ImGuiWidgets.PropertyGrid grid,
		string ommName,
		string unit,
		double value,
		double step,
		string dataTermField,
		Func<ElementSet, double, ElementSet> apply)
	{
		using ImGuiWidgets.PropertyGrid.SectionScope section = grid.Section(ommName);

		double edited = value;
		bool changed = grid.Value($"Value{(unit.Length > 0 ? $" ({unit})" : string.Empty)}##{ommName}", ref edited);

		if (changed)
		{
			current = apply(current, edited);
		}

		ReadOnlyRow(grid, $"Step##{ommName}", Format(step, "G3"));
		ReadOnlyRow(grid, $"dr per step##{ommName}", Sensitivity(dataTermField));

		return changed;
	}

	private static string Sensitivity(string dataTermField)
	{
		if (measurement is not { Error: null } m)
		{
			return "-";
		}

		// Half a step was measured; a whole step is twice that to first order.
		return m.PerField.TryGetValue(dataTermField, out double halfStepKilometers)
			? $"{FormatMeters(2.0 * halfStepKilometers * MetersPerKilometer)} per step"
			: "-";
	}

	private static void ReadOnlyRow(ImGuiWidgets.PropertyGrid grid, string label, string text)
	{
		string shown = text;
		ImGui.BeginDisabled();
		grid.Value(label, ref shown);
		ImGui.EndDisabled();
	}

	private static string Format(double value, string format) => value.ToString(format, Culture);

	private static string FormatSeconds(double seconds) => seconds switch
	{
		>= 1.0 => $"{Format(seconds, "G4")} s",
		>= 1e-3 => $"{Format(seconds * 1e3, "G4")} ms",
		_ => $"{Format(seconds * 1e6, "G4")} us",
	};

	private static string FormatMeters(double meters) => double.IsNaN(meters) ? "-" : Math.Abs(meters) switch
	{
		>= 1000.0 => $"{Format(meters / 1000.0, "G4")} km",
		>= 1.0 => $"{Format(meters, "G4")} m",
		>= 1e-3 => $"{Format(meters * 1e3, "G4")} mm",
		0.0 => "0 m",
		_ => $"{Format(meters, "E3")} m",
	};
}
