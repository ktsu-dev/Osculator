// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Hexa.NET.ImGui;
using ktsu.Osculator.App.Shell;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Storage;
using ktsu.Osculator.Numerics.Precise;

/// <summary>
/// Propagates one object in all four storage types and reports the arithmetic error of each.
/// </summary>
/// <remarks>
/// <para>
/// This is the panel the application exists for. For a chosen object and a chosen time since its
/// epoch it runs SGP4 in <see langword="float"/>, <see langword="double"/>, <see langword="decimal"/>
/// and <c>PreciseNumber</c> from the same element set, and reports how far each lands from the
/// <c>PreciseNumber</c> run. The algorithm and the inputs are held fixed, so that distance is
/// Δ_arith and nothing else. The wall-clock cost of one propagation sits beside it, because the
/// trade a reader is weighing is digits against time.
/// </para>
/// <para>
/// Above it stays the table the panel started as: the smallest step each type can represent at an
/// orbital radius, which is a property of the arithmetic alone and needs no propagator.
/// </para>
/// </remarks>
internal static class StorageComparisonPanel
{
	/// <summary>A nominal low Earth orbital radius, in metres.</summary>
	private const double LeoRadiusMeters = 7_000_000.0;

	/// <summary>How long the timing loop keeps repeating a propagation for, in milliseconds.</summary>
	/// <remarks>
	/// One <see langword="double"/> propagation takes microseconds, which a single stopwatch reading
	/// cannot resolve reliably, so the fast types are repeated and averaged. A <c>PreciseNumber</c>
	/// propagation takes longer than this on its own and is therefore timed once.
	/// </remarks>
	private const double TimingBudgetMilliseconds = 50.0;

	/// <summary>The most repetitions the timing loop makes, whatever the budget.</summary>
	private const int MaximumTimingRepetitions = 10_000;

	/// <summary>Minutes in a day, for the Δt shortcuts.</summary>
	private const double MinutesPerDay = 1440.0;

	private static readonly IReadOnlyList<IStorageProfile> Profiles =
	[
		new ktsu.Osculator.Storage.FloatStorageProfile(),
		new ktsu.Osculator.Storage.DoubleStorageProfile(),
		new ktsu.Osculator.Storage.DecimalStorageProfile(),
		new ktsu.Osculator.Storage.PreciseStorageProfile(),
	];

	/// <summary>
	/// The objects offered until the catalogue panel supplies a selection.
	/// </summary>
	/// <remarks>
	/// One per regime that behaves differently in the arithmetic: a low orbit, a sun-synchronous one,
	/// a geostationary one, and two highly eccentric deep-space ones. All but the station come from
	/// the published verification set, so every one of them is a case SGP4 is known to reproduce.
	/// </remarks>
	private static readonly IReadOnlyList<ElementSet> Samples =
	[
		TleParser.Parse(
			"1 25544U 98067A   26258.88499338  .00006292  00000+0  12172-3 0  9999",
			"2 25544  51.6310 211.2092 0004923 144.3133 215.8185 15.49128922585812",
			"ISS (ZARYA)"),
		TleParser.Parse(
			"1 28057U 03049A   06177.78615833  .00000060  00000-0  35940-4 0  1836",
			"2 28057  98.4283 247.6961 0000884  88.1964 271.9322 14.35478080140550",
			"28057 (sun-synchronous)"),
		TleParser.Parse(
			"1 28626U 05008A   06176.46683397 -.00000205  00000-0  10000-3 0  2190",
			"2 28626   0.0019 286.9433 0000335  13.7918  55.6504  1.00270176  4891",
			"28626 (geostationary)"),
		TleParser.Parse(
			"1 09880U 77021A   06176.56157475  .00000421  00000-0  10000-3 0  9814",
			"2 09880  64.5968 349.3786 7069051 270.0229  16.3320  2.00813614112380",
			"09880 (Molniya)"),
		TleParser.Parse(
			"1 21897U 92011A   06176.02341244 -.00001273  00000-0 -13525-3 0  3044",
			"2 21897  62.1749 198.0096 7421690 253.0462  20.1561  2.01269994104880",
			"21897 (Molniya, negative drag)"),
	];

	private static int selectedSample;
	private static double minutesSinceEpoch = MinutesPerDay;

	/// <summary>The inputs of the measurement most recently started, so a change is noticed.</summary>
	private static (int Sample, double Minutes)? requested;

	/// <summary>The measurement in flight, if any.</summary>
	private static Task<Comparison>? pending;

