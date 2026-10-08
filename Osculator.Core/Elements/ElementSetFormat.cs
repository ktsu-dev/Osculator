// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Elements;

/// <summary>
/// The representation an element set was read from, which fixes how finely each field is written.
/// </summary>
/// <remarks>
/// The two are not interchangeable containers for the same digits. The OMM JSON is generated from
/// the originating values rather than by re-reading the text, so eccentricity comes through one
/// digit finer and the drag term three. <see cref="ElementFieldQuantization.For(ElementSetFormat)"/>
/// gives each format's steps.
/// </remarks>
public enum ElementSetFormat
{
	/// <summary>The fixed-column two-line element format.</summary>
	Tle,

	/// <summary>CCSDS Orbit Mean-Elements Message, as CelesTrak distributes it in JSON.</summary>
	Omm,
}
