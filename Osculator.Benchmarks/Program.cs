// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Benchmarks;

using BenchmarkDotNet.Running;

/// <summary>
/// The benchmark entry point.
/// </summary>
internal static class Program
{
	/// <summary>
	/// Runs the benchmark switcher over every benchmark in the assembly, or the M3 budget gate when
	/// asked for it.
	/// </summary>
	/// <param name="args">
	/// <c>--budget</c> alone to run <see cref="PropagationBudget"/>; anything else is forwarded to
	/// BenchmarkDotNet, such as <c>--filter</c>.
	/// </param>
	/// <returns>The process exit code: non-zero when the budget gate fails.</returns>
	private static int Main(string[] args)
	{
		if (args is [PropagationBudget.Switch])
		{
			return PropagationBudget.Run();
		}

		BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
		return 0;
	}
}
