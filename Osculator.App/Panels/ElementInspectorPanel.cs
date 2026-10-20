// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Panels;

using System;
using System.Collections.Generic;
using System.Globalization;
using Hexa.NET.ImGui;
using ktsu.ImGui.Widgets;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.App.Shell;
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
/// mean motion never leads however it is nudged. Every step is the one the set's own format writes
/// the field to (<see cref="ElementFieldQuantization.For(ElementSet)"/>), which is the step
/// <see cref="DataTerm"/> perturbs by, so an OMM set shows the JSON's finer eccentricity and B*
/// beside the shift those finer steps produce. The epoch is measured like any other field. Only
/// the second derivative of mean motion is not, because SGP4 never reads it and so it contributes
/// exactly nothing.
/// </para>
/// <para>
/// The panel follows <see cref="CatalogueSelection"/>. Until something is selected there it shows an
/// ISS element set that is also committed in the parser tests.
/// </para>
/// </remarks>
internal sealed class ElementInspectorPanel : Panel
{

	private const string DefaultLine1 = "1 25544U 98067A   26258.88499338  .00006292  00000+0  12172-3 0  9999";
	private const string DefaultLine2 = "2 25544  51.6310 211.2092 0004923 144.3133 215.8185 15.49128922585812";
	private const double MinutesPerDay = 1440.0;
	private const double MetersPerKilometer = 1000.0;

	private const double SecondsPerDay = 86400.0;

	private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

	private static readonly FieldRow[] FieldRows =
	[
		new("MEAN_MOTION", "rev/day", nameof(ElementSet.MeanMotion), static e => e.MeanMotion, static (e, v) => e with { MeanMotion = v }),
		new("ECCENTRICITY", string.Empty, nameof(ElementSet.Eccentricity), static e => e.Eccentricity, static (e, v) => e with { Eccentricity = v }),
		new("INCLINATION", "deg", nameof(ElementSet.Inclination), static e => e.Inclination, static (e, v) => e with { Inclination = v }),
		new("RA_OF_ASC_NODE", "deg", nameof(ElementSet.RightAscensionOfAscendingNode), static e => e.RightAscensionOfAscendingNode,
			static (e, v) => e with { RightAscensionOfAscendingNode = v }),
		new("ARG_OF_PERICENTER", "deg", nameof(ElementSet.ArgumentOfPericenter), static e => e.ArgumentOfPericenter,
			static (e, v) => e with { ArgumentOfPericenter = v }),
		new("MEAN_ANOMALY", "deg", nameof(ElementSet.MeanAnomaly), static e => e.MeanAnomaly, static (e, v) => e with { MeanAnomaly = v }),
		new("BSTAR", "1/Earth radii", nameof(ElementSet.BStar), static e => e.BStar, static (e, v) => e with { BStar = v }),
		new("MEAN_MOTION_DOT", "rev/day^2", nameof(ElementSet.MeanMotionDot), static e => e.MeanMotionDot, static (e, v) => e with { MeanMotionDot = v }),
	];

	private static readonly ImGuiWidgets.PropertyGridOptions GridOptions = new()
	{
		DoubleFormat = "%.12g",
		LabelColumnWeight = 0.65f,
	};

	private static readonly ElementSet DefaultElements = TleParser.Parse(DefaultLine1, DefaultLine2, "ISS (ZARYA)");

	private ElementSet asWritten = DefaultElements;
	private ElementSet current = DefaultElements;
	private float horizonDays = 1.0f;
	private Measurement? measurement;
	private long seenSelectionVersion;

	/// <inheritdoc/>
	protected override string Title => "Element inspector";

	/// <summary>
	/// Shows an element set. The catalogue selection calls this through <see cref="Draw"/>.
	/// </summary>
	/// <param name="elements">The element set as written.</param>
	internal void Select(ElementSet elements)
	{
		Ensure.NotNull(elements);

		asWritten = elements;
		current = elements;
		measurement = null;
	}

	/// <summary>
	/// Restores the panel's start-up state.
	/// </summary>
	internal void ResetState()
	{
		Select(DefaultElements);
		horizonDays = 1.0f;
	}

