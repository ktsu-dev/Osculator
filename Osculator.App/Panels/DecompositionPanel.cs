// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Panels;

using System;
using System.Collections.Generic;
using System.Globalization;
using Hexa.NET.ImGui;
using ktsu.Osculator.App.Shell;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Residuals;
using ktsu.Osculator.Data.CelesTrak;
using ktsu.Osculator.Numerics.Precise;
using ktsu.PreciseNumber;

/// <summary>
/// The M5 decomposition for the selected object: Δ_model, Δ_data and Δ_arith(T), in RIC, between two
/// element sets from its archived history.
/// </summary>
/// <remarks>
/// <para>
/// This is <see cref="Decomposition{TReference}"/> drawn as a table, computed in 30-digit
/// <see cref="PreciseNumber"/> on the shell's work queue. The prediction is made from one archived
/// element set and compared with a later one, evaluated at its own epoch. By default those are the two
/// newest, which is the shortest horizon the history can offer, and either can be changed.
/// </para>
/// <para>
/// The history comes from <see cref="SnapshotStore"/>, which <see cref="SnapshotFeed"/> fills from
/// every CelesTrak response the application caches. CelesTrak serves only the current set, so a fresh
/// installation has one set per object and the panel says so: it takes a later fetch, at least a
/// refetch window later, before there is anything to compare against.
/// </para>
/// </remarks>
internal sealed class DecompositionPanel : Panel
{
	/// <summary>The digits the reference arithmetic carries, as in <c>DecompositionTests</c>.</summary>
	internal const int ReferenceDigits = 30;

	private const string HistoryKey = "decomposition-history";

	private const string ComputeKey = "decomposition-compute";

	private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

	private static readonly int[] SampleChoices = [64, 256, 1024];

	private static readonly string[] SampleNames = [.. Array.ConvertAll(SampleChoices, n => n.ToString(Culture))];

	private readonly SnapshotFeed? feed;

	private long seenSelectionVersion = -1;
	private int sampleChoice = 1;
	private string? status;

	/// <summary>
	/// Initializes a new instance of the <see cref="DecompositionPanel"/> class over the application's
	/// archive, and starts the feed that fills it.
	/// </summary>
	public DecompositionPanel()
		: this(SnapshotFeed.Application.Store, SnapshotFeed.Application) => SnapshotFeed.StartApplicationFeed();

	/// <summary>
	/// Initializes a new instance of the <see cref="DecompositionPanel"/> class over a given archive.
	/// </summary>
	/// <param name="store">The archive the history is read from.</param>
	/// <param name="feed">Swept before each read, so the history is current; or null.</param>
	internal DecompositionPanel(SnapshotStore store, SnapshotFeed? feed)
	{
		Store = Ensure.NotNull(store);
		this.feed = feed;
	}

	/// <summary>Gets the archive the history is read from.</summary>
	internal SnapshotStore Store { get; }

	/// <summary>Gets the object the history was read for, or null.</summary>
	internal ElementSet? Subject { get; private set; }

	/// <summary>Gets the selected object's archived history, or null while it is being read.</summary>
	internal SnapshotHistory? History { get; private set; }

	/// <summary>Gets the index in <see cref="History"/> of the set the prediction is made from.</summary>
	internal int PredictionIndex { get; private set; }

	/// <summary>Gets the index in <see cref="History"/> of the set taken as truth.</summary>
	internal int TruthIndex { get; private set; }

	/// <summary>Gets the latest decomposition, or null while none is ready.</summary>
	internal DecompositionResult<PreciseNumber>? Result { get; private set; }

	/// <summary>Gets a value indicating whether a decomposition is being computed.</summary>
	internal bool IsComputing => Work.IsRunning(ComputeKey);

	/// <summary>Gets the size of the Δ_data ensemble the next decomposition draws.</summary>
	internal int Samples => SampleChoices[sampleChoice];

	/// <inheritdoc/>
	protected override string Title => "Decomposition";

