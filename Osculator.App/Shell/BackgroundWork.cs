// Copyright (c) 2026 ktsu-dev contributors

namespace ktsu.Osculator.App.Shell;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ktsu.ImGui.App;

/// <summary>
/// Runs long work off the UI thread and hands its result back on it.
/// </summary>
/// <remarks>
/// <para>
/// Every job has a key, and a key holds at most one job: starting another under the same key cancels
/// the one before it. That is what a selection change wants: the sweep for the object that is no
/// longer selected stops, and its result never reaches the panel even if it finishes anyway, because
/// a result is only delivered while its job is still the current one for its key.
/// </para>
/// <para>
/// Results, failures and the bookkeeping that decides between them all run through the marshal
/// delegate, which in the application is <see cref="ImGuiApp.Invoker"/>. So <see cref="Run{T}"/>,
/// the callbacks and every read of the queue's state happen on the UI thread, and none of it needs
/// a lock.
/// </para>
/// </remarks>
/// <param name="marshal">Queues an action to run on the UI thread.</param>
internal sealed class BackgroundWork(Func<Action, Task> marshal) : IDisposable
{
	private readonly Dictionary<string, Job> jobs = [];
	private bool disposed;

	/// <summary>
	/// Gets the number of jobs that have been started and not yet delivered, failed or cancelled.
	/// </summary>
	internal int Running => jobs.Count;

	/// <summary>
	/// Creates the queue the application uses, which marshals through <see cref="ImGuiApp.Invoker"/>.
	/// </summary>
	/// <returns>The queue.</returns>
	/// <remarks>
	/// The invoker is read when a job finishes rather than here, because it is only assigned once
	/// the application starts.
	/// </remarks>
	internal static BackgroundWork ForApplication() => new(OnUiThread);

	/// <summary>
	/// Gets a value indicating whether a job is running under a key.
	/// </summary>
	/// <param name="key">The key.</param>
	/// <returns><see langword="true"/> if a job under the key has not yet finished.</returns>
	internal bool IsRunning(string key) => jobs.ContainsKey(key);

	/// <summary>
	/// Starts a job on a worker thread, cancelling any job already running under the same key.
	/// </summary>
	/// <typeparam name="T">The type of the result.</typeparam>
	/// <param name="key">Identifies the job, so that a later one replaces it.</param>
	/// <param name="work">The work. Runs on a worker thread and should observe the token.</param>
	/// <param name="onResult">Receives the result on the UI thread.</param>
	/// <param name="onError">
	/// Receives a failure on the UI thread. When absent, failures are traced. Cancellation is not a
	/// failure and is reported to neither.
	/// </param>
	internal void Run<T>(string key, Func<CancellationToken, T> work, Action<T> onResult, Action<Exception>? onError = null)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentException.ThrowIfNullOrEmpty(key);
		Ensure.NotNull(work);
		Ensure.NotNull(onResult);

		Cancel(key);

		Job job = new();
		jobs[key] = job;
		CancellationToken token = job.Cancellation.Token;

		_ = Task.Run(() =>
		{
			T result;
			try
			{
				result = work(token);
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				return marshal(() => _ = Retire(key, job));
			}
#pragma warning disable CA1031 // Any failure belongs to the job, and is handed to the UI thread rather than lost on a worker.
			catch (Exception error)
#pragma warning restore CA1031
			{
				return marshal(() =>
				{
					if (Retire(key, job))
					{
						Report(error, onError);
					}
				});
			}

			return marshal(() =>
			{
				if (Retire(key, job))
				{
					onResult(result);
				}
			});
		}, CancellationToken.None);
	}

	/// <summary>
	/// Cancels the job under a key, if there is one. Its result will not be delivered.
	/// </summary>
	/// <param name="key">The key.</param>
	internal void Cancel(string key)
	{
		// Not disposed here: the worker may still be reading the token. The job's completion, which
		// always runs, disposes it.
		if (jobs.Remove(key, out Job? job))
		{
			job.Cancellation.Cancel();
		}
	}

	/// <summary>
	/// Cancels every running job.
	/// </summary>
	internal void CancelAll()
	{
		foreach (string key in new List<string>(jobs.Keys))
		{
			Cancel(key);
		}
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		if (!disposed)
		{
			CancelAll();
			disposed = true;
		}
	}

	/// <summary>
	/// Queues an action on <see cref="ImGuiApp.Invoker"/>, read at the time of the call.
	/// </summary>
	private static Task OnUiThread(Action action) => ImGuiApp.Invoker.InvokeAsync(action);

	private static void Report(Exception error, Action<Exception>? onError)
	{
		if (onError is null)
		{
			Trace.TraceError($"Background work failed: {error}");
		}
		else
		{
			onError(error);
		}
	}

	/// <summary>
	/// Releases a finished job, and removes it from the table if it is still the current one for its
	/// key.
	/// </summary>
	/// <returns><see langword="true"/> if the job was current, and so its outcome should be delivered.</returns>
	private bool Retire(string key, Job job)
	{
		bool current = jobs.TryGetValue(key, out Job? latest) && ReferenceEquals(latest, job);
		if (current)
		{
			jobs.Remove(key);
		}

		job.Cancellation.Dispose();
		return current;
	}

	private sealed class Job
	{
		internal CancellationTokenSource Cancellation { get; } = new();
	}
}
