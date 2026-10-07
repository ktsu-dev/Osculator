// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Globe;

using System;
using System.Numerics;

/// <summary>
/// How the Earth is laid onto the flat image.
/// </summary>
internal enum MapProjection
{
	/// <summary>Longitude and latitude as plain x and y: the whole world, and the ground-track view.</summary>
	Equirectangular,

	/// <summary>The Earth as seen from far away: one hemisphere, and the globe view.</summary>
	Orthographic,
}

/// <summary>
/// A projection and the point it is centred on, mapping latitude and longitude to the unit square.
/// </summary>
/// <param name="Projection">The projection.</param>
/// <param name="CentreLatitudeDegrees">
/// The latitude at the centre of the view. Only the orthographic projection reads it; an
/// equirectangular map always runs pole to pole.
/// </param>
/// <param name="CentreLongitudeDegrees">The longitude at the centre of the view.</param>
/// <remarks>
/// <para>
/// Coordinates are normalised, <c>(0, 0)</c> at the image's top-left and <c>(1, 1)</c> at its
/// bottom-right, so the same view serves the rasterizer, which works in texels, and the overlay,
/// which works in screen pixels at whatever zoom the canvas is at. Nothing here knows how big
/// either is.
/// </para>
/// <para>
/// Display geometry only: the angles are <see langword="double"/> and nothing measured in this
/// repository passes through here. A latitude arrives already geodetic, from
/// <c>Geodetic&lt;double&gt;</c>, and is drawn on a sphere, which is the usual and harmless
/// liberty a map takes — the 0.19° that matters for a residual is a pixel and a half on a
/// thousand-pixel globe.
/// </para>
/// </remarks>
internal readonly record struct MapView(MapProjection Projection, double CentreLatitudeDegrees, double CentreLongitudeDegrees)
{
	/// <summary>
	/// The share of the image the orthographic disc's diameter fills, leaving a margin for the limb.
	/// </summary>
	internal const double DiscFill = 0.96;

	private const double DegreesToRadians = Math.PI / 180.0;

	/// <summary>Gets the width the image should have per unit of height.</summary>
	internal double AspectRatio => Projection == MapProjection.Equirectangular ? 2.0 : 1.0;

	/// <summary>
	/// Projects a point onto the image.
	/// </summary>
	/// <param name="latitudeDegrees">Latitude, in degrees.</param>
	/// <param name="longitudeDegrees">Longitude, in degrees east.</param>
	/// <param name="normalized">Where the point lands, in the unit square.</param>
	/// <returns><see langword="false"/> when the point is on the far side of an orthographic globe.</returns>
	internal bool TryProject(double latitudeDegrees, double longitudeDegrees, out Vector2 normalized)
	{
		double deltaLongitude = WrapDegrees(longitudeDegrees - CentreLongitudeDegrees);

		if (Projection == MapProjection.Equirectangular)
		{
			normalized = new Vector2(
				(float)((deltaLongitude / 360.0) + 0.5),
				(float)((90.0 - latitudeDegrees) / 180.0));
			return true;
		}

		double latitude = latitudeDegrees * DegreesToRadians;
		double centreLatitude = CentreLatitudeDegrees * DegreesToRadians;
		double lambda = deltaLongitude * DegreesToRadians;

		double cosC = (Math.Sin(centreLatitude) * Math.Sin(latitude)) + (Math.Cos(centreLatitude) * Math.Cos(latitude) * Math.Cos(lambda));
		double x = Math.Cos(latitude) * Math.Sin(lambda);
		double y = (Math.Cos(centreLatitude) * Math.Sin(latitude)) - (Math.Sin(centreLatitude) * Math.Cos(latitude) * Math.Cos(lambda));

		normalized = new Vector2(
			(float)(0.5 + (x * 0.5 * DiscFill)),
			(float)(0.5 - (y * 0.5 * DiscFill)));
		return cosC >= 0.0;
	}

	/// <summary>
	/// Finds the point on the Earth that a place on the image shows.
	/// </summary>
	/// <param name="normalized">The place on the image, in the unit square.</param>
	/// <param name="latitudeDegrees">The latitude shown there, in degrees.</param>
	/// <param name="longitudeDegrees">The longitude shown there, in degrees east, in [−180, 180).</param>
	/// <returns><see langword="false"/> when the place is off the map, outside the orthographic disc.</returns>
	internal bool TryUnproject(Vector2 normalized, out double latitudeDegrees, out double longitudeDegrees)
	{
		if (Projection == MapProjection.Equirectangular)
		{
			latitudeDegrees = 90.0 - (normalized.Y * 180.0);
			longitudeDegrees = WrapDegrees(CentreLongitudeDegrees + ((normalized.X - 0.5) * 360.0));
			return normalized.Y is >= 0f and <= 1f;
		}

		double x = (normalized.X - 0.5) * 2.0 / DiscFill;
		double y = (0.5 - normalized.Y) * 2.0 / DiscFill;
		double rho = Math.Sqrt((x * x) + (y * y));

		// A point exactly on the limb projects to ρ = 1, and the single-precision image coordinate can
		// round that a hair outside. Half a millionth is far below a texel on any image this draws.
		if (rho > 1.0 + 5e-7)
		{
			latitudeDegrees = 0.0;
			longitudeDegrees = 0.0;
			return false;
		}

		rho = Math.Min(rho, 1.0);

		double centreLatitude = CentreLatitudeDegrees * DegreesToRadians;

		if (rho == 0.0)
		{
			latitudeDegrees = CentreLatitudeDegrees;
			longitudeDegrees = WrapDegrees(CentreLongitudeDegrees);
			return true;
		}

		double c = Math.Asin(rho);
		double sinC = Math.Sin(c);
		double cosC = Math.Cos(c);

		latitudeDegrees = Math.Asin((cosC * Math.Sin(centreLatitude)) + (y * sinC * Math.Cos(centreLatitude) / rho)) / DegreesToRadians;
		longitudeDegrees = WrapDegrees(CentreLongitudeDegrees + (Math.Atan2(
			x * sinC,
			(rho * cosC * Math.Cos(centreLatitude)) - (y * sinC * Math.Sin(centreLatitude))) / DegreesToRadians));
		return true;
	}

	/// <summary>
	/// Brings an angle into [−180, 180).
	/// </summary>
	/// <param name="degrees">The angle, in degrees.</param>
	/// <returns>The same direction, in [−180, 180).</returns>
	internal static double WrapDegrees(double degrees) =>
		degrees - (360.0 * Math.Floor((degrees + 180.0) / 360.0));
}
