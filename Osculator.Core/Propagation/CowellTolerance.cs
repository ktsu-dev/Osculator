// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Propagation;

/// <summary>
/// The local error tolerance and step limits for an adaptive <see cref="Cowell{T}"/> integration.
/// </summary>
/// <remarks>
/// <para>
/// A component's step error is acceptable when it is no more than
/// <c>absolute + relative · |value|</c>. Position and velocity get separate absolute floors because
/// they are in different units and differ in magnitude by three orders.
/// </para>
/// <para>
/// Held in <see langword="double"/> in every storage type: these steer the controller, which runs in
/// <see langword="double"/> for the reason given on <see cref="Cowell{T}"/>.
/// </para>
/// </remarks>
public sealed record CowellTolerance
{
	/// <summary>Gets the default tolerance, about a micrometre per step at low Earth orbit.</summary>
	public static CowellTolerance Default { get; } = new();

	/// <summary>Gets the relative tolerance per component.</summary>
	public double Relative { get; init; } = 1e-13;

	/// <summary>Gets the absolute tolerance on a position component, in kilometres.</summary>
	public double AbsolutePositionKm { get; init; } = 1e-9;

	/// <summary>Gets the absolute tolerance on a velocity component, in kilometres per second.</summary>
	public double AbsoluteVelocityKmPerSecond { get; init; } = 1e-12;

	/// <summary>Gets the first step tried, in seconds.</summary>
	public double InitialStepSeconds { get; init; } = 60.0;

	/// <summary>Gets the step below which the integration is abandoned, in seconds.</summary>
	public double MinimumStepSeconds { get; init; } = 1e-3;

	/// <summary>Gets the largest step allowed, in seconds.</summary>
	public double MaximumStepSeconds { get; init; } = 3600.0;

	/// <summary>Gets the number of steps, kept or rejected, after which the integration is abandoned.</summary>
	public int MaximumSteps { get; init; } = 1_000_000;
}