	/// <summary>How one storage type fared.</summary>
	/// <param name="StorageName">The storage type's name.</param>
	/// <param name="Error">The propagator's verdict in this type.</param>
	/// <param name="X">TEME x position, in km, converted to <see langword="double"/> for display.</param>
	/// <param name="Y">TEME y position, in km.</param>
	/// <param name="Z">TEME z position, in km.</param>
	/// <param name="SecondsPerPropagation">Wall-clock seconds for one propagation, initialization excluded.</param>
	internal sealed record StorageRun(string StorageName, Sgp4Error Error, double X, double Y, double Z, double SecondsPerPropagation);

	/// <summary>One object at one instant, in all four storage types.</summary>
	/// <param name="ObjectName">The object propagated.</param>
	/// <param name="Minutes">Minutes since the element set's epoch.</param>
	/// <param name="Runs">Float, double, decimal, then the reference, in that order.</param>
	internal sealed record Comparison(string ObjectName, double Minutes, IReadOnlyList<StorageRun> Runs)
	{
		/// <summary>Gets the reference run every other one is measured against.</summary>
		public StorageRun Reference => Runs[^1];

		/// <summary>
		/// Gets the distance between a run's position and the reference's, in km.
		/// </summary>
		/// <param name="run">The run to measure.</param>
		/// <returns>The arithmetic error, or <see langword="null"/> when either run produced no state.</returns>
		/// <remarks>
		/// Differenced in <see langword="double"/>, as gate 2 does. Rounding the reference to
		/// <see langword="double"/> puts a floor of about 1e-12 km under the result at an orbital
		/// radius: below <see langword="double"/>'s own error everywhere except at epoch, where the two
		/// meet, and the reason a <see langword="decimal"/> run can read as zero.
		/// </remarks>
		public double? ArithmeticErrorKm(StorageRun run)
		{
			Ensure.NotNull(run);

			if (run.Error != Sgp4Error.None || Reference.Error != Sgp4Error.None)
			{
				return null;
			}

			double dx = run.X - Reference.X;
			double dy = run.Y - Reference.Y;
			double dz = run.Z - Reference.Z;
			return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
		}
	}

	/// <summary>
	/// Draws the panel for one frame.
	/// </summary>
	internal static void Draw()
	{
		DrawResolutionTable();
		ImGui.Separator();
		DrawPropagationControls();
		Poll();
		DrawPropagationTable();
	}

	/// <summary>
	/// Gets the latest finished measurement, or <see langword="null"/> before the first one lands.
	/// </summary>
	internal static Comparison? Latest { get; private set; }

	/// <summary>
	/// Gets a value indicating whether a measurement is in flight.
	/// </summary>
	internal static bool IsMeasuring => pending is not null;

	/// <summary>
	/// Chooses the object and the time since its epoch, as the combo box and the Δt field would.
	/// </summary>
	/// <param name="objectName">The name of one of the sample objects the combo box offers.</param>
	/// <param name="minutes">Minutes since the object's epoch.</param>
	/// <exception cref="ArgumentException">No sample object has that name.</exception>
	internal static void Select(string objectName, double minutes)
	{
		int index = -1;

		for (int i = 0; i < Samples.Count; i++)
		{
			if (string.Equals(Samples[i].ObjectName, objectName, StringComparison.Ordinal))
			{
				index = i;
			}
		}

		if (index < 0)
		{
			throw new ArgumentException($"No sample object is named \"{objectName}\".", nameof(objectName));
		}

		selectedSample = index;
		minutesSinceEpoch = minutes;
	}

	/// <summary>
	/// Puts the panel back as the application first draws it, forgetting any measurement.
	/// </summary>
	/// <remarks>
	/// The panel's state is static and outlives a test's application, so a test that wants to see the
	/// first measurement rather than the last test's resets it first. A measurement still in flight is
	/// abandoned rather than awaited; it touches no state of the panel's when it finishes.
	/// </remarks>
	internal static void ResetState()
	{
		selectedSample = 0;
		minutesSinceEpoch = MinutesPerDay;
		requested = null;
		pending = null;
		Latest = null;
	}

	/// <summary>
	/// Propagates one element set in all four storage types and times each.
	/// </summary>
	/// <param name="elements">The element set.</param>
	/// <param name="minutes">Minutes since its epoch.</param>
	/// <returns>The four runs, reference last.</returns>
	/// <remarks>
	/// Internal rather than private so it can be exercised without a window. Nothing here touches
	/// ImGui, so it is safe to run off the render thread.
	/// </remarks>
	internal static Comparison Compare(ElementSet elements, double minutes)
	{
		Ensure.NotNull(elements);

		return new Comparison(
			elements.ObjectName ?? elements.NoradCatalogId.ToString(CultureInfo.InvariantCulture),
			minutes,
			[
				Run("float", elements, minutes, FloatStorageMath.Instance),
				Run("double", elements, minutes, DoubleStorageMath.Instance),
				Run("decimal", elements, minutes, DecimalStorageMath.Instance),
				Run($"PreciseNumber ({PreciseStorageMath.Instance.SignificantDigits} digits)", elements, minutes, PreciseStorageMath.Instance),
			]);
	}