	/// <inheritdoc/>
	protected override void Draw()
	{
		if (CatalogueSelection.Version != seenSelectionVersion)
		{
			seenSelectionVersion = CatalogueSelection.Version;

			if (CatalogueSelection.Selected is { } selected)
			{
				Select(selected);
			}
		}

		ImGui.TextUnformatted($"{current.ObjectName}  ({current.NoradCatalogId.ToString(Culture)}), read from {FormatName(current.Format)}");

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
		foreach (FieldRow row in FieldRows)
		{
			changed |= DrawField(grid, row);
		}

		DrawMeanMotionDdot(grid);

		if (changed)
		{
			measurement = null;
		}
	}

	/// <summary>Gets the fields whose rows show a measured "dr per step", in the order they are drawn.</summary>
	/// <remarks>So a test can hold the panel against <see cref="DataTerm.PerturbedFields"/>.</remarks>
	internal static IReadOnlyList<string> MeasuredFields { get; } = [nameof(ElementSet.Epoch), .. Array.ConvertAll(FieldRows, static r => r.Field)];

	/// <summary>One editable field of the element set and the <see cref="DataTerm"/> field it is measured as.</summary>
	/// <param name="OmmName">The OMM keyword the row is labelled with.</param>
	/// <param name="Unit">The unit shown beside the value.</param>
	/// <param name="Field">The field as <see cref="DataTerm.Contribution.Field"/> names it.</param>
	/// <param name="Get">Reads the field.</param>
	/// <param name="Set">Writes the field.</param>
	private readonly record struct FieldRow(string OmmName, string Unit, string Field, Func<ElementSet, double> Get, Func<ElementSet, double, ElementSet> Set);

	/// <summary>
	/// The data term for the element set on screen, keyed by <see cref="DataTerm.Contribution.Field"/>.
	/// </summary>
	/// <param name="PerField">Each measured field's half-step position shift, in kilometres.</param>
	/// <param name="CombinedMeters">The combined term, in metres.</param>
	/// <param name="Error">Why there is no measurement, or <see langword="null"/> when there is one.</param>
	internal readonly record struct Measurement(IReadOnlyDictionary<string, double> PerField, double CombinedMeters, string? Error);

	/// <summary>Measures the data term for an element set at a horizon, as the panel shows it.</summary>
	/// <param name="elements">The element set.</param>
	/// <param name="days">The horizon, in days.</param>
	/// <returns>The measurement, or one carrying the reason there is none.</returns>
	internal static Measurement Measure(ElementSet elements, float days)
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

	private void DrawIdentity(ImGuiWidgets.PropertyGrid grid)
	{
		using ImGuiWidgets.PropertyGrid.SectionScope section = grid.Section("Identity (not quantized orbit fields)", defaultOpen: false);

		ReadOnlyRow(grid, "OBJECT_NAME", current.ObjectName);
		ReadOnlyRow(grid, "OBJECT_ID", current.ObjectId);
		ReadOnlyRow(grid, "NORAD_CAT_ID", current.NoradCatalogId.ToString(Culture));
		ReadOnlyRow(grid, "ELEMENT_SET_NO", current.ElementSetNumber.ToString(Culture));
		ReadOnlyRow(grid, "REV_AT_EPOCH", current.RevolutionAtEpoch.ToString(Culture));
	}

	private void DrawEpoch(ImGuiWidgets.PropertyGrid grid)
	{
		using ImGuiWidgets.PropertyGrid.SectionScope section = grid.Section("EPOCH");

		ReadOnlyRow(grid, "Value##EPOCH", current.Epoch.ToString("yyyy-MM-ddTHH:mm:ss.ffffff", Culture));
		double step = StepOf(current, nameof(ElementSet.Epoch));
		ReadOnlyRow(grid, "Step##EPOCH", $"{Format(step, "G3")} day ({FormatSeconds(step * SecondsPerDay)})");
		ReadOnlyRow(grid, "dr per step##EPOCH", Sensitivity(measurement, nameof(ElementSet.Epoch)));
	}

