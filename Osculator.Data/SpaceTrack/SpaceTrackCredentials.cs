// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.SpaceTrack;

/// <summary>
/// A Space-Track account: the identity it logs in with and its password.
/// </summary>
/// <remarks>
/// <see cref="ToString"/> is overridden so the password cannot reach a log, an exception message
/// or a debugger's display string by accident. A record's generated <c>ToString</c> prints every
/// member, which for this type would be the one thing it must never do.
/// </remarks>
/// <param name="Identity">The account's login, usually an e-mail address.</param>
/// <param name="Password">The account's password.</param>
public sealed record SpaceTrackCredentials(string Identity, string Password)
{
	/// <summary>Describes the account without its password.</summary>
	/// <returns>The identity alone.</returns>
	public override string ToString() => $"SpaceTrackCredentials {{ Identity = {Identity} }}";
}