	/// <summary>
	/// Propagates in one storage type, repeating the propagation until it can be timed.
	/// </summary>
	/// <typeparam name="T">The storage type.</typeparam>
	/// <param name="name">The name to report.</param>
	/// <param name="elements">The element set.</param>
	/// <param name="minutes">Minutes since its epoch.</param>
	/// <param name="math">The transcendental functions for <typeparamref name="T"/>.</param>
	/// <returns>The run.</returns>
	/// <remarks>
	/// Generic, so that one body serves all four types and the comparison cannot differ between them
	/// in anything but the arithmetic. The time is converted with <c>CreateChecked</c>, which for each
	/// type is the same conversion gate 2 makes by hand.
	/// </remarks>
	private static StorageRun Run<T>(string name, ElementSet elements, double minutes, IStorageMath<T> math)
		where T : struct, INumber<T>
	{
		Sgp4Satellite<T> satellite = Sgp4<T>.Initialize(elements, math);
		T time = T.CreateChecked(minutes);

		Stopwatch stopwatch = Stopwatch.StartNew();
		Sgp4Result<T> result = Sgp4<T>.Propagate(satellite, time, math);
		int repetitions = 1;

		while (stopwatch.Elapsed.TotalMilliseconds < TimingBudgetMilliseconds && repetitions < MaximumTimingRepetitions)
		{
			result = Sgp4<T>.Propagate(satellite, time, math);
			repetitions++;
		}

		stopwatch.Stop();

		return new StorageRun(
			name,
			result.Error,
			double.CreateChecked(result.State.X),
			double.CreateChecked(result.State.Y),
			double.CreateChecked(result.State.Z),
			stopwatch.Elapsed.TotalSeconds / repetitions);
	}

	private static void DrawResolutionTable()
	{
		ImGui.TextUnformatted("Smallest distinguishable step at a 7,000 km orbital radius");

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
				ImGui.TextUnformatted(profile.ApproximateSignificantDigits.ToString(CultureInfo.InvariantCulture));

				ImGui.TableNextColumn();
				ImGui.TextUnformatted(DescribeLength(profile.SmallestDistinguishableStepMeters(LeoRadiusMeters)));
			}

