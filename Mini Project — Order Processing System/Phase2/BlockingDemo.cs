using System.Diagnostics;
using OrderProcessing.Shared.Logging;

namespace OrderProcessing.Phase2;

public static class BlockingDemo
{
    /// <summary>
    /// Demonstrates the dangers of calling .Result or .Wait() on an unfinished task.
    /// The ONLY place in the codebase where .Result / .Wait() is permitted by specification.
    /// </summary>
    public static void RunDemo()
    {
        Log.Info("\n========== DANGEROUS BLOCKING DEMO (.Result / .Wait()) ==========");
        Log.Info("Calling .Result on an unfinished asynchronous task synchronously blocks the executing thread.");

        int callerThreadId = Environment.CurrentManagedThreadId;
        Log.Info($"Caller Thread #{callerThreadId} is about to block waiting for task.Result...");

        var sw = Stopwatch.StartNew();

        var task = Task.Run(async () =>
        {
            int workerThreadId = Environment.CurrentManagedThreadId;
            Log.Info($"Worker Thread #{workerThreadId} inside task is sleeping for 300ms...");
            await Task.Delay(300).ConfigureAwait(false);
            return 42;
        });

        // SYNCHRONOUS BLOCKING: Stalls caller thread and wraps exceptions in AggregateException
        int result = task.Result;
        sw.Stop();

        Log.Info($"Caller Thread #{callerThreadId} resumed after {sw.ElapsedMilliseconds} ms of blocking! Result = {result}");
        Log.Success("Danger summary: Thread wasted while idle; risk of deadlocks with SynchronizationContext; unhandled exceptions wrapped in AggregateException.");
    }
}
