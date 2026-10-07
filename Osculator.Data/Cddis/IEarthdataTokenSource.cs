// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Cddis;

/// <summary>
/// Somewhere an Earthdata Login token can be read from.
/// </summary>
/// <remarks>
/// The client asks for the token only when it has to go to the network, so a machine with no token
/// at all can still read everything it fetched earlier. The one implementation that ships is
/// <see cref="OsCredentialStore"/>; the seam exists so the client can be tested without a keyring.
/// </remarks>
public interface IEarthdataTokenSource
{
	/// <summary>Reads the token.</summary>
	/// <returns>The token, or <see langword="null"/> when none has been stored.</returns>
	public EarthdataToken? GetToken();
}
