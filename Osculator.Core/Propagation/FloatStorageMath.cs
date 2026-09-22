// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

using System;

/// <summary>
/// <see cref="IStorageMath{T}"/> over <see langword="float"/>, computed by <see cref="MathF"/>.
/// </summary>
/// <remarks>
/// Single precision is not a storage type anybody should track a satellite in, and it is here for
/// exactly that reason: the point of running the model over four types is to find where each one
/// stops being able to carry the answer, and this is the one that stops soonest. Seven significant
/// digits cannot express a metre at geostationary radius.
/// </remarks>
public sealed class FloatStorageMath : IStorageMath<float>
{
	/// <summary>Gets the shared instance.</summary>
	public static FloatStorageMath Instance { get; } = new();

	/// <inheritdoc />
	public float Pi => MathF.PI;

	/// <inheritdoc />
	public float Sqrt(float value) => MathF.Sqrt(value);

	/// <inheritdoc />
	public float Sin(float radians) => MathF.Sin(radians);

	/// <inheritdoc />
	public float Cos(float radians) => MathF.Cos(radians);

	/// <inheritdoc />
	public float Atan2(float y, float x) => MathF.Atan2(y, x);

	/// <inheritdoc />
	public float Pow(float value, float exponent) => MathF.Pow(value, exponent);
}
