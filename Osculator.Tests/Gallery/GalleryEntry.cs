// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests.Gallery;

using System;
using System.Text;

/// <summary>One picture in the application gallery: how to stage it, and what to say about it.</summary>
/// <param name="Name">The caption, which also names the picture's file.</param>
/// <param name="Description">One or two sentences under the picture in the gallery's index.</param>
/// <param name="Stage">
/// Drives the application into the state worth photographing, starting from the shell with every
/// panel registered and its first frames drawn. It ends once whatever the picture shows has landed,
/// so nothing still in flight is photographed half drawn.
/// </param>
internal sealed record GalleryEntry(string Name, string Description, Action<GalleryStage> Stage)
{
	/// <summary>Gets the display the picture is drawn at. Every entry fills it, so this is the picture's size.</summary>
	public (int Width, int Height) Display { get; init; } = (1280, 800);

	/// <summary>Gets the file name the picture is written under, without its extension.</summary>
	public string Slug => MakeSlug(Name);

	/// <inheritdoc/>
	public override string ToString() => Name;

	/// <summary>Turns a caption into a lower-case, hyphenated file name.</summary>
	/// <param name="text">The caption.</param>
	/// <returns>The file name, without an extension.</returns>
	internal static string MakeSlug(string text)
	{
		StringBuilder slug = new(text.Length);
		foreach (char character in text)
		{
			if (char.IsAsciiLetterOrDigit(character))
			{
				slug.Append(char.ToLowerInvariant(character));
			}
			else if (slug.Length > 0 && slug[^1] != '-')
			{
				slug.Append('-');
			}
		}

		return slug.ToString().TrimEnd('-');
	}
}