			ImGui.EndTable();
		}
	}

	private static void DrawPropagationControls()
	{
		ImGui.TextUnformatted("Arithmetic error of one SGP4 propagation, per storage type");

		if (ImGui.BeginCombo("Object", Samples[selectedSample].ObjectName ?? string.Empty))
		{
			for (int i = 0; i < Samples.Count; i++)
			{
				bool isSelected = i == selectedSample;

				if (ImGui.Selectable(Samples[i].ObjectName ?? string.Empty, isSelected))
				{
					selectedSample = i;
				}

				if (isSelected)
				{
					ImGui.SetItemDefaultFocus();
				}
			}

			ImGui.EndCombo();
		}

		ImGui.InputDouble("Minutes since epoch", ref minutesSinceEpoch, 60.0, MinutesPerDay, "%.3f");

		foreach (double days in (ReadOnlySpan<double>)[0.0, 1.0, 3.0, 7.0])
		{
			if (days > 0.0)
			{
				ImGui.SameLine();
			}

			if (ImGui.Button(string.Create(CultureInfo.InvariantCulture, $"{days:0} d")))
			{
				minutesSinceEpoch = days * MinutesPerDay;
			}
		}
	}

	/// <summary>
	/// Starts a measurement when the inputs have changed, and collects one that has finished.
	/// </summary>
	/// <remarks>
	/// A <c>PreciseNumber</c> propagation takes tens of milliseconds, so the work runs on the thread
	/// pool and the frame keeps drawing the previous result until the new one is ready. Only one
	/// measurement runs at a time: if the inputs change while one is in flight, the next starts when
	/// it finishes, so a held key on the Δt field does not queue a measurement per frame.
	/// </remarks>
	private static void Poll()
	{
		if (pending is { IsCompleted: true })
		{
			if (pending.IsCompletedSuccessfully)
			{
				Latest = pending.Result;
			}

			pending = null;
		}

		(int, double) wanted = (selectedSample, minutesSinceEpoch);

		if (pending is null && requested != wanted)
		{
			requested = wanted;
			ElementSet elements = Samples[selectedSample];
			double minutes = minutesSinceEpoch;
			pending = Task.Run(() => Compare(elements, minutes), CancellationToken.None);
		}
	}

	private static void DrawPropagationTable()
	{
		if (Latest is null)
		{
			ImGui.TextUnformatted("Propagating...");
			return;
		}

		Comparison comparison = Latest;
		double minutes = comparison.Minutes;

		ImGui.TextUnformatted(string.Create(
			CultureInfo.InvariantCulture,
			$"{comparison.ObjectName}, {minutes:0.###} min ({minutes / MinutesPerDay:0.###} d) from epoch{(pending is null ? string.Empty : " (updating)")}"));

		if (ImGui.BeginTable("arithmetic", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
		{
			ImGui.TableSetupColumn("Storage");
			ImGui.TableSetupColumn("Arithmetic error (position)");
			ImGui.TableSetupColumn("Wall-clock per propagation");
			ImGui.TableSetupColumn("Cost vs double");
			ImGui.TableHeadersRow();

			double doubleSeconds = comparison.Runs[1].SecondsPerPropagation;

			foreach (StorageRun run in comparison.Runs)
			{
				ImGui.TableNextRow();

				ImGui.TableNextColumn();
				ImGui.TextUnformatted(run.StorageName);

				ImGui.TableNextColumn();
				ImGui.TextUnformatted(DescribeError(comparison, run));

				ImGui.TableNextColumn();
				ImGui.TextUnformatted(MeasuredDurations.Show(DescribeDuration(run.SecondsPerPropagation)));

				ImGui.TableNextColumn();
				ImGui.TextUnformatted(MeasuredDurations.Show(string.Create(CultureInfo.InvariantCulture, $"×{run.SecondsPerPropagation / doubleSeconds:#,0.#}")));
			}

			ImGui.EndTable();
		}

		ImGui.TextWrapped(
			"The arithmetic error is each run's distance from the PreciseNumber run of the same element set at the same " +
			"instant, so the model and the inputs are held fixed and only the arithmetic differs. Compare it " +
			"with the tens of metres the element set's own quantization puts on the same propagation: for " +
			"double the arithmetic is many orders of magnitude below it, and float is wrong without saying so.");
	}

	private static string DescribeError(Comparison comparison, StorageRun run)
	{
		if (ReferenceEquals(run, comparison.Reference))
		{
			return run.Error == Sgp4Error.None ? "reference" : $"reference: {run.Error}";
		}

		if (run.Error != Sgp4Error.None)
		{
			return run.Error.ToString();
		}

		double? km = comparison.ArithmeticErrorKm(run);

		// Zero does not mean the two runs agree exactly: it means they round to the same double, and
		// one double step at an orbital radius is about a nanometre. Saying "zero" would claim more.
		return km switch
		{
			null => "no reference state",
			0.0 => "< 1 nm (below the display floor)",
			_ => DescribeLength(km.Value * 1000.0),
		};
	}

	/// <summary>
	/// Renders a length in whichever unit reads most naturally.
	/// </summary>
	/// <param name="meters">The length, in metres.</param>
	/// <returns>A short human-readable description.</returns>
	private static string DescribeLength(double meters)
	{
		CultureInfo culture = CultureInfo.InvariantCulture;

		return meters switch
		{
			>= 1000.0 => $"{(meters / 1000.0).ToString("F3", culture)} km",
			>= 1.0 => $"{meters.ToString("F3", culture)} m",
			>= 1e-3 => $"{(meters * 1e3).ToString("F3", culture)} mm",
			>= 1e-6 => $"{(meters * 1e6).ToString("F3", culture)} µm",
			>= 1e-9 => $"{(meters * 1e9).ToString("F3", culture)} nm",
			_ => $"{meters.ToString("E3", culture)} m",
		};
	}

	/// <summary>
	/// Renders a duration in whichever unit reads most naturally.
	/// </summary>
	/// <param name="seconds">The duration, in seconds.</param>
	/// <returns>A short human-readable description.</returns>
	private static string DescribeDuration(double seconds)
	{
		CultureInfo culture = CultureInfo.InvariantCulture;

		return seconds switch
		{
			>= 1.0 => $"{seconds.ToString("F3", culture)} s",
			>= 1e-3 => $"{(seconds * 1e3).ToString("F3", culture)} ms",
			_ => $"{(seconds * 1e6).ToString("F3", culture)} µs",
		};
	}
}
