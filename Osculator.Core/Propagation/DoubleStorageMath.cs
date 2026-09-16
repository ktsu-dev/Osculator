// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System;

/// <summary>
/// <see cref="IStorageMath{T}"/> over <see langword="double"/>, computed by <see cref="Math"/>.
/// </summary>
public sealed class DoubleStorageMath : IStorageMath<double>
{
	/// <summary>Gets the shared instance.</summary>
	public static DoubleStorageMath Instance { get; } = new();

	/// <inheritdoc />
	public double Pi => Math.PI;

	/// <inheritdoc />
	public double Sqrt(double value) => Math.Sqrt(value);

	/// <inheritdoc />
	public double Sin(double radians) => Math.Sin(radians);

	/// <inheritdoc />
	public double Cos(double radians) => Math.Cos(radians);

	/// <inheritdoc />
	public double Atan2(double y, double x) => Math.Atan2(y, x);

	/// <inheritdoc />
	public double Pow(double value, double exponent) => Math.Pow(value, exponent);
}
