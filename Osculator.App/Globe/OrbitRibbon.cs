// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Globe;

using System;
using System.Collections.Generic;
using System.Numerics;
using ktsu.Osculator.Core.Elements;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// The orbit itself, as opposed to the ground track: one revolution drawn where it lies in space
/// at a single instant, so it closes on itself instead of walking west.
/// </summary>
/// <remarks>
/// <para>
/// A ground track drops each sample onto the ground the Earth had turned to at <em>that sample's</em>
/// instant, so successive revolutions fall about 23.6° apart for the ISS. The ribbon propagates the
/// same revolution and rotates every sample with the Earth's orientation at <em>one</em> instant,
/// the one the panel is showing. What is left is the orbit as an observer above the Earth would see
/// it: a closed loop, opening only by the third of a degree J2 turns the node in a revolution.
/// </para>
/// <para>
/// In <see langword="double"/>, with <see cref="Core.Frames.EarthOrientation.Ignored"/>, through
/// <see cref="GroundTrack.ToGround"/>, for the reasons that type gives: this is a picture, never a
/// residual.
/// </para>
/// </remarks>
internal static class OrbitRibbon
{
	/// <summary>The samples drawn over one revolution, whatever its period.</summary>
	internal const int SamplesPerRevolution = 180;

	/// <summary>
	/// The mean equatorial radius the ribbon's height is measured against on the globe, in kilometres:
	/// WGS-84's, the ellipsoid <see cref="TrackPoint.AltitudeKilometers"/> is a height above.
	/// </summary>
	internal const double EarthRadiusKilometers = 6378.137;

	/// <summary>
	/// The period of one revolution, from the element set's mean motion.
	/// </summary>
	/// <param name="elements">The element set.</param>
	/// <returns>The time one revolution takes.</returns>
	internal static TimeSpan Period(ElementSet elements)
	{
		Ensure.NotNull(elements);

		if (double.IsNaN(elements.MeanMotion) || elements.MeanMotion <= 0.0)
		{
			throw new ArgumentOutOfRangeException(nameof(elements), elements.MeanMotion, "The mean motion must be positive.");
		}

		return TimeSpan.FromDays(1.0 / elements.MeanMotion);
	}

	/// <summary>
	/// Samples one revolution starting at an instant, every sample placed with the Earth as it is at
	/// that instant.
	/// </summary>
	/// <param name="elements">The element set the satellite was initialized from.</param>
	/// <param name="satellite">The initialized satellite.</param>
	/// <param name="instantUtc">The instant the ribbon is drawn for, and the start of the revolution.</param>
	/// <param name="samples">How many intervals the revolution is cut into; the last sample is one period on.</param>
	/// <returns>
	/// The samples, <paramref name="samples"/> + 1 of them when the model accepts every one, or fewer
	/// when it refuses some. Each point's <see cref="TrackPoint.InstantUtc"/> is the instant the
	/// object is there; its latitude and longitude are under that place on the Earth of
	/// <paramref name="instantUtc"/>. Empty when the satellite could not be initialized.
	/// </returns>
	internal static List<TrackPoint> Sample(ElementSet elements, Sgp4Satellite<double> satellite, DateTime instantUtc, int samples = SamplesPerRevolution)
	{
		Ensure.NotNull(elements);
		Ensure.NotNull(satellite);

		if (samples < 2)
		{
			throw new ArgumentOutOfRangeException(nameof(samples), samples, "A revolution needs at least two intervals.");
		}

		List<TrackPoint> points = [];

		if (satellite.InitializationError != Sgp4Error.None)
		{
			return points;
		}

		TimeSpan period = Period(elements);

		for (int i = 0; i <= samples; i++)
		{
			DateTime instant = instantUtc + (period * i / samples);
			Sgp4Result<double> result = Sgp4<double>.Propagate(satellite, (instant - elements.Epoch).TotalMinutes, DoubleStorageMath.Instance);

			if (result.IsSuccess)
			{
				points.Add(GroundTrack.ToGround(result.State, instantUtc) with { InstantUtc = instant });
			}
		}

		return points;
	}

	/// <summary>
	/// Turns a ribbon into polylines on the image, at its height above the Earth.
	/// </summary>
	/// <param name="view">The projection.</param>
	/// <param name="ribbon">The samples, from <see cref="Sample"/>.</param>
	/// <returns>Polylines in the unit square.</returns>
	/// <remarks>
	/// On the globe the ribbon is drawn at its altitude rather than on the surface, so it stands off
	/// the limb and is hidden only where the Earth is in front of it, not wherever it is on the far
	/// side. On the map it is the same line as on the ground, broken where it crosses the
	/// antimeridian, which is where a map cannot show height.
	/// </remarks>
	internal static List<List<Vector2>> Project(MapView view, IReadOnlyList<TrackPoint> ribbon)
	{
		Ensure.NotNull(ribbon);

		return GroundTrack.Project(
			[[.. ribbon]],
			point => view.TryProject(point.LatitudeDegrees, point.LongitudeDegrees, RadiusRatio(point), out Vector2 at) ? at : null);
	}

	/// <summary>
	/// Gets how far the point is from the Earth's centre, in Earth radii.
	/// </summary>
	/// <param name="point">The point.</param>
	/// <returns>The distance, where the surface is 1.</returns>
	internal static double RadiusRatio(TrackPoint point) =>
		(EarthRadiusKilometers + point.AltitudeKilometers) / EarthRadiusKilometers;

	/// <summary>
	/// Places a point in an Earth-fixed frame, treating the Earth as a sphere, for measuring the
	/// ribbon rather than drawing it.
	/// </summary>
	/// <param name="point">The point.</param>
	/// <returns>Its position, in kilometres.</returns>
	internal static Vector3 ToCartesian(TrackPoint point)
	{
		double radius = EarthRadiusKilometers + point.AltitudeKilometers;
		double latitude = point.LatitudeDegrees * Math.PI / 180.0;
		double longitude = point.LongitudeDegrees * Math.PI / 180.0;

		return new Vector3(
			(float)(radius * Math.Cos(latitude) * Math.Cos(longitude)),
			(float)(radius * Math.Cos(latitude) * Math.Sin(longitude)),
			(float)(radius * Math.Sin(latitude)));
	}
}
