// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Data.Cddis;

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Reads the Earthdata Login token from the operating system's own credential store.
/// </summary>
/// <remarks>
/// <para>
/// The token is never read from a file, and there is deliberately no way to make this class do so:
/// the repository's rule is that CDDIS credentials live in the OS credential store and nowhere else,
/// and a configurable fallback path is how a token ends up committed. Each platform keeps it under
/// the service name <see cref="ServiceName"/>:
/// </para>
/// <list type="table">
/// <listheader><term>Platform</term><description>How to store the token</description></listheader>
/// <item>
/// <term>Windows</term>
/// <description>Credential Manager, a generic credential:
/// <c>cmdkey /generic:osculator-earthdata /user:token /pass</c> (it prompts for the token).</description>
/// </item>
/// <item>
/// <term>macOS</term>
/// <description>The login keychain:
/// <c>security add-generic-password -s osculator-earthdata -a token -w</c> (it prompts).</description>
/// </item>
/// <item>
/// <term>Linux</term>
/// <description>The Secret Service, through libsecret:
/// <c>secret-tool store --label="Osculator Earthdata token" service osculator-earthdata</c> (it
/// prompts).</description>
/// </item>
/// </list>
/// <para>
/// All three commands prompt rather than take the token as an argument, so it never reaches shell
/// history. Reading is the mirror image: Windows through <c>CredReadW</c>, the other two through
/// <c>security</c> and <c>secret-tool</c>, whose output is captured and never echoed.
/// </para>
/// <para>
/// ktsu.CredentialCache does the same job and was considered first. It targets .NET 9 and 10 only,
/// and this project also targets .NET 8, so it cannot be referenced here without dropping a
/// framework the rest of the solution supports.
/// </para>
/// </remarks>
public sealed class OsCredentialStore : IEarthdataTokenSource
{
	/// <summary>The name the token is stored under on every platform.</summary>
	public const string ServiceName = "osculator-earthdata";

	private readonly Func<string, string[], CommandResult> run;
	private readonly OSPlatform platform;

	/// <summary>Initializes a new instance of the <see cref="OsCredentialStore"/> class for this machine.</summary>
	public OsCredentialStore()
		: this(Current(), Run)
	{
	}

	/// <summary>Initializes a new instance of the <see cref="OsCredentialStore"/> class with a stand-in for the platform's tools.</summary>
	/// <param name="platform">The platform whose store to read.</param>
	/// <param name="run">Runs a command and captures its output.</param>
	internal OsCredentialStore(OSPlatform platform, Func<string, string[], CommandResult> run)
	{
		this.platform = platform;
		this.run = run;
	}

	/// <inheritdoc/>
	/// <exception cref="InvalidOperationException">
	/// The platform has no supported credential store, or its command-line tool is not installed.
	/// </exception>
	public EarthdataToken? GetToken()
	{
		string? secret = platform == OSPlatform.Windows
			? ReadWindowsCredential()
			: platform == OSPlatform.OSX
				? ReadFromCommand("security", ["find-generic-password", "-s", ServiceName, "-w"])
				: platform == OSPlatform.Linux
					? ReadFromCommand("secret-tool", ["lookup", "service", ServiceName])
					: throw new InvalidOperationException("This platform has no credential store Osculator knows how to read.");

		return string.IsNullOrWhiteSpace(secret) ? null : new EarthdataToken(secret);
	}

	private static OSPlatform Current() =>
		RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatform.Windows
		: RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatform.OSX
		: RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? OSPlatform.Linux
		: OSPlatform.FreeBSD;

	private string? ReadFromCommand(string tool, string[] arguments)
	{
		CommandResult result;

		try
		{
			result = run(tool, arguments);
		}
		catch (Win32Exception missing)
		{
			throw new InvalidOperationException(
				$"Could not run '{tool}' to read the Earthdata token from the credential store. Is it installed?",
				missing);
		}

		// Both tools exit non-zero for "no such entry", which is the ordinary case of a machine that
		// has never been given a token. The output is not inspected further: it is the secret.
		return result.ExitCode == 0 ? result.Output : null;
	}

	private static CommandResult Run(string tool, string[] arguments)
	{
		ProcessStartInfo start = new(tool)
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

		using Process process = Process.Start(start) ?? throw new Win32Exception($"{tool} did not start.");

		// Standard error is drained so a chatty tool cannot block on a full pipe, and then dropped.
		System.Threading.Tasks.Task<string> error = process.StandardError.ReadToEndAsync();
		string output = process.StandardOutput.ReadToEnd();
		process.WaitForExit();
		_ = error.GetAwaiter().GetResult();

		return new CommandResult(process.ExitCode, output);
	}

	private string? ReadWindowsCredential()
	{
		if (platform != OSPlatform.Windows || !OperatingSystem.IsWindows())
		{
			throw new InvalidOperationException("The Windows credential store can only be read on Windows.");
		}

		if (!NativeMethods.CredRead(ServiceName, NativeMethods.GenericCredential, 0, out IntPtr handle))
		{
			// ERROR_NOT_FOUND is the ordinary "never stored" case; anything else is too, as far as
			// the caller can act on it, and the error code says nothing about the secret.
			return null;
		}

		try
		{
			NativeMethods.Credential credential = Marshal.PtrToStructure<NativeMethods.Credential>(handle);

			if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
			{
				return null;
			}

			byte[] blob = new byte[credential.CredentialBlobSize];
			Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);

			// cmdkey and the Credential Manager UI both write the password as UTF-16.
			return Encoding.Unicode.GetString(blob);
		}
		finally
		{
			NativeMethods.CredFree(handle);
		}
	}

	/// <summary>What a credential-store command printed, and how it exited.</summary>
	/// <param name="ExitCode">The exit code.</param>
	/// <param name="Output">Standard output.</param>
	internal readonly record struct CommandResult(int ExitCode, string Output)
	{
		/// <summary>Describes the result without the output, which is the secret.</summary>
		/// <returns>The exit code only.</returns>
		public override string ToString() => $"CommandResult {{ ExitCode = {ExitCode} }}";
	}

	private static class NativeMethods
	{
		public const int GenericCredential = 1;

		[System.Diagnostics.CodeAnalysis.SuppressMessage(
			"Interoperability", "SYSLIB1054:Use LibraryImportAttribute instead of DllImportAttribute",
			Justification = "LibraryImport needs AllowUnsafeBlocks for the whole project, for one Windows-only call.")]
		[DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credential);

		[System.Diagnostics.CodeAnalysis.SuppressMessage(
			"Interoperability", "SYSLIB1054:Use LibraryImportAttribute instead of DllImportAttribute",
			Justification = "LibraryImport needs AllowUnsafeBlocks for the whole project, for one Windows-only call.")]
		[DllImport("advapi32.dll", EntryPoint = "CredFree")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		public static extern void CredFree(IntPtr buffer);

		[StructLayout(LayoutKind.Sequential)]
		public struct Credential
		{
			public int Flags;
			public int Type;
			public IntPtr TargetName;
			public IntPtr Comment;
			public long LastWritten;
			public int CredentialBlobSize;
			public IntPtr CredentialBlob;
			public int Persist;
			public int AttributeCount;
			public IntPtr Attributes;
			public IntPtr TargetAlias;
			public IntPtr UserName;
		}
	}
}