	/// <summary>
	/// Reads the history of an object and decomposes its two newest sets.
	/// </summary>
	/// <param name="elements">The object, as the catalogue selected it.</param>
	internal void Select(ElementSet elements)
	{
		Ensure.NotNull(elements);

		Subject = elements;
		History = null;
		Result = null;
		status = null;
		Work.Cancel(ComputeKey);

		int id = elements.NoradCatalogId;
		SnapshotFeed? sweeping = feed;
		SnapshotStore store = Store;

		Work.Run(
			HistoryKey,
			token =>
			{
				token.ThrowIfCancellationRequested();
				_ = sweeping?.Sweep();
				return store.ReadHistory(id);
			},
			history =>
			{
				History = history;

				if (history.ElementSets.Count >= 2)
				{
					Choose(history.ElementSets.Count - 2, history.ElementSets.Count - 1);
				}
			},
			error => status = error.GetBaseException().Message);
	}

	/// <summary>
	/// Decomposes the prediction from one archived set against a later one.
	/// </summary>
	/// <param name="prediction">The index of the set the prediction is made from.</param>
	/// <param name="truth">The index of the set taken as truth, which must be later.</param>
	internal void Choose(int prediction, int truth)
	{
		if (History is not { } history
			|| prediction < 0
			|| truth >= history.ElementSets.Count
			|| prediction >= truth)
		{
			return;
		}

		PredictionIndex = prediction;
		TruthIndex = truth;
		Result = null;
		status = null;

		ElementSet elements = history.ElementSets[prediction];
		ElementSet later = history.ElementSets[truth];
		DecompositionOptions options = new(Samples);

		Work.Run(
			ComputeKey,
			_ => Decomposition<PreciseNumber>.Compute(elements, later, new PreciseStorageMath(ReferenceDigits), options),
			result => Result = result,
			error => status = error.GetBaseException().Message);
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

		if (Subject is null)
		{
			ImGui.TextWrapped("Select an object in the catalogue to decompose the gap between its prediction and a later element set.");
			return;
		}

		ImGui.TextUnformatted($"{Subject.ObjectName}  ({Subject.NoradCatalogId.ToString(Culture)})");
		ImGui.SameLine();

		if (ImGui.Button("Reread history"))
		{
			Select(Subject);
		}

		if (status is not null)
		{
			ImGui.TextWrapped(status);
		}

		if (History is not { } history)
		{
			ImGui.TextUnformatted("Reading the archived history…");
			return;
		}

		if (history.UnreadableFiles.Count > 0)
		{
			ImGui.TextWrapped(string.Create(Culture, $"{history.UnreadableFiles.Count} archived file(s) could not be read and are left out."));
		}

		if (history.ElementSets.Count < 2)
		{
			ImGui.TextWrapped(DescribeShortHistory(history.ElementSets.Count));
			return;
		}

		DrawChoices(history);
		ImGui.Separator();

		if (Result is { } result)
		{
			DrawResult(result);
		}
		else if (IsComputing)
		{
			ImGui.TextUnformatted(string.Create(Culture, $"Decomposing in {ReferenceDigits}-digit PreciseNumber, {Samples} ensemble samples…"));
		}
	}

	/// <inheritdoc/>
	protected override void OnDismissed()
	{
		Work.Cancel(HistoryKey);
		Work.Cancel(ComputeKey);
	}

	/// <summary>Says what a history too short to decompose is missing.</summary>
	/// <param name="count">How many sets the archive holds for the object.</param>
	/// <returns>The explanation.</returns>
	internal static string DescribeShortHistory(int count) => count == 0
		? "No element set is archived for this object yet. Every set the application fetches from CelesTrak is archived, so the history starts with the next fetch."
		: "Only one element set is archived for this object. The decomposition needs two: one to predict from and a later one to take as truth. CelesTrak serves only the current set, so the next one arrives with a later fetch, once the cache's refetch window has passed.";

