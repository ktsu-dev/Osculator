// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Globe;

using System;

/// <summary>
/// Maps a residual magnitude to a colour, on a logarithmic scale.
/// </summary>
/// <remarks>
/// <para>
/// Logarithmic because the residuals this repository cares about span fourteen orders of
/// magnitude — <see langword="decimal"/> against <see langword="double"/> sits near 1e-10 km and
/// <see langword="float"/> reaches 55 km — and on a linear scale everything but the worst
/// <see langword="float"/> case would be one colour.
/// </para>
/// <para>
/// The ramp is viridis, sampled at five stops: perceptually ordered, so a brighter dot is
/// unambiguously a larger residual, and readable by the commonest forms of colour blindness,
/// which a red-to-green ramp is not.
/// </para>
/// </remarks>
internal static class ResidualColorScale
{
	/// <summary>The smallest residual the scale distinguishes, as a power of ten in kilometres: a nanometre.</summary>
	internal const double MinimumLog10Kilometers = -12.0;

	/// <summary>The largest residual the scale distinguishes, as a power of ten in kilometres: a hundred kilometres.</summary>
	internal const double MaximumLog10Kilometers = 2.0;

	/// <summary>The colour of an object whose residual could not be computed.</summary>
	internal static readonly (byte R, byte G, byte B) Unavailable = (140, 140, 140);

	private static readonly (byte R, byte G, byte B)[] Stops =
	[
		(68, 1, 84),
		(59, 82, 139),
		(33, 145, 140),
		(94, 201, 98),
		(253, 231, 37),
	];

	/// <summary>
	/// Where a residual falls on the scale.
	/// </summary>
	/// <param name="kilometers">The residual, in kilometres.</param>
	/// <returns>A position in [0, 1]; zero and anything below the minimum clamp to 0.</returns>
	internal static double Position(double kilometers)
	{
		if (!(kilometers > 0.0))
		{
			return 0.0;
		}

		double t = (Math.Log10(kilometers) - MinimumLog10Kilometers) / (MaximumLog10Kilometers - MinimumLog10Kilometers);
		return Math.Clamp(t, 0.0, 1.0);
	}

	/// <summary>
	/// The colour for a residual.
	/// </summary>
	/// <param name="kilometers">The residual, in kilometres, or <see langword="null"/> when there is none.</param>
	/// <returns>The colour.</returns>
	internal static (byte R, byte G, byte B) ColorFor(double? kilometers) =>
		kilometers is double value ? Sample(Position(value)) : Unavailable;

	/// <summary>
	/// The ramp's colour at a position.
	/// </summary>
	/// <param name="position">The position, in [0, 1].</param>
	/// <returns>The colour, interpolated between the two nearest stops.</returns>
	internal static (byte R, byte G, byte B) Sample(double position)
	{
		double scaled = Math.Clamp(position, 0.0, 1.0) * (Stops.Length - 1);
		int lower = Math.Min((int)scaled, Stops.Length - 2);
		double t = scaled - lower;

		(byte r0, byte g0, byte b0) = Stops[lower];
		(byte r1, byte g1, byte b1) = Stops[lower + 1];

		return (Lerp(r0, r1, t), Lerp(g0, g1, t), Lerp(b0, b1, t));
	}

	private static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + ((b - a) * t));
}
