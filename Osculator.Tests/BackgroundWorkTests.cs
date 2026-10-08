// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.Tests;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ktsu.Osculator.App.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the shell's work queue against a marshal the test pumps by hand, so the order in which a
/// worker finishes and the UI thread hears about it is under the test's control.
/// </summary>
[TestClass]
public sealed class BackgroundWorkTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	[TestMethod]
	public void Result_IsDeliveredOnlyWhenTheUiThreadRunsIt()
	{
		ManualMarshal ui = new();
		using BackgroundWork work = new(ui.Post);
		int? delivered = null;

		work.Run("sweep", _ => 42, result => delivered = result);
		ui.WaitFor(1);

		Assert.IsNull(delivered, "The result reached the panel before the UI thread ran it.");
		Assert.IsTrue(work.IsRunning("sweep"));

		ui.RunAll();

		Assert.AreEqual(42, delivered);
		Assert.IsFalse(work.IsRunning("sweep"));
		Assert.AreEqual(0, work.Running);
	}

	[TestMethod]
	public void StartingAgainUnderTheSameKey_CancelsTheFirstAndDropsItsResult()
	{
		// The selection-change case: the first sweep is for an object nobody is looking at any more.
		// It finishes regardless, because it ignores its token, and its result must still not land.
		ManualMarshal ui = new();
		using BackgroundWork work = new(ui.Post);
		using ManualResetEventSlim release = new();
		CancellationToken firstToken = default;
		string? delivered = null;

		work.Run(
			"sweep",
			token =>
			{
				firstToken = token;
				release.Wait(Patience, CancellationToken.None);
				return "stale";
			},
			result => delivered = result);
		work.Run("sweep", _ => "current", result => delivered = result);

		release.Set();
		ui.WaitFor(2);
		ui.RunAll();

		Assert.IsTrue(firstToken.IsCancellationRequested, "The superseded job was not told to stop.");
		Assert.AreEqual("current", delivered);
		Assert.AreEqual(0, work.Running);
	}

	[TestMethod]
	public void SupersededResult_IsDroppedEvenWhenItArrivesLast()
	{
		ManualMarshal ui = new();
		using BackgroundWork work = new(ui.Post);
		using ManualResetEventSlim release = new();
		string? delivered = null;

		work.Run(
			"sweep",
			_ =>
			{
				release.Wait(Patience, CancellationToken.None);
				return "stale";
			},
			result => delivered = result);
		work.Run("sweep", _ => "current", result => delivered = result);

		// The current job's result is delivered first; the stale one is posted after it.
		ui.WaitFor(1);
		ui.RunAll();
		release.Set();
		ui.WaitFor(1);
		ui.RunAll();

		Assert.AreEqual("current", delivered);
	}

	[TestMethod]
	public void Cancel_StopsTheJobAndItsResultNeverLands()
	{
		ManualMarshal ui = new();
		using BackgroundWork work = new(ui.Post);
		bool delivered = false;
		bool failed = false;

		work.Run(
			"sweep",
			token =>
			{
				token.WaitHandle.WaitOne(Patience);
				token.ThrowIfCancellationRequested();
				return 1;
			},
			_ => delivered = true,
			_ => failed = true);
		work.Cancel("sweep");

		Assert.IsFalse(work.IsRunning("sweep"), "A cancelled key still reads as running.");

		ui.WaitFor(1);
		ui.RunAll();

		Assert.IsFalse(delivered);
		Assert.IsFalse(failed, "Cancellation was reported as a failure.");
	}

	[TestMethod]
	public void Failure_IsHandedToTheUiThread()
	{
		ManualMarshal ui = new();
		using BackgroundWork work = new(ui.Post);
		Exception? reported = null;

		work.Run<int>("sweep", _ => throw new InvalidOperationException("diverged"), _ => { }, error => reported = error);
		ui.WaitFor(1);

		Assert.IsNull(reported, "The failure was reported from the worker thread.");

		ui.RunAll();

		Assert.IsInstanceOfType<InvalidOperationException>(reported);
		Assert.AreEqual(0, work.Running);
	}

	[TestMethod]
	public void Dispose_CancelsEveryJob()
	{
		ManualMarshal ui = new();
		BackgroundWork work = new(ui.Post);
		CancellationToken token = default;
		using ManualResetEventSlim started = new();

		work.Run(
			"a",
			t =>
			{
				token = t;
				started.Set();
				t.WaitHandle.WaitOne(Patience);
				return 0;
			},
			_ => { });
		Assert.IsTrue(started.Wait(Patience));

		work.Dispose();

		Assert.IsTrue(token.IsCancellationRequested);
		Assert.ThrowsExactly<ObjectDisposedException>(() => work.Run("b", _ => 0, _ => { }));
	}

	/// <summary>
	/// Stands in for the UI thread's invoker: posted actions wait until the test runs them.
	/// </summary>
	private sealed class ManualMarshal
	{
		private readonly ConcurrentQueue<Action> posted = new();

		internal Task Post(Action action)
		{
			posted.Enqueue(action);
			return Task.CompletedTask;
		}

		internal void WaitFor(int count) =>
			Assert.IsTrue(SpinWait.SpinUntil(() => posted.Count >= count, Patience), $"Expected {count} posted action(s), saw {posted.Count}.");

		internal void RunAll()
		{
			while (posted.TryDequeue(out Action? action))
			{
				action();
			}
		}
	}
}
