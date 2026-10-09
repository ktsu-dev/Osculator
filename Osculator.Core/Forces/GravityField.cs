// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Forces;

using System;
using System.Globalization;
using System.IO;

/// <summary>
/// A spherical-harmonic model of a body's gravity field: its fully normalized Stokes coefficients,
/// and the gravitational parameter and reference radius they are scaled to.
/// </summary>
/// <remarks>
/// <para>
/// The coefficients are held as the text they were published in, not as <see langword="double"/>.
/// A model's coefficients are its data, and the same rule that has <see cref="Propagation.Wgs72{T}"/>
/// parse its constants per storage type applies here: a <see langword="decimal"/> or 30-digit run
/// should carry the published twelve digits exactly, not the binary expansion a <see langword="double"/>
/// rounded them to. <see cref="SphericalHarmonicGravity{T}"/> parses each one once, in its own
/// storage type.
/// </para>
/// <para>
/// Normalization is the geodetic "4π" convention without the Condon–Shortley phase, which is what
/// EGM96, EGM2008 and the ICGEM archive publish.
/// </para>
/// </remarks>
public sealed class GravityField
{
	private readonly string[] cosineTerms;
	private readonly string[] sineTerms;

	private GravityField(string name, string gravitationalParameter, string referenceRadius, int maximumDegree, string[] cosine, string[] sine)
	{
		Name = name;
		GravitationalParameter = gravitationalParameter;
		ReferenceRadius = referenceRadius;
		MaximumDegree = maximumDegree;
		cosineTerms = cosine;
		sineTerms = sine;
	}

	/// <summary>Gets the model's name, such as <c>EGM96</c>.</summary>
	public string Name { get; }

	/// <summary>Gets μ, in cubic kilometres per second squared, as the literal the model states.</summary>
	public string GravitationalParameter { get; }

	/// <summary>Gets the reference radius, in kilometres, as the literal the model states.</summary>
	public string ReferenceRadius { get; }

	/// <summary>Gets the highest degree the model holds.</summary>
	public int MaximumDegree { get; }

	/// <summary>
	/// Reads a model from text with one coefficient pair per line: degree, order, C̄, S̄, and
	/// optionally anything after (the published files carry the two standard deviations there).
	/// </summary>
	/// <param name="reader">The text. Blank lines and lines starting with <c>#</c> are skipped.</param>
	/// <param name="name">The model's name.</param>
	/// <param name="gravitationalParameter">μ in km³/s², as a literal.</param>
	/// <param name="referenceRadius">The reference radius in km, as a literal.</param>
	/// <param name="maximumDegree">
	/// The highest degree to keep; lines above it are skipped. Pass the file's own maximum to keep
	/// everything.
	/// </param>
	/// <returns>The model. A pair the text does not mention is zero.</returns>
	/// <exception cref="ArgumentNullException">An argument is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumDegree"/> is below 2.</exception>
	/// <exception cref="FormatException">A line is not a degree, an order and two numbers, or names an order above its degree.</exception>
	/// <remarks>
	/// The NGA's <c>EGM96</c> distribution file is in this layout, so the full degree-360 model reads
	/// with this method directly; only the first seventy degrees are bundled, see <see cref="Egm96"/>.
	/// </remarks>
	public static GravityField Parse(TextReader reader, string name, string gravitationalParameter, string referenceRadius, int maximumDegree)
	{
		Ensure.NotNull(reader);
		Ensure.NotNull(name);
		Ensure.NotNull(gravitationalParameter);
		Ensure.NotNull(referenceRadius);
		if (maximumDegree < 2)
		{
			throw new ArgumentOutOfRangeException(nameof(maximumDegree), "A gravity field needs at least degree 2.");
		}

		int count = Index(maximumDegree, maximumDegree) + 1;
		string[] cosine = new string[count];
		string[] sine = new string[count];

		int lineNumber = 0;
		string? line;
		while ((line = reader.ReadLine()) is not null)
		{
			lineNumber++;
			string trimmed = line.Trim();
			if (trimmed.Length == 0 || trimmed[0] == '#')
			{
				continue;
			}

			string[] fields = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			if (fields.Length < 4
				|| !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int degree)
				|| !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int order)
				|| order < 0 || degree < order
				|| !IsNumber(fields[2]) || !IsNumber(fields[3]))
			{
				throw new FormatException($"Line {lineNumber} is not a degree, an order and two coefficients: \"{trimmed}\".");
			}

			if (degree > maximumDegree)
			{
				continue;
			}

			// The published files use FORTRAN's D exponent; every .NET parser wants E.
			int index = Index(degree, order);
			cosine[index] = Normalize(fields[2]);
			sine[index] = Normalize(fields[3]);
		}

		return new GravityField(name, gravitationalParameter, referenceRadius, maximumDegree, cosine, sine);
	}

	/// <summary>Gets C̄ₙₘ as published, or <c>0</c> where the model has none.</summary>
	/// <param name="degree">n.</param>
	/// <param name="order">m, at most n.</param>
	/// <returns>The coefficient, as a literal.</returns>
	public string Cosine(int degree, int order) => cosineTerms[CheckedIndex(degree, order)] ?? "0";

	/// <summary>Gets S̄ₙₘ as published, or <c>0</c> where the model has none.</summary>
	/// <param name="degree">n.</param>
	/// <param name="order">m, at most n.</param>
	/// <returns>The coefficient, as a literal.</returns>
	public string Sine(int degree, int order) => sineTerms[CheckedIndex(degree, order)] ?? "0";

	internal static int Index(int degree, int order) => (degree * (degree + 1) / 2) + order;

	private int CheckedIndex(int degree, int order)
	{
		if (degree < 0 || degree > MaximumDegree || order < 0 || order > degree)
		{
			throw new ArgumentOutOfRangeException(nameof(degree), $"No coefficient ({degree}, {order}) in a model of degree {MaximumDegree}.");
		}

		return Index(degree, order);
	}

	private static string Normalize(string field) => field.Replace('D', 'E').Replace('d', 'E');

	private static bool IsNumber(string field) =>
		double.TryParse(Normalize(field), NumberStyles.Float, CultureInfo.InvariantCulture, out _);
}
