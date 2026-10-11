// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Shell;

/// <summary>
/// Decides whether the panels show the wall-clock durations they measure.
/// </summary>
/// <remarks>
/// <para>
/// A duration read off a stopwatch is a fact about the machine and the moment rather than about the
/// arithmetic, so it differs on every run. The application always shows them, because the trade a
/// reader is weighing is digits against time.
/// </para>
/// <para>
/// The gallery hides them, so that each picture is a function of the code that draws it and nothing
/// else, and a regeneration commits nothing when nothing has changed. It says <see cref="HiddenText"/>
/// in the cell rather than leaving it blank, so a picture never reads as a measurement that failed.
/// </para>
/// </remarks>
internal static class MeasuredDurations
{
	/// <summary>What a hidden duration reads as.</summary>
	internal const string HiddenText = "hidden";

	/// <summary>
	/// Gets or sets a value indicating whether measured durations are hidden. Off unless a test turns it on.
	/// </summary>
	internal static bool Hidden { get; set; }

	/// <summary>
	/// Returns what a cell holding a measured duration should read.
	/// </summary>
	/// <param name="measured">The duration as the panel would show it.</param>
	/// <returns><paramref name="measured"/>, or <see cref="HiddenText"/> while durations are hidden.</returns>
	internal static string Show(string measured) => Hidden ? HiddenText : measured;
}
