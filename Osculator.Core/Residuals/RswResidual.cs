// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Residuals;

using System.Numerics;
using ktsu.Osculator.Core.Propagation;
using ktsu.Semantics.Quantities;

/// <summary>
/// The difference between two orbital states, resolved in the reference state's RIC/RSW frame.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <param name="Radial">Radial component of the position difference, in kilometres.</param>
/// <param name="AlongTrack">Along-track component of the position difference, in kilometres.</param>
/// <param name="CrossTrack">Cross-track component of the position difference, in kilometres.</param>
/// <param name="RadialRate">Radial component of the velocity difference, in km/s.</param>
/// <param name="AlongTrackRate">Along-track component of the velocity difference, in km/s.</param>
/// <param name="CrossTrackRate">Cross-track component of the velocity difference, in km/s.</param>
/// <remarks>
/// <para>
/// Kilometres and km/s, because that is what the propagator works in and what every published
/// verification value is quoted in. The typed accessors below convert.
/// </para>
/// <para>
/// <strong>Every component is signed, and the type system will not save you here.</strong>
/// <c>Length</c> and <c>Speed</c> are magnitude (V0) forms, and <c>V0 - V0</c> is defined in
/// <c>ktsu.Semantics.Quantities</c> as <c>T.Abs(a - b)</c> — so subtracting two speeds silently
/// gives the absolute difference, which is not a residual. The accessors return
/// <see cref="Displacement1D{T}"/> and <see cref="Velocity1D{T}"/>, the signed forms, for that
/// reason. <see cref="Magnitude"/> is the one member that legitimately answers a <c>Length</c>.
/// </para>
/// </remarks>
public readonly record struct RswResidual<T>(
	T Radial,
	T AlongTrack,
	T CrossTrack,
	T RadialRate,
	T AlongTrackRate,
	T CrossTrackRate)
	where T : struct, INumber<T>
{
	/// <summary>
	/// Compares a state against a reference, in the reference's own frame.
	/// </summary>
	/// <param name="reference">The state taken as truth. Its frame is the one used.</param>
	/// <param name="test">The state being measured against it.</param>
	/// <param name="math">The storage type's square root.</param>
	/// <returns>The residual.</returns>
	/// <remarks>
	/// The difference is taken in TEME and then rotated, rather than each state being resolved and
	/// the resolved components subtracted. Those are not the same operation unless both states
	/// share a frame, and the whole point is that they do not.
	/// </remarks>
	public static RswResidual<T> Between(TemeState<T> reference, TemeState<T> test, IStorageMath<T> math)
	{
		RswBasis<T> basis = RswBasis<T>.Of(reference, math);

		(T Radial, T AlongTrack, T CrossTrack) position = basis.Resolve(
			test.X - reference.X, test.Y - reference.Y, test.Z - reference.Z);

		(T Radial, T AlongTrack, T CrossTrack) velocity = basis.Resolve(
			test.VelocityX - reference.VelocityX,
			test.VelocityY - reference.VelocityY,
			test.VelocityZ - reference.VelocityZ);

		return new RswResidual<T>(
			position.Radial, position.AlongTrack, position.CrossTrack,
			velocity.Radial, velocity.AlongTrack, velocity.CrossTrack);
	}

	/// <summary>Gets the magnitude of the position difference, in kilometres.</summary>
	/// <param name="math">The storage type's square root.</param>
	/// <returns>The magnitude. Non-negative, so a magnitude form is the honest type.</returns>
	public Length<T> Magnitude(IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		return Length<T>.FromKilometer(
			math.Sqrt((Radial * Radial) + (AlongTrack * AlongTrack) + (CrossTrack * CrossTrack)));
	}

	/// <summary>Gets the radial component as a signed displacement.</summary>
	public Displacement1D<T> RadialDisplacement => Displacement1D<T>.FromKilometer(Radial);

	/// <summary>Gets the along-track component as a signed displacement.</summary>
	public Displacement1D<T> AlongTrackDisplacement => Displacement1D<T>.FromKilometer(AlongTrack);

	/// <summary>Gets the cross-track component as a signed displacement.</summary>
	public Displacement1D<T> CrossTrackDisplacement => Displacement1D<T>.FromKilometer(CrossTrack);

	/// <summary>Gets the radial rate as a signed velocity.</summary>
	/// <remarks>
	/// Converted through metres per second because the library has no kilometre-per-second factory,
	/// which is the one unit astrodynamics is actually written in. Filed upstream rather than
	/// worked around silently; the multiplication is exact in every storage type here.
	/// </remarks>
	public Velocity1D<T> RadialVelocity => PerSecond(RadialRate);

	/// <summary>Gets the along-track rate as a signed velocity.</summary>
	public Velocity1D<T> AlongTrackVelocity => PerSecond(AlongTrackRate);

	/// <summary>Gets the cross-track rate as a signed velocity.</summary>
	public Velocity1D<T> CrossTrackVelocity => PerSecond(CrossTrackRate);

	private static Velocity1D<T> PerSecond(T kilometersPerSecond) =>
		Velocity1D<T>.FromMeterPerSecond(kilometersPerSecond * T.CreateChecked(1000));
}