	/// <summary>Formats a length in kilometres, signed when asked, for a table cell.</summary>
	/// <param name="kilometres">The length.</param>
	/// <param name="signed">Whether to show the sign of a positive value.</param>
	/// <returns>The text.</returns>
	internal static string FormatKilometres(double kilometres, bool signed = true)
	{
		if (kilometres == 0.0)
		{
			return "0";
		}

		string text = Math.Abs(kilometres) is >= 1e-3 and < 1e5
			? kilometres.ToString("F4", Culture)
			: kilometres.ToString("0.000e+0", Culture);

		return signed && kilometres > 0 ? "+" + text : text;
	}

	/// <summary>Describes one Δ_arith row's figure, saying "0, every digit" where it is exactly zero.</summary>
	/// <param name="term">The term.</param>
	/// <param name="math">The reference arithmetic, for the magnitude.</param>
	/// <returns>The magnitude cell's text.</returns>
	internal static string DescribeArithmetic(ArithmeticTerm<PreciseNumber> term, IStorageMath<PreciseNumber> math)
	{
		if (!term.IsSuccess)
		{
			return $"refused: {term.Error}";
		}

		return term.IsExactlyZero ? "0, every digit" : FormatKilometres(Magnitude(term.Residual, math), signed: false);
	}

	/// <summary>Gets the name a storage type is shown under.</summary>
	/// <param name="storageType">The type.</param>
	/// <returns>The name.</returns>
	internal static string StorageName(Type storageType) =>
		storageType == typeof(float) ? "float"
		: storageType == typeof(double) ? "double"
		: storageType == typeof(decimal) ? "decimal"
		: storageType == typeof(PreciseNumber) ? string.Create(Culture, $"PreciseNumber ({ReferenceDigits} digits), against itself")
		: storageType.Name;

	/// <summary>The length of a residual's position part, in kilometres.</summary>
	/// <param name="residual">The residual.</param>
	/// <param name="math">The reference arithmetic.</param>
	/// <returns>The length, rounded to a <see langword="double"/> for display.</returns>
	internal static double Magnitude(RswResidual<PreciseNumber> residual, IStorageMath<PreciseNumber> math)
	{
		PreciseNumber squares = math.ToWorkingPrecision(
			(residual.Radial * residual.Radial) + (residual.AlongTrack * residual.AlongTrack) + (residual.CrossTrack * residual.CrossTrack));

		return double.CreateChecked(math.Sqrt(squares));
	}

	private static double D(PreciseNumber value) => double.CreateChecked(value);

	private static string Describe(ElementSet elements) => Describe(elements.Epoch);

	private static string Describe(DateTime epoch) =>
		epoch.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", Culture);

	private static void Row(string term, string radial, string alongTrack, string crossTrack, string magnitude, string note)
	{
		ImGui.TableNextRow();
		ImGui.TableNextColumn();
		ImGui.TextUnformatted(term);
		ImGui.TableNextColumn();
		ImGui.TextUnformatted(radial);
		ImGui.TableNextColumn();
		ImGui.TextUnformatted(alongTrack);
		ImGui.TableNextColumn();
		ImGui.TextUnformatted(crossTrack);
		ImGui.TableNextColumn();
		ImGui.TextUnformatted(magnitude);
		ImGui.TableNextColumn();
		ImGui.TextUnformatted(note);
	}

	private void DrawChoices(SnapshotHistory history)
	{
		IReadOnlyList<ElementSet> sets = history.ElementSets;

		int prediction = PredictionIndex;
		int truth = TruthIndex;
		bool changed = false;

		changed |= Combo("Predict from", sets, ref prediction, 0, sets.Count - 2);
		changed |= Combo("Truth", sets, ref truth, prediction + 1, sets.Count - 1);

		if (ImGui.Combo("Ensemble samples", ref sampleChoice, SampleNames, SampleNames.Length))
		{
			changed = true;
		}

		if (changed)
		{
			Choose(prediction, Math.Max(truth, prediction + 1));
		}

		ImGui.TextUnformatted(string.Create(Culture, $"{sets.Count} archived sets · horizon {(sets[TruthIndex].Epoch - sets[PredictionIndex].Epoch).TotalDays:F2} days"));
	}

