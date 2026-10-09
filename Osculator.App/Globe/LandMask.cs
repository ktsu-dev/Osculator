// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Globe;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Answers whether a latitude and longitude are on land, from a grid filled once at start-up.
/// </summary>
/// <remarks>
/// <para>
/// The outlines are scan-filled in longitude–latitude space rather than in any one projection, so
/// every projection the globe draws reads the same grid and none of them has to know about
/// polygons. A point-in-polygon test per pixel would be correct too, and would cost five thousand
/// edge tests for each of half a million pixels on every redraw.
/// </para>
/// <para>
/// <strong>Even-odd, not non-zero.</strong> Natural Earth stores lakes as rings inside their
/// continent, and the even-odd rule makes those holes without the data having to say which rings
/// are holes. Rings that cross the antimeridian are already split there by the source, so a scan
/// line never has to wrap.
/// </para>
/// </remarks>
internal sealed class LandMask
{
	private readonly BitArray cells;

	/// <summary>
	/// Initializes a new instance of the <see cref="LandMask"/> class.
	/// </summary>
	/// <param name="cellsPerDegree">Grid resolution. Four is a quarter of a degree, about 28 km.</param>
	/// <param name="outlines">The rings, in the format <see cref="LandOutlines.Rings"/> uses.</param>
	internal LandMask(int cellsPerDegree, string outlines)
	{
		if (cellsPerDegree < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(cellsPerDegree), cellsPerDegree, "At least one cell per degree is needed.");
		}

		CellsPerDegree = cellsPerDegree;
		Columns = 360 * cellsPerDegree;
		Rows = 180 * cellsPerDegree;
		cells = new BitArray(Columns * Rows);
		Fill(ParseRings(Ensure.NotNull(outlines)));
	}

	/// <summary>Gets the mask at a quarter of a degree, built from the compiled-in outlines.</summary>
	internal static LandMask Default => LazyDefault.Value;

	/// <summary>Gets the grid resolution, in cells per degree.</summary>
	internal int CellsPerDegree { get; }

	/// <summary>Gets the number of longitude columns.</summary>
	internal int Columns { get; }

	/// <summary>Gets the number of latitude rows.</summary>
	internal int Rows { get; }

	private static Lazy<LandMask> LazyDefault { get; } = new(() => new LandMask(4, LandOutlines.Rings));

	/// <summary>
	/// Whether a point is on land.
	/// </summary>
	/// <param name="latitudeDegrees">Latitude, in degrees; clamped to [−90, 90].</param>
	/// <param name="longitudeDegrees">Longitude, in degrees east; any value, wrapped.</param>
	/// <returns><see langword="true"/> when the cell holding the point is land.</returns>
	internal bool IsLand(double latitudeDegrees, double longitudeDegrees)
	{
		double wrapped = longitudeDegrees - (360.0 * Math.Floor((longitudeDegrees + 180.0) / 360.0));
		int column = Math.Clamp((int)((wrapped + 180.0) * CellsPerDegree), 0, Columns - 1);
		int row = Math.Clamp((int)((90.0 - latitudeDegrees) * CellsPerDegree), 0, Rows - 1);
		return cells[(row * Columns) + column];
	}

	/// <summary>
	/// Reads the outline text into rings of (longitude, latitude) pairs.
	/// </summary>
	/// <param name="outlines">The outline text.</param>
	/// <returns>The rings.</returns>
	internal static List<List<(double Longitude, double Latitude)>> ParseRings(string outlines)
	{
		List<List<(double Longitude, double Latitude)>> rings = [];
		List<(double Longitude, double Latitude)> ring = [];

		foreach (string token in outlines.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
		{
			if (token == "|")
			{
				if (ring.Count >= 3)
				{
					rings.Add(ring);
				}

				ring = [];
				continue;
			}

			int comma = token.IndexOf(',', StringComparison.Ordinal);
			ring.Add((
				double.Parse(token.AsSpan(0, comma), NumberStyles.Float, CultureInfo.InvariantCulture),
				double.Parse(token.AsSpan(comma + 1), NumberStyles.Float, CultureInfo.InvariantCulture)));
		}

		return rings;
	}

	/// <summary>
	/// Scan-fills every ring into the grid by the even-odd rule.
	/// </summary>
	/// <param name="rings">The rings.</param>
	/// <remarks>
	/// Each row is sampled at its centre latitude. The crossings of that latitude by every ring are
	/// sorted together and filled in pairs, first to second, third to fourth: pairing across all the
	/// rings at once, rather than ring by ring, is what makes a hole cancel its continent, which is
	/// the even-odd rule written as arithmetic.
	/// </remarks>
	private void Fill(List<List<(double Longitude, double Latitude)>> rings)
	{
		List<double> crossings = [];

		for (int row = 0; row < Rows; row++)
		{
			double latitude = 90.0 - ((row + 0.5) / CellsPerDegree);
			crossings.Clear();

			foreach (List<(double Longitude, double Latitude)> ring in rings)
			{
				for (int i = 0; i < ring.Count; i++)
				{
					(double x0, double y0) = ring[i];
					(double x1, double y1) = ring[(i + 1) % ring.Count];

					// Half-open in latitude, so a vertex exactly on the scan line is counted once.
					if ((y0 <= latitude) != (y1 <= latitude))
					{
						crossings.Add(x0 + ((latitude - y0) / (y1 - y0) * (x1 - x0)));
					}
				}
			}

			crossings.Sort();

			for (int i = 0; i + 1 < crossings.Count; i += 2)
			{
				int start = Math.Max(0, (int)Math.Ceiling(((crossings[i] + 180.0) * CellsPerDegree) - 0.5));
				int end = Math.Min(Columns - 1, (int)Math.Floor(((crossings[i + 1] + 180.0) * CellsPerDegree) - 0.5));

				for (int column = start; column <= end; column++)
				{
					cells[(row * Columns) + column] = true;
				}
			}
		}
	}
}