	private void DrawMeanMotionDdot(ImGuiWidgets.PropertyGrid grid)
	{
		using ImGuiWidgets.PropertyGrid.SectionScope section = grid.Section("MEAN_MOTION_DDOT");

		ReadOnlyRow(grid, "Value##MEAN_MOTION_DDOT", $"{Format(current.MeanMotionDdot, "G6")} rev/day^3");
		ReadOnlyRow(grid, "Step##MEAN_MOTION_DDOT", Format(StepOf(current, nameof(ElementSet.MeanMotionDdot)), "G3"));
		ReadOnlyRow(grid, "dr per step##MEAN_MOTION_DDOT", "0 m: SGP4 never reads this field");
	}

	private bool DrawField(ImGuiWidgets.PropertyGrid grid, FieldRow row)
	{
		using ImGuiWidgets.PropertyGrid.SectionScope section = grid.Section(row.OmmName);

		double edited = row.Get(current);
		bool changed = grid.Value($"Value{(row.Unit.Length > 0 ? $" ({row.Unit})" : string.Empty)}##{row.OmmName}", ref edited);

		if (changed)
		{
			current = row.Set(current, edited);
		}

		ReadOnlyRow(grid, $"Step##{row.OmmName}", Format(StepOf(current, row.Field), "G3"));
		ReadOnlyRow(grid, $"dr per step##{row.OmmName}", Sensitivity(measurement, row.Field));

		return changed;
	}

	/// <summary>
	/// The step a field of an element set is written to, in the format that set was read from.
	/// </summary>
	/// <param name="elements">The element set.</param>
	/// <param name="field">The field, named as <see cref="DataTerm.Contribution.Field"/> names it.</param>
	/// <returns>The step, which for a measured field is the one <see cref="DataTerm"/> perturbs by.</returns>
	internal static double StepOf(ElementSet elements, string field)
	{
		ElementFieldSteps steps = ElementFieldQuantization.For(elements);

		return field switch
		{
			nameof(ElementSet.Epoch) => steps.EpochDays,
			nameof(ElementSet.MeanMotion) => steps.MeanMotion,
			nameof(ElementSet.Eccentricity) => steps.Eccentricity,
			nameof(ElementSet.Inclination) => steps.Inclination,
			nameof(ElementSet.RightAscensionOfAscendingNode) => steps.RightAscensionOfAscendingNode,
			nameof(ElementSet.ArgumentOfPericenter) => steps.ArgumentOfPericenter,
			nameof(ElementSet.MeanAnomaly) => steps.MeanAnomaly,
			nameof(ElementSet.MeanMotionDot) => steps.MeanMotionDot,
			nameof(ElementSet.BStar) => steps.StepForExponentialField(elements.BStar),
			nameof(ElementSet.MeanMotionDdot) => steps.StepForExponentialField(elements.MeanMotionDdot),
			_ => throw new ArgumentOutOfRangeException(nameof(field), field, "Not an element set field."),
		};
	}

	/// <summary>
	/// The "dr per step" text of one field: the measured half-step shift doubled to a whole step.
	/// </summary>
	/// <param name="measurement">The measurement on screen, or <see langword="null"/> before there is one.</param>
	/// <param name="field">The field, named as <see cref="DataTerm.Contribution.Field"/> names it.</param>
	/// <returns>The text, or a dash when nothing is measured for the field.</returns>
	internal static string Sensitivity(Measurement? measurement, string field)
	{
		if (measurement is not { Error: null } m)
		{
			return "-";
		}

		// Half a step was measured; a whole step is twice that to first order.
		return m.PerField.TryGetValue(field, out double halfStepKilometers)
			? $"{FormatMeters(2.0 * halfStepKilometers * MetersPerKilometer)} per step"
			: "-";
	}

	private static string FormatName(ElementSetFormat format) => format switch
	{
		ElementSetFormat.Omm => "OMM JSON",
		ElementSetFormat.Tle => "two-line text",
		_ => format.ToString(),
	};

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