	private static bool Combo(string label, IReadOnlyList<ElementSet> sets, ref int index, int first, int last)
	{
		bool changed = false;

		if (ImGui.BeginCombo(label, Describe(sets[index])))
		{
			for (int i = first; i <= last; i++)
			{
				if (ImGui.Selectable(Describe(sets[i]), i == index))
				{
					changed = i != index;
					index = i;
				}
			}

			ImGui.EndCombo();
		}

		return changed;
	}

	private static void DrawResult(DecompositionResult<PreciseNumber> result)
	{
		if (!result.IsSuccess)
		{
			ImGui.TextWrapped($"SGP4 refused the prediction or the truth: {result.Error}. No term is computed.");
			return;
		}

		PreciseStorageMath math = new(ReferenceDigits);

		ImGui.TextWrapped(string.Create(Culture, $"From {Describe(result.Elements)} to {Describe(result.Truth.Epoch)}, {D(result.HorizonMinutes) / 1440.0:F2} days. Every term in km, in RIC, computed and accumulated in {ReferenceDigits}-digit PreciseNumber."));

		const ImGuiTableFlags Flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit;

		if (!ImGui.BeginTable("decomposition", 6, Flags))
		{
			return;
		}

		ImGui.TableSetupColumn("Term");
		ImGui.TableSetupColumn("Radial");
		ImGui.TableSetupColumn("Along-track");
		ImGui.TableSetupColumn("Cross-track");
		ImGui.TableSetupColumn("|r|");
		ImGui.TableSetupColumn("Note", ImGuiTableColumnFlags.WidthStretch);
		ImGui.TableHeadersRow();

		RswResidual<PreciseNumber> model = result.Model;
		Row(
			"Δ_model",
			FormatKilometres(D(model.Radial)),
			FormatKilometres(D(model.AlongTrack)),
			FormatKilometres(D(model.CrossTrack)),
			FormatKilometres(Magnitude(model, math), signed: false),
			"prediction minus the later set, at its epoch");

		DataTermEnsemble<PreciseNumber> data = result.Data;
		bool anyAccepted = data.Refusals < data.Samples;
		Row(
			"Δ_data (RMS)",
			anyAccepted ? FormatKilometres(D(data.RmsRadial), signed: false) : "-",
			anyAccepted ? FormatKilometres(D(data.RmsAlongTrack), signed: false) : "-",
			anyAccepted ? FormatKilometres(D(data.RmsCrossTrack), signed: false) : "-",
			anyAccepted ? FormatKilometres(D(data.RmsMagnitude), signed: false) : "-",
			string.Create(Culture, $"{data.Samples} samples, {data.Refusals} refused; furthest {(anyAccepted ? FormatKilometres(D(data.MaxMagnitude), signed: false) : "-")}"));

		foreach (ArithmeticTerm<PreciseNumber> term in result.Arithmetic)
		{
			string name = "Δ_arith " + StorageName(term.StorageType);
			string time = string.Create(Culture, $"{term.PropagationTime.TotalMilliseconds:F2} ms");

			if (!term.IsSuccess)
			{
				Row(name, "-", "-", "-", DescribeArithmetic(term, math), time);
				continue;
			}

			RswResidual<PreciseNumber> residual = term.Residual;
			Row(
				name,
				FormatKilometres(D(residual.Radial)),
				FormatKilometres(D(residual.AlongTrack)),
				FormatKilometres(D(residual.CrossTrack)),
				DescribeArithmetic(term, math),
				term.IsExactlyZero ? time + " · the harness holds all but the arithmetic fixed" : time);
		}

		ImGui.EndTable();
	}
}
