// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.SpaceTrack;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Reads the Space-Track password from the operating system's own credential store.
/// </summary>
/// <remarks>
/// <para>
/// The password is never read from or written to a file, in the repository or anywhere else: it
/// lives in Windows Credential Manager, the macOS login keychain, or the freedesktop Secret Service
/// (GNOME Keyring, KWallet) on Linux. The identity is not a secret and is supplied by the caller.
/// </para>
/// <para>
/// This only reads. Storing the password is done once, by hand, with the platform's own tool, each
/// of which prompts for it rather than taking it as an argument — so it never appears in a shell
/// history or a process list either:
/// </para>
/// <list type="table">
/// <item><term>Windows</term><description><c>cmdkey /generic:ktsu.Osculator.SpaceTrack:you@example.com /user:you@example.com /pass</c></description></item>
/// <item><term>macOS</term><description><c>security add-generic-password -s ktsu.Osculator.SpaceTrack -a you@example.com -w</c></description></item>
/// <item><term>Linux</term><description><c>secret-tool store --label="Osculator Space-Track" service ktsu.Osculator.SpaceTrack account you@example.com</c></description></item>
/// </list>
/// </remarks>
public sealed class OsCredentialStore : ISpaceTrackCredentialSource
{
	/// <summary>The service name every platform's entry is filed under.</summary>
	public const string ServiceName = "ktsu.Osculator.SpaceTrack";

	private readonly CommandRunner run;

	private readonly OSPlatform platform;

	/// <summary>Creates a store reading the password for one Space-Track account.</summary>
	/// <param name="identity">The account's login.</param>
	/// <exception cref="ArgumentNullException"><paramref name="identity"/> is null.</exception>
	/// <exception cref="PlatformNotSupportedException">The OS is not Windows, macOS or Linux.</exception>
	public OsCredentialStore(string identity)
		: this(identity, CurrentPlatform(), RunProcessAsync)
	{
	}

	/// <summary>Creates a store over a given platform and command runner, for tests.</summary>
	/// <param name="identity">The account's login.</param>
	/// <param name="platform">Which platform's store to read.</param>
	/// <param name="run">Runs a command and returns its exit code and standard output.</param>
	internal OsCredentialStore(string identity, OSPlatform platform, CommandRunner run)
	{
		Ensure.NotNull(identity);
		Ensure.NotNull(run);

		Identity = identity;
		this.platform = platform;
		this.run = run;
	}

	/// <summary>Runs a command and returns its exit code and standard output.</summary>
	/// <param name="fileName">The program.</param>
	/// <param name="arguments">Its arguments, each passed as one argument with no shell between.</param>
	/// <param name="cancellationToken">Cancels the run.</param>
	/// <returns>The exit code and everything written to standard output.</returns>
	internal delegate Task<(int ExitCode, string Output)> CommandRunner(
		string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);

	/// <summary>Gets the account's login.</summary>
	public string Identity { get; }

	/// <summary>Gets the name a Windows generic credential is filed under for this account.</summary>
	public string WindowsTargetName => $"{ServiceName}:{Identity}";

	/// <inheritdoc/>
	/// <exception cref="SpaceTrackException">The platform's credential tool is not installed.</exception>
	public async Task<SpaceTrackCredentials?> GetAsync(CancellationToken cancellationToken)
	{
		string? password = platform == OSPlatform.Windows
			? WindowsCredentials.ReadPassword(WindowsTargetName)
			: await ReadWithToolAsync(cancellationToken).ConfigureAwait(false);

		return password is null ? null : new SpaceTrackCredentials(Identity, password);
	}

	/// <summary>Gets the command that reads the password on macOS or Linux.</summary>
	/// <returns>The program and its arguments.</returns>
	internal (string FileName, IReadOnlyList<string> Arguments) LookupCommand() => platform == OSPlatform.OSX
		? ("security", ["find-generic-password", "-s", ServiceName, "-a", Identity, "-w"])
		: ("secret-tool", ["lookup", "service", ServiceName, "account", Identity]);

