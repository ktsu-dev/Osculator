// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Benchmarks;

using BenchmarkDotNet.Running;

/// <summary>
/// The benchmark entry point.
/// </summary>
internal static class Program
{
	/// <summary>
	/// Runs the benchmark switcher over every benchmark in the assembly.
	/// </summary>
	/// <param name="args">Arguments forwarded to BenchmarkDotNet, such as <c>--filter</c>.</param>
	private static void Main(string[] args) =>
		BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
