// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App;

using System.Collections.Generic;
using Hexa.NET.ImGui;
using ktsu.Osculator.Core.Storage;

/// <summary>
/// Reports what each storage type can represent at an orbital radius.
/// </summary>
/// <remarks>
/// The first panel the application had, and the only one that needs no propagator: the figures here
/// are properties of the arithmetic alone. It is deliberately the storage comparison rather than a
/// globe, because the comparison is what the application is for — and because a panel showing that
/// <see langword="float"/> cannot express half a metre at this magnitude makes the point before any
/// orbit has been computed.
/// </remarks>
internal static class StorageComparisonPanel
{
	/// <summary>A nominal low Earth orbital radius, in metres.</summary>
	private const double LeoRadiusMeters = 7_000_000.0;

	private static readonly IReadOnlyList<IStorageProfile> Profiles =
	[
		new ktsu.Osculator.Storage.FloatStorageProfile(),
		new ktsu.Osculator.Storage.DoubleStorageProfile(),
		new ktsu.Osculator.Storage.DecimalStorageProfile(),
		new ktsu.Osculator.Storage.PreciseStorageProfile(),
	];

	/// <summary>
	/// Draws the panel for one frame.
	/// </summary>
	internal static void Draw()
	{
		ImGui.TextUnformatted("Smallest distinguishable step at a 7,000 km orbital radius");
		ImGui.Separator();

		if (ImGui.BeginTable("storage", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
		{
			ImGui.TableSetupColumn("Storage");
			ImGui.TableSetupColumn("Digits");
			ImGui.TableSetupColumn("Resolution");
			ImGui.TableHeadersRow();

			foreach (IStorageProfile profile in Profiles)
			{
				ImGui.TableNextRow();

				ImGui.TableNextColumn();
				ImGui.TextUnformatted(profile.StorageName);

				ImGui.TableNextColumn();
				ImGui.TextUnformatted(profile.ApproximateSignificantDigits.ToString(System.Globalization.CultureInfo.InvariantCulture));

				ImGui.TableNextColumn();
				ImGui.TextUnformatted(Describe(profile.SmallestDistinguishableStepMeters(LeoRadiusMeters)));
			}

			ImGui.EndTable();
		}

		ImGui.Separator();
		ImGui.TextWrapped(
			"No propagator yet. Milestone M1 is SGP4 generic over the storage type, gated on the " +
			"Vallado verification suite; nothing else in this repository means anything until that passes.");
	}

	/// <summary>
	/// Renders a step in whichever unit reads most naturally.
	/// </summary>
	/// <param name="meters">The step, in metres.</param>
	/// <returns>A short human-readable description.</returns>
	private static string Describe(double meters)
	{
		System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.InvariantCulture;

		return meters switch
		{
			>= 1.0 => $"{meters.ToString("F3", culture)} m",
			>= 1e-3 => $"{(meters * 1e3).ToString("F3", culture)} mm",
			>= 1e-6 => $"{(meters * 1e6).ToString("F3", culture)} µm",
			>= 1e-9 => $"{(meters * 1e9).ToString("F3", culture)} nm",
			_ => $"{meters.ToString("E3", culture)} m",
		};
	}
}