	/// <summary>
	/// Removes the one line ending a credential tool appends, and nothing else.
	/// </summary>
	/// <param name="output">The tool's standard output.</param>
	/// <returns>The password.</returns>
	/// <remarks>
	/// Not <see cref="string.Trim()"/>: a password may legitimately begin or end with a space, and
	/// trimming it would log in with a different password from the one stored.
	/// </remarks>
	internal static string StripLineEnding(string output) =>
		output.EndsWith("\r\n", StringComparison.Ordinal) ? output[..^2]
		: output.EndsWith('\n') ? output[..^1]
		: output;

	private static OSPlatform CurrentPlatform() =>
		OperatingSystem.IsWindows() ? OSPlatform.Windows
		: OperatingSystem.IsMacOS() ? OSPlatform.OSX
		: OperatingSystem.IsLinux() ? OSPlatform.Linux
		: throw new PlatformNotSupportedException("Space-Track credentials are read from Windows, macOS or Linux credential stores only.");

	private async Task<string?> ReadWithToolAsync(CancellationToken cancellationToken)
	{
		(string fileName, IReadOnlyList<string> arguments) = LookupCommand();

		(int exitCode, string output) = await run(fileName, arguments, cancellationToken).ConfigureAwait(false);

		// Both tools answer a missing entry with a non-zero exit and nothing on standard output.
		if (exitCode != 0 || output.Length == 0)
		{
			return null;
		}

		return StripLineEnding(output);
	}

	private static async Task<(int ExitCode, string Output)> RunProcessAsync(
		string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
	{
		ProcessStartInfo start = new(fileName)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};

		foreach (string argument in arguments)
		{
			start.ArgumentList.Add(argument);
		}

		try
		{
			using Process process = Process.Start(start)
				?? throw new SpaceTrackException(FormattableString.Invariant($"{fileName} could not be started."));

			Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
			Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);

			await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
			_ = await error.ConfigureAwait(false);

			return (process.ExitCode, await output.ConfigureAwait(false));
		}
		catch (Win32Exception missing)
		{
			throw new SpaceTrackException(
				FormattableString.Invariant($"{fileName} is not installed, so the Space-Track password cannot be read from the credential store."),
				missing);
		}
	}

	/// <summary>Windows Credential Manager, through <c>advapi32</c>.</summary>
	private static class WindowsCredentials
	{
		private const int GenericCredential = 1;

		private const int ErrorNotFound = 1168;

		/// <summary>Reads a generic credential's secret.</summary>
		/// <param name="target">The credential's target name.</param>
		/// <returns>The secret, or null when there is no such credential.</returns>
		public static string? ReadPassword(string target)
		{
			if (!NativeMethods.CredReadW(target, GenericCredential, 0, out IntPtr handle))
			{
				int error = Marshal.GetLastPInvokeError();

				return error == ErrorNotFound
					? null
					: throw new SpaceTrackException(
						FormattableString.Invariant($"Windows Credential Manager refused to read {target} (error {error})."));
			}

			try
			{
				NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(handle);

				// cmdkey stores the password as UTF-16 without a terminator.
				return credential.CredentialBlob == IntPtr.Zero
					? string.Empty
					: Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / sizeof(char));
			}
			finally
			{
				NativeMethods.CredFree(handle);
			}
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct NativeCredential
		{
			public int Flags;
			public int Type;
			public IntPtr TargetName;
			public IntPtr Comment;
			public long LastWritten;
			public uint CredentialBlobSize;
			public IntPtr CredentialBlob;
			public int Persist;
			public int AttributeCount;
			public IntPtr Attributes;
			public IntPtr TargetAlias;
			public IntPtr UserName;
		}

		private static class NativeMethods
		{
			[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
			[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
			[return: MarshalAs(UnmanagedType.Bool)]
			[System.Diagnostics.CodeAnalysis.SuppressMessage(
				"Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time",
				Justification = "LibraryImport needs AllowUnsafeBlocks project-wide for one read-only call on one platform.")]
			public static extern bool CredReadW(string target, int type, int flags, out IntPtr credential);

			[DllImport("advapi32.dll")]
			[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
			[System.Diagnostics.CodeAnalysis.SuppressMessage(
				"Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time",
				Justification = "LibraryImport needs AllowUnsafeBlocks project-wide for one read-only call on one platform.")]
			public static extern void CredFree(IntPtr credential);
		}
	}
}
