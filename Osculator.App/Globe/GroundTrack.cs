// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Globe;

using System;
using System.Collections.Generic;
using System.Numerics;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Frames;
using ktsu.Osculator.Core.Propagation;
using ktsu.Osculator.Core.Time;

/// <summary>
/// Where an object is over the ground at one instant.
/// </summary>
/// <param name="InstantUtc">The instant, on the UTC scale.</param>
/// <param name="LatitudeDegrees">Geodetic latitude, in degrees.</param>
/// <param name="LongitudeDegrees">Longitude, in degrees east, in (−180, 180].</param>
/// <param name="AltitudeKilometers">Height above the WGS-84 ellipsoid, in kilometres.</param>
internal readonly record struct TrackPoint(DateTime InstantUtc, double LatitudeDegrees, double LongitudeDegrees, double AltitudeKilometers);

/// <summary>
/// Propagates an object and drops it onto the ground: TEME, then the Earth-fixed frame, then
/// geodetic latitude and longitude.
/// </summary>
/// <remarks>
/// <para>
/// In <see langword="double"/>, always. The globe shows where things are; which storage type was
/// used to work out <em>how wrong</em> they are is the residual's business, and a ground track
/// drawn in <see langword="float"/> would be 55 km of somebody else's point made on the wrong panel.
/// </para>
/// <para>
/// <see cref="EarthOrientation.Ignored"/> is passed on purpose and said so here, as its own
/// documentation asks: up to 415 m of UT1 − UTC is a third of a pixel on a whole-Earth map, and the
/// panel has no IERS table to hand. A residual must not be built this way; a picture may.
/// </para>
/// </remarks>
internal static class GroundTrack
{
	/// <summary>
	/// Locates an object at one instant.
	/// </summary>
	/// <param name="elements">The element set the satellite was initialized from.</param>
	/// <param name="satellite">The initialized satellite.</param>
	/// <param name="instantUtc">The instant, on the UTC scale.</param>
	/// <param name="point">Where the object is, when the propagation succeeds.</param>
	/// <returns><see langword="false"/> when the model reports an error, such as decay, at that instant.</returns>
	internal static bool TryLocate(ElementSet elements, Sgp4Satellite<double> satellite, DateTime instantUtc, out TrackPoint point)
	{
		Ensure.NotNull(elements);
		Ensure.NotNull(satellite);

		point = default;

		if (satellite.InitializationError != Sgp4Error.None)
		{
			return false;
		}

		double minutes = (instantUtc - elements.Epoch).TotalMinutes;
		Sgp4Result<double> result = Sgp4<double>.Propagate(satellite, minutes, DoubleStorageMath.Instance);

		if (!result.IsSuccess)
		{
			return false;
		}

		point = ToGround(result.State, instantUtc);
		return true;
	}

	/// <summary>
	/// Drops a TEME state onto the ground.
	/// </summary>
	/// <param name="state">The state SGP4 produced.</param>
	/// <param name="instantUtc">The instant it is for, on the UTC scale.</param>
	/// <returns>The geodetic point under it.</returns>
	internal static TrackPoint ToGround(TemeState<double> state, DateTime instantUtc)
	{
		ItrfState<double> earthFixed = EarthFixedFrame<double>.ToItrf(
			state, JulianDate.FromUtc(instantUtc), EarthOrientation.Ignored, DoubleStorageMath.Instance);
		GeodeticPosition<double> geodetic = Geodetic<double>.FromEarthFixed(earthFixed, DoubleStorageMath.Instance);

		return new TrackPoint(
			instantUtc,
			geodetic.LatitudeRadians * 180.0 / Math.PI,
			geodetic.LongitudeRadians * 180.0 / Math.PI,
			geodetic.AltitudeKilometers);
	}

	/// <summary>
	/// Samples a ground track either side of an instant.
	/// </summary>
	/// <param name="elements">The element set the satellite was initialized from.</param>
	/// <param name="satellite">The initialized satellite.</param>
	/// <param name="centreUtc">The instant the track is centred on.</param>
	/// <param name="before">How far back the track reaches.</param>
	/// <param name="after">How far ahead the track reaches.</param>
	/// <param name="step">The interval between samples.</param>
	/// <returns>
	/// Runs of consecutive successful samples. A sample the model refuses ends a run rather than
	/// being bridged, so a decayed object's track stops where the model stopped rather than drawing
	/// a straight line across whatever it could not compute.
	/// </returns>
	internal static List<List<TrackPoint>> Sample(ElementSet elements, Sgp4Satellite<double> satellite, DateTime centreUtc, TimeSpan before, TimeSpan after, TimeSpan step)
	{
		if (step <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(step), step, "The step must be positive.");
		}

		List<List<TrackPoint>> runs = [];
		List<TrackPoint> run = [];

		for (DateTime instant = centreUtc - before; instant <= centreUtc + after; instant += step)
		{
			if (TryLocate(elements, satellite, instant, out TrackPoint point))
			{
				run.Add(point);
				continue;
			}

			if (run.Count > 0)
			{
				runs.Add(run);
				run = [];
			}
		}

		if (run.Count > 0)
		{
			runs.Add(run);
		}

		return runs;
	}

	/// <summary>
	/// Turns runs of track points into polylines on the image, broken wherever the line would lie.
	/// </summary>
	/// <param name="view">The projection.</param>
	/// <param name="runs">The runs, from <see cref="Sample"/>.</param>
	/// <returns>Polylines in the unit square.</returns>
	/// <remarks>
	/// Two places a line would lie. On an equirectangular map a track crossing the antimeridian
	/// jumps from one edge to the other, and joining the two ends draws a line straight across the
	/// world; any step wider than half the map is that jump, since no orbit moves half the Earth
	/// between samples. On an orthographic globe a track passing behind the Earth must vanish
	/// rather than be drawn through it.
	/// </remarks>
	internal static List<List<Vector2>> Project(MapView view, IEnumerable<List<TrackPoint>> runs)
	{
		Ensure.NotNull(runs);

		List<List<Vector2>> lines = [];

		foreach (List<TrackPoint> run in runs)
		{
			List<Vector2> line = [];

			foreach (TrackPoint point in run)
			{
				bool visible = view.TryProject(point.LatitudeDegrees, point.LongitudeDegrees, out Vector2 at);
				bool wrapped = line.Count > 0 && MathF.Abs(at.X - line[^1].X) > 0.5f;

				if (!visible || wrapped)
				{
					Flush(lines, ref line);
				}

				if (visible)
				{
					line.Add(at);
				}
			}

			Flush(lines, ref line);
		}

		return lines;
	}

	private static void Flush(List<List<Vector2>> lines, ref List<Vector2> line)
	{
		if (line.Count >= 2)
		{
			lines.Add(line);
		}

		line = line.Count == 0 ? line : [];
	}
}
