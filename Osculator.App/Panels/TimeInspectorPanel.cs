// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Panels;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;
using ktsu.Osculator.Numerics.Precise;

/// <summary>
/// The time inspector: the Julian date staircase, drawn.
/// </summary>
/// <remarks>
/// <para>
/// Sweeps requested time continuously across a span of microseconds and plots the propagated
/// along-track position for each way of carrying the instant. One <see langword="double"/> Julian date
/// is a staircase with 40.2-microsecond treads; the two-part form and a <c>PreciseNumber</c> date are
/// one line, drawn over each other. <see langword="float"/> and <see langword="decimal"/> are there to
/// switch on: a <see langword="float"/> date cannot move at all inside a quarter of a day, and a
/// <see langword="decimal"/> one is a line.
/// </para>
/// <para>
/// The sweep runs off the render thread, because the 30-digit series takes around a second for a
/// hundred samples, and the result is swapped in whole when it finishes. All the arithmetic is in
/// <see cref="JulianDateStaircase"/>, which is tested without a window; this class only draws it.
/// </para>
/// <para>
/// Until the catalogue's shared selection exists this sweeps one committed ISS element set.
/// </para>
/// </remarks>
internal sealed class TimeInspectorPanel
{
	/// <summary>The panel's title.</summary>
	internal const string Title = "Time inspector";

	/// <summary>The working precision of the <c>PreciseNumber</c> series, in significant digits.</summary>
	private const int PreciseDigits = 30;

	/// <summary>Gets the element set being swept: one ISS element set, until there is a selection to read.</summary>
	internal static ElementSet Elements { get; } = TleParser.Parse(
		"1 25544U 98067A   26258.88499338  .00006292  00000+0  12172-3 0  9999",
		"2 25544  51.6310 211.2092 0004923 144.3133 215.8185 15.49128922585812",
		"ISS (ZARYA)");

	private readonly Lock gate = new();

	private float startSeconds = 600.0f;
	private float spanMicroseconds = 400.0f;
	private int samples = 161;
	private bool includeFloat;
	private bool includeDecimal;
	private bool subtractLine;

	private IReadOnlyList<Series>? result;
	private string? failure;
	private bool running;

	/// <summary>
	/// Draws the panel's contents for one frame.
	/// </summary>
	internal void Draw()
	{
		ElementSet elements = Elements;
		double tread = JulianDateStaircase.TreadSeconds(elements.EpochJulianDate);

		ImGui.TextWrapped(
			$"{elements.ObjectName}. One double Julian date near {(elements.EpochJulianDate.Day + elements.EpochJulianDate.DayFraction).ToString("F0", CultureInfo.InvariantCulture)} " +
			$"resolves {(tread * 1e6).ToString("F2", CultureInfo.InvariantCulture)} µs, so time reaches the propagator in steps.");

		ImGui.InputFloat("Start (s after epoch)", ref startSeconds, 1.0f, 60.0f, "%.3f");
		ImGui.InputFloat("Span (µs)", ref spanMicroseconds, 10.0f, 100.0f, "%.1f");
		ImGui.SliderInt("Samples", ref samples, 2, 400);
		ImGui.Checkbox("float", ref includeFloat);
		ImGui.SameLine();
		ImGui.Checkbox("decimal", ref includeDecimal);
		ImGui.SameLine();
		ImGui.Checkbox("Subtract the line", ref subtractLine);

		bool busy;
		IReadOnlyList<Series>? series;
		string? error;

		lock (gate)
		{
			busy = running;
			series = result;
			error = failure;
		}

		ImGui.BeginDisabled(busy);

		if (ImGui.Button(busy ? "Sweeping…" : "Sweep"))
		{
			Start();
		}

		ImGui.EndDisabled();

		if (error is not null)
		{
			ImGui.TextWrapped(error);
		}

		if (series is null)
		{
			// The first frame starts a sweep on its own; a failed one waits for the button.
			if (!busy && error is null)
			{
				Start();
			}

			return;
		}

		DrawPlot(series);
	}

	/// <summary>Starts a sweep with the current settings, off the render thread.</summary>
	private void Start()
	{
		ElementSet elements = Elements;
		double start = startSeconds;
		double span = Math.Max(spanMicroseconds, 0.001) * 1e-6;
		int count = Math.Max(samples, 2);
		bool withFloat = includeFloat;
		bool withDecimal = includeDecimal;

		lock (gate)
		{
			running = true;
			failure = null;
		}

		_ = Task.Run(() =>
		{
			try
			{
				List<Series> computed =
				[
					new("double, one value", JulianDateStaircase.Sweep(elements, start, span, count, JulianDateMode.SingleValue, DoubleStorageMath.Instance)),
					new("double, two-part", JulianDateStaircase.Sweep(elements, start, span, count, JulianDateMode.TwoPart, DoubleStorageMath.Instance)),
					new($"PreciseNumber ({PreciseDigits} digits), one value", JulianDateStaircase.Sweep(elements, start, span, count, JulianDateMode.SingleValue, new PreciseStorageMath(PreciseDigits))),
				];

				if (withFloat)
				{
					computed.Add(new("float, one value", JulianDateStaircase.Sweep(elements, start, span, count, JulianDateMode.SingleValue, FloatStorageMath.Instance)));
				}

				if (withDecimal)
				{
					computed.Add(new("decimal, one value", JulianDateStaircase.Sweep(elements, start, span, count, JulianDateMode.SingleValue, DecimalStorageMath.Instance)));
				}

				lock (gate)
				{
					result = computed;
					running = false;
				}
			}
			catch (Exception exception) when (exception is ArgumentException or ArithmeticException or InvalidOperationException)
			{
				lock (gate)
				{
					failure = exception.Message;
					running = false;
				}
			}
		});
	}

	/// <summary>Plots every series against requested time.</summary>
	/// <param name="series">The completed sweep.</param>
	private void DrawPlot(IReadOnlyList<Series> series)
	{
		// The line is the PreciseNumber series end to end; subtracting it leaves each mode's
		// departure from exact time, which is where a 31-centimetre riser is easiest to read.
		IReadOnlyList<JulianDateStaircaseSample> line = series[2].Samples;
		double origin = line[0].RequestedSeconds;
		double slope = (line[^1].AlongTrackMeters - line[0].AlongTrackMeters) / (line[^1].RequestedSeconds - origin);
		double intercept = line[0].AlongTrackMeters;

		if (!ImPlot.BeginPlot("##staircase", new System.Numerics.Vector2(-1.0f, -1.0f)))
		{
			return;
		}

		ImPlot.SetupAxes("Requested time since start (µs)", subtractLine ? "Departure from exact time (m)" : "Along-track position (m)");

		foreach (Series s in series)
		{
			double[] xs = new double[s.Samples.Count];
			double[] ys = new double[s.Samples.Count];

			for (int i = 0; i < xs.Length; i++)
			{
				JulianDateStaircaseSample sample = s.Samples[i];
				xs[i] = (sample.RequestedSeconds - origin) * 1e6;
				ys[i] = subtractLine
					? sample.AlongTrackMeters - (intercept + (slope * (sample.RequestedSeconds - origin)))
					: sample.AlongTrackMeters;
			}

			ImPlot.PlotLine(s.Label, ref xs[0], ref ys[0], xs.Length);
		}

		ImPlot.EndPlot();
	}

	/// <summary>One labelled sweep.</summary>
	/// <param name="Label">The legend entry.</param>
	/// <param name="Samples">The samples.</param>
	private sealed record Series(string Label, IReadOnlyList<JulianDateStaircaseSample> Samples);
}
