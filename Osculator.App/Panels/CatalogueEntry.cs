// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Panels;

using System.Globalization;
using ktsu.Osculator.Core.Elements;

/// <summary>
/// One row of the catalogue: an element set, the orbit it describes, and the text search ranks it by.
/// </summary>
/// <remarks>
/// Everything a row displays or is filtered on is computed once, when the catalogue loads, so that
/// drawing a row is formatting and nothing else.
/// </remarks>
internal sealed class CatalogueEntry
{
	/// <summary>
	/// Initializes a new instance of the <see cref="CatalogueEntry"/> class.
	/// </summary>
	/// <param name="elements">The element set.</param>
	internal CatalogueEntry(ElementSet elements)
	{
		Elements = elements;
		Geometry = OrbitGeometry.Of(elements);
		NoradText = elements.NoradCatalogId.ToString(CultureInfo.InvariantCulture);

		// The catalogue number leads, so typing one ranks its object first. It also makes every key
		// distinct, which names alone are not: hundreds of fragments share one debris name.
		SearchKey = $"{NoradText} {elements.ObjectName} {elements.ObjectId}";
	}

	/// <summary>Gets the element set.</summary>
	internal ElementSet Elements { get; }

	/// <summary>Gets the orbit the element set describes.</summary>
	internal OrbitGeometry Geometry { get; }

	/// <summary>Gets the catalogue number as text, formatted once rather than per frame.</summary>
	internal string NoradText { get; }

	/// <summary>Gets the text the search box ranks this row by: catalogue number, name and designator.</summary>
	internal string SearchKey { get; }
}
