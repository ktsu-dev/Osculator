// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Panels;

using System.Globalization;
using Hexa.NET.ImGui;
using ktsu.Osculator.App.Shell;

/// <summary>
/// Spec demonstration 3: the naive Newton solver and the universal-variable solver for Kepler's
/// equation, side by side in every storage type, near perigee of an eccentric orbit.
/// </summary>
/// <remarks>
/// The computation is <see cref="KeplerDemo.Compute"/>, run on the shell's work queue whenever the
/// inputs change. Each row shows the eccentric anomaly a solver reached and how many of the type's
/// own digits it lost against a sixty-digit reference, in the anomaly and in the position it implies.
/// </remarks>
internal sealed class KeplerDemoPanel : Panel
{
	/// <summary>The panel's title.</summary>
	internal const string PanelTitle = "Kepler solvers";

	/// <summary>The background job key.</summary>
	internal const string ComputeKey = "kepler-demo";

	private double eccentricity = KeplerDemo.MolniyaEccentricity;
	private double meanAnomaly = KeplerDemo.DefaultMeanAnomaly;
	private string? status;
	private (double Eccentricity, double MeanAnomaly)? requested;

	/// <summary>Gets the latest finished run.</summary>
	internal KeplerDemoResult? Result { get; private set; }

	/// <inheritdoc/>
	protected override string Title => PanelTitle;

	/// <summary>
	/// Sets the inputs, as the controls do, and starts a run if they changed.
	/// </summary>
	/// <param name="newEccentricity">The eccentricity.</param>
	/// <param name="newMeanAnomaly">The mean anomaly past perigee, in radians.</param>
	internal void Select(double newEccentricity, double newMeanAnomaly)
	{
		eccentricity = newEccentricity;
		meanAnomaly = newMeanAnomaly;
		StartIfChanged();
	}

	/// <inheritdoc/>
	protected override void Draw()
	{
		ImGui.TextWrapped("Kepler's equation near perigee, solved by a naive Newton iteration on E − e·sin E = M and by universal variables, in each storage type. Digits lost are counted against a 60-digit PreciseNumber reference.");

		ImGui.InputDouble("Eccentricity", ref eccentricity, 0.01, 0.1, "%.6f");
		ImGui.InputDouble("Mean anomaly past perigee (rad)", ref meanAnomaly, 0.0, 0.0, "%.3e");

		if (ImGui.Button("Molniya defaults"))
		{
			eccentricity = KeplerDemo.MolniyaEccentricity;
			meanAnomaly = KeplerDemo.DefaultMeanAnomaly;
		}

		if (!KeplerDemo.IsValid(eccentricity, meanAnomaly))
		{
			ImGui.TextDisabled("The eccentricity must be in [0, 0.999999] and the mean anomaly in (0, π].");
		}
		else
		{
			StartIfChanged();
		}

		if (status is not null)
		{
			ImGui.TextWrapped(status);
		}

		if (Work.IsRunning(ComputeKey))
		{
			ImGui.TextUnformatted("Solving in float, double, decimal and PreciseNumber…");
		}

		if (Result is not null)
		{
			ImGui.Separator();
			DrawResult(Result);
		}
	}

	/// <inheritdoc/>
	protected override void OnDismissed()
	{
		Work.Cancel(ComputeKey);
		requested = null;
	}

	private static string FormatLost(double lost, bool converged) =>
		converged ? lost.ToString("F2", CultureInfo.InvariantCulture) : "diverged";

	private static void DrawResult(KeplerDemoResult result)
	{
		CultureInfo culture = CultureInfo.InvariantCulture;
		ImGui.TextUnformatted(string.Create(culture, $"e = {result.Eccentricity:G6}, M = {result.MeanAnomaly:G6} rad, a = {KeplerDemo.SemiMajorAxis:N0} km"));

		const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersV | ImGuiTableFlags.SizingFixedFit;

		if (!ImGui.BeginTable("kepler-runs", 10, flags))
		{
			return;
		}

		ImGui.TableSetupColumn("Storage type");
		ImGui.TableSetupColumn("Digits");
		ImGui.TableSetupColumn("Naive E (rad)");
		ImGui.TableSetupColumn("E lost");
		ImGui.TableSetupColumn("r lost");
		ImGui.TableSetupColumn("Universal E (rad)");
		ImGui.TableSetupColumn("E lost##u");
		ImGui.TableSetupColumn("r lost##u");
		ImGui.TableSetupColumn("Steps n/u");
		ImGui.TableSetupColumn("Time");
		ImGui.TableHeadersRow();

		foreach (KeplerRun run in result.Runs)
		{
			ImGui.TableNextRow();
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(run.StorageName);
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(run.AvailableDigits.ToString("F1", culture));
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(run.NaiveConverged ? run.NaiveAnomaly.ToString("G12", culture) : "—");
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(FormatLost(run.NaiveAnomalyLost, run.NaiveConverged));
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(FormatLost(run.NaivePositionLost, run.NaiveConverged));
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(run.UniversalConverged ? run.UniversalAnomaly.ToString("G12", culture) : "—");
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(FormatLost(run.UniversalAnomalyLost, run.UniversalConverged));
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(FormatLost(run.UniversalPositionLost, run.UniversalConverged));
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(string.Create(culture, $"{run.NaiveIterations}/{run.UniversalIterations}"));
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(MeasuredDurations.Show(string.Create(culture, $"{run.Seconds * 1000.0:F1} ms")));
		}

		ImGui.EndTable();

		ImGui.TextWrapped("At e = 0.74 E − e·sin E cancels terms in the ratio 1 : 0.74, which costs about half a digit, so float, double and PreciseNumber lose under a digit either way. decimal loses more because its precision is absolute and E near perigee is small. Raise the eccentricity towards one to watch the cancellation grow, and the naive starting guess E₀ = M give out.");
	}

	private void StartIfChanged()
	{
		if (!KeplerDemo.IsValid(eccentricity, meanAnomaly) || requested == (eccentricity, meanAnomaly))
		{
			return;
		}

		double e = eccentricity;
		double m = meanAnomaly;
		requested = (e, m);
		status = null;

		Work.Run(
			ComputeKey,
			token => KeplerDemo.Compute(e, m, token),
			finished => Result = finished,
			error => status = error.GetBaseException().Message);
	}
}
