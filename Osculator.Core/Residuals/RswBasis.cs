// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Residuals;

using System.Numerics;
using ktsu.Osculator.Core.Propagation;

/// <summary>
/// The radial / along-track / cross-track orthonormal frame carried by one orbital state.
/// </summary>
/// <typeparam name="T">The numeric storage type.</typeparam>
/// <remarks>
/// <para>
/// A residual expressed in TEME components says almost nothing: all three numbers change as the
/// satellite goes round, and a position error that is entirely a timing error looks different at
/// every point in the orbit. Rotated into this frame the same error is one number — along-track —
/// and stays that number, which is why every operational orbit comparison is quoted in it.
/// </para>
/// <para>
/// <strong>RSW and RIC are the same frame under two names.</strong> R is radial in both. S
/// ("along-track") and I ("in-track") are the same axis, and W and C ("cross-track") are the same
/// axis. This type uses the RSW letters for the fields and the RIC words in the documentation,
/// because that is how the literature is split: Vallado writes RSW, operational products say RIC.
/// </para>
/// <para>
/// <strong>S is not the velocity direction.</strong> It is what completes the right-handed set from
/// R and W, which means it is the velocity direction only where the orbit is circular. On an
/// eccentric orbit the velocity has a radial component and S is perpendicular to R by construction,
/// so the two differ by the flight path angle — a degree or two at moderate eccentricity, and
/// tens of degrees for a Molniya at perigee. Calling S "the velocity direction" is a defect that
/// hides until a deep-space case is compared.
/// </para>
/// <para>
/// The three axes are <em>directions</em>, and a direction is dimensionless, so they are held as
/// bare <typeparamref name="T"/> components rather than as <c>Displacement3D</c>. The library's
/// <c>Normalize()</c> keeps the length type, so a normalized displacement claims to be a
/// one-metre displacement; storing the basis that way would add a dimensional claim that is false
/// rather than catch one that is wrong. The residual itself is a genuine length and is typed —
/// see <see cref="RswResidual{T}"/>.
/// </para>
/// </remarks>
public readonly record struct RswBasis<T>
	where T : struct, INumber<T>
{
	/// <summary>Gets the radial direction: outward along the position vector.</summary>
	public (T X, T Y, T Z) Radial { get; private init; }

	/// <summary>
	/// Gets the along-track direction, completing the right-handed set. In-track in RIC.
	/// </summary>
	public (T X, T Y, T Z) AlongTrack { get; private init; }

	/// <summary>
	/// Gets the cross-track direction: the orbit normal, along the specific angular momentum.
	/// </summary>
	public (T X, T Y, T Z) CrossTrack { get; private init; }

	/// <summary>
	/// Builds the frame carried by a state.
	/// </summary>
	/// <param name="state">The state whose frame this is. The reference, in a comparison.</param>
	/// <param name="math">The storage type's square root.</param>
	/// <returns>The orthonormal frame.</returns>
	/// <exception cref="ArgumentException">
	/// The state has no position, or its position and velocity are parallel, so no frame exists.
	/// </exception>
	/// <remarks>
	/// The frame belongs to the <em>reference</em> state in a comparison, not to the state under
	/// test and not to some average of the two. Two orbits being compared have two frames, and
	/// which one the residual is resolved in changes the answer at second order; naming the
	/// reference as the owner is what makes the number reproducible.
	/// </remarks>
	public static RswBasis<T> Of(TemeState<T> state, IStorageMath<T> math)
	{
		Ensure.NotNull(math);

		(T X, T Y, T Z) radial = Normalized(
			(state.X, state.Y, state.Z), math, "The state has no position, so it carries no radial direction.");

		// h = r x v. The cross-track axis is the orbit normal, and it is undefined for a state whose
		// position and velocity are parallel — a purely radial trajectory, which is not an orbit.
		(T X, T Y, T Z) momentum = Cross(
			(state.X, state.Y, state.Z),
			(state.VelocityX, state.VelocityY, state.VelocityZ));

		(T X, T Y, T Z) crossTrack = Normalized(
			momentum, math, "The state's position and velocity are parallel, so it carries no orbit normal.");

		return new RswBasis<T>
		{
			Radial = radial,
			CrossTrack = crossTrack,

			// W x R, in that order. The reverse is its negation, and nothing in the dimensions
			// catches a sign, so the order is pinned by a test rather than by the type system.
			AlongTrack = Cross(crossTrack, radial),
		};
	}

	/// <summary>
	/// Resolves a TEME vector into this frame.
	/// </summary>
	/// <param name="x">The vector's TEME x component.</param>
	/// <param name="y">The vector's TEME y component.</param>
	/// <param name="z">The vector's TEME z component.</param>
	/// <returns>The radial, along-track and cross-track components.</returns>
	public (T Radial, T AlongTrack, T CrossTrack) Resolve(T x, T y, T z) =>
		(Dot(Radial, (x, y, z)), Dot(AlongTrack, (x, y, z)), Dot(CrossTrack, (x, y, z)));

	private static (T X, T Y, T Z) Cross((T X, T Y, T Z) a, (T X, T Y, T Z) b) =>
		((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

	private static T Dot((T X, T Y, T Z) a, (T X, T Y, T Z) b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

	private static (T X, T Y, T Z) Normalized((T X, T Y, T Z) v, IStorageMath<T> math, string whenZero)
	{
		T length = math.Sqrt(Dot(v, v));

		return length == T.Zero
			? throw new ArgumentException(whenZero)
			: ((T X, T Y, T Z))(v.X / length, v.Y / length, v.Z / length);
	}
}
