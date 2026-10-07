// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Core.Storage;

using ktsu.Osculator.Core.Elements;

/// <summary>
/// A storage facade that can propagate, held without naming its storage type.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam every panel calls through. The application displays four storage types side by
/// side and so cannot be generic over any one of them, and the alias packages bind one storage type
/// per project, so each facade closes the generic propagator in its own project and hands back a
/// state with the type erased. The application then holds a list of four of these and loops.
/// </para>
/// <para>
/// It extends <see cref="IStorageProfile"/> because a panel that shows a propagation also shows
/// what the type could represent, and holding the two through separate lists would let them fall
/// out of order.
/// </para>
/// </remarks>
public interface IPropagatorHost : IStorageProfile
{
	/// <summary>
	/// Propagates an element set to a time after its epoch, in this facade's storage type.
	/// </summary>
	/// <param name="elements">The element set.</param>
	/// <param name="minutesSinceEpoch">Minutes since the element set's epoch; may be negative.</param>
	/// <returns>
	/// The TEME state, rounded to <see langword="double"/> for display, with the wall-clock time
	/// the propagation took. A failed propagation is reported through
	/// <see cref="PropagatedState.Error"/> rather than thrown, as SGP4 itself reports it.
	/// </returns>
	/// <remarks>
	/// The element set is initialized once per instance and the result kept for as long as the
	/// element set is reachable, so sweeping one object across many times pays initialization once.
	/// <paramref name="minutesSinceEpoch"/> is converted into the storage type exactly where the type
	/// can hold it, so the time argument does not add a rounding of its own.
	/// </remarks>
	public PropagatedState Propagate(ElementSet elements, double minutesSinceEpoch);
}
