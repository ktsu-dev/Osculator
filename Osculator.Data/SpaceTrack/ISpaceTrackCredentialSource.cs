// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.SpaceTrack;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Supplies the Space-Track account the client logs in with.
/// </summary>
/// <remarks>
/// The client asks only when it is about to log in, which is only when the cache could not answer,
/// so an application working from its cache never reads the credential store at all.
/// <see cref="OsCredentialStore"/> is the implementation an application uses; tests supply their
/// own.
/// </remarks>
public interface ISpaceTrackCredentialSource
{
	/// <summary>Reads the account.</summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>The account, or <see langword="null"/> when none has been stored.</returns>
	public Task<SpaceTrackCredentials?> GetAsync(CancellationToken cancellationToken);
}
