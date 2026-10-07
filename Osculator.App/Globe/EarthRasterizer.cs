// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Globe;

using System;
using System.Numerics;
using System.Threading.Tasks;

/// <summary>
/// Paints the Earth into an RGBA buffer on the CPU: land, sea, and the night side.
/// </summary>
/// <remarks>
/// <para>
/// Inverse mapping, one texel at a time: each texel asks the view which latitude and longitude it
/// shows and colours itself from the land mask and the Sun. That way round there are no gaps and no
/// overdraw whatever the projection does to area, which a forward rasterization of the coastlines
/// cannot promise on an orthographic globe.
/// </para>
/// <para>
/// Only the Earth itself is rasterized. Ground tracks, the graticule and the objects are drawn over
/// it as vectors, because they move every frame and must stay one pixel wide at any zoom, while the
/// Earth changes only when the view or the terminator moves. That split is what lets the texture be
/// re-uploaded rarely rather than every frame.
/// </para>
/// </remarks>
internal static class EarthRasterizer
{
	/// <summary>The colour of open sea.</summary>
	internal static readonly (byte R, byte G, byte B) Sea = (22, 48, 86);

	/// <summary>The colour of land.</summary>
	internal static readonly (byte R, byte G, byte B) Land = (74, 112, 66);

	/// <summary>The colour of the image beyond the orthographic disc.</summary>
	internal static readonly (byte R, byte G, byte B) Space = (10, 12, 18);

	/// <summary>How bright the night side is, against the day side's one.</summary>
	internal const float NightBrightness = 0.35f;

	/// <summary>
	/// The cosine of the zenith angle at which full night begins: the Sun six degrees below the
	/// horizon, which is the end of civil twilight.
	/// </summary>
	private const double NightCosine = -0.104528;

	/// <summary>
	/// Paints one image.
	/// </summary>
	/// <param name="view">The projection and centre.</param>
	/// <param name="width">Image width, in texels.</param>
	/// <param name="height">Image height, in texels.</param>
	/// <param name="mask">The land mask.</param>
	/// <param name="subsolar">Where the Sun is overhead, or <see langword="null"/> to paint everything as day.</param>
	/// <returns>The image, tightly packed RGBA8, top row first.</returns>
	internal static byte[] Render(MapView view, int width, int height, LandMask mask, (double LatitudeDegrees, double LongitudeDegrees)? subsolar)
	{
		Ensure.NotNull(mask);
		ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
		ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

		byte[] pixels = new byte[width * height * 4];

		Parallel.For(0, height, row =>
		{
			for (int column = 0; column < width; column++)
			{
				Vector2 normalized = new((column + 0.5f) / width, (row + 0.5f) / height);
				(byte r, byte g, byte b) = Shade(view, normalized, mask, subsolar);

				int offset = ((row * width) + column) * 4;
				pixels[offset] = r;
				pixels[offset + 1] = g;
				pixels[offset + 2] = b;
				pixels[offset + 3] = 255;
			}
		});

		return pixels;
	}

	/// <summary>
	/// The colour of one place on the image.
	/// </summary>
	/// <param name="view">The projection and centre.</param>
	/// <param name="normalized">The place, in the unit square.</param>
	/// <param name="mask">The land mask.</param>
	/// <param name="subsolar">Where the Sun is overhead, if shading.</param>
	/// <returns>The colour.</returns>
	internal static (byte R, byte G, byte B) Shade(MapView view, Vector2 normalized, LandMask mask, (double LatitudeDegrees, double LongitudeDegrees)? subsolar)
	{
		if (!view.TryUnproject(normalized, out double latitude, out double longitude))
		{
			return Space;
		}

		(byte r, byte g, byte b) = mask.IsLand(latitude, longitude) ? Land : Sea;

		if (subsolar is null)
		{
			return (r, g, b);
		}

		float light = Daylight(SubsolarPoint.CosineOfZenith(latitude, longitude, subsolar.Value));
		return ((byte)(r * light), (byte)(g * light), (byte)(b * light));
	}

	/// <summary>
	/// How lit a place is, from the cosine of the Sun's zenith angle there.
	/// </summary>
	/// <param name="cosineOfZenith">The cosine; zero on the terminator.</param>
	/// <returns><see cref="NightBrightness"/> at night, one by day, and a smooth ramp through twilight.</returns>
	internal static float Daylight(double cosineOfZenith)
	{
		double t = Math.Clamp((cosineOfZenith - NightCosine) / -NightCosine, 0.0, 1.0);
		double smooth = t * t * (3.0 - (2.0 * t));
		return (float)(NightBrightness + ((1.0 - NightBrightness) * smooth));
	}
}
