using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Windows.UI.Core;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// The window thread's queue under the names the code used with UWP's CoreDispatcher: under WinUI 3 an element's
    /// Dispatcher is always null, and DispatcherQueue takes its place. Each task ends when the work has run on the queue
    /// (faulted when it threw); work the queue refuses (its window gone) ends the task as cancelled - as RunAsync did with a
    /// closed window's dispatcher, it is not run at all.
    /// </summary>
    public static class CoreDispatcherExtensions
    {
        private static DispatcherQueuePriority Map(CoreDispatcherPriority priority) => priority switch
        {
            CoreDispatcherPriority.High => DispatcherQueuePriority.High,
            CoreDispatcherPriority.Normal => DispatcherQueuePriority.Normal,
            _ => DispatcherQueuePriority.Low,
        };

        private static Task<T> Enqueue<T>(DispatcherQueue queue, DispatcherQueuePriority priority, Func<T> work)
        {
            var task = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (queue == null || !queue.TryEnqueue(priority, () =>
            {
                try { task.SetResult(work()); }
                catch (Exception ex) { task.SetException(ex); }
            }))
                task.TrySetCanceled();
            return task.Task;
        }

        public static Task RunAsync(this DispatcherQueue queue, CoreDispatcherPriority priority, DispatchedHandler handler) =>
            Enqueue<object>(queue, Map(priority), () => { handler(); return null; });

        /// <summary>False when the queue refused the work (its window gone), as CoreDispatcher.TryRunAsync answered.</summary>
        public static async Task<bool> TryRunAsync(this DispatcherQueue queue, CoreDispatcherPriority priority, DispatchedHandler handler)
        {
            try
            {
                await Enqueue<object>(queue, Map(priority), () => { handler(); return null; });
                return true;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        }

        /// <summary>When the window has nothing more pressing: the queue's low priority (WinUI 3 has no idle one).</summary>
        public static Task RunIdleAsync(this DispatcherQueue queue, IdleDispatchedHandler handler) =>
            Enqueue<object>(queue, DispatcherQueuePriority.Low, () => { handler(null); return null; });

        public static Task<T> RunAsync<T>(this DispatcherQueue queue, Func<T> action, CoreDispatcherPriority priority = CoreDispatcherPriority.Normal) =>
            Enqueue(queue, Map(priority), action);

        public static Task RunAsync(this DispatcherQueue queue, Action action, CoreDispatcherPriority priority = CoreDispatcherPriority.Normal) =>
            Enqueue<object>(queue, Map(priority), () => { action(); return null; });

        public static Task<T> RunIdleAsync<T>(this DispatcherQueue queue, Func<T> action) =>
            Enqueue(queue, DispatcherQueuePriority.Low, action);

        public static Task RunIdleAsync(this DispatcherQueue queue, Action action) =>
            Enqueue<object>(queue, DispatcherQueuePriority.Low, () => { action(); return null; });

        /// <summary>
        /// DispatcherQueue.TryEnqueue with the work's exception caught and logged. One that escapes a queued callback is no
        /// unhandled exception the app hears of: WinUI fails the process fast (0xc000027b, DispatcherQueue::
        /// DeferInvokeCallback - the close crash's dump, 2026-11-03: a window closed, another left, a callback queued for
        /// it threw). The log names the work that threw.
        /// </summary>
        public static bool TryEnqueueSafe(this DispatcherQueue queue, DispatcherQueueHandler work) =>
            queue != null && queue.TryEnqueue(Guard(work));

        public static bool TryEnqueueSafe(this DispatcherQueue queue, DispatcherQueuePriority priority, DispatcherQueueHandler work) =>
            queue != null && queue.TryEnqueue(priority, Guard(work));

        /// <summary>The same for the system's queue (Windows.System.DispatcherQueue, the file tree's items use it).</summary>
        public static bool TryEnqueueSafe(this Windows.System.DispatcherQueue queue, Windows.System.DispatcherQueueHandler work) =>
            queue != null && queue.TryEnqueue(() =>
            {
                try { work(); }
                catch (Exception ex) { Log.WriteLocal("DispatcherQueueCallback", $"{work.Method.DeclaringType?.Name}.{work.Method.Name}: {ex}"); }
            });

        /// <summary>
        /// A DispatcherQueueTimer's Tick with the handler's exception caught and logged: one that escapes a timer's callback
        /// fails the process fast as one out of queued work does (0xc000027b, DispatcherQueueTimer::TimerCallback - hp job 91,
        /// 2026-11-03: a window's timer firing after the window closed). Add-only; a handler that is taken off and put on
        /// again keeps its method-group subscription and catches in its body (<see cref="TimerFailed"/>).
        /// </summary>
        public static void TickSafe(this DispatcherQueueTimer timer, global::Windows.Foundation.TypedEventHandler<DispatcherQueueTimer, object> work) =>
            timer.Tick += (t, a) =>
            {
                try { work(t, a); }
                catch (Exception ex) { TimerFailed(work.Method, ex); }
            };

        /// <summary>A timer handler's exception, logged as queued work's is (the E2E driver names each new one).</summary>
        public static void TimerFailed(System.Reflection.MethodBase handler, Exception ex) =>
            Log.WriteLocal("DispatcherQueueCallback", $"{handler?.DeclaringType?.Name}.{handler?.Name} (timer): {ex}");

        private static DispatcherQueueHandler Guard(DispatcherQueueHandler work) => () =>
        {
            try { work(); }
            catch (Exception ex) { Log.WriteLocal("DispatcherQueueCallback", $"{work.Method.DeclaringType?.Name}.{work.Method.Name}: {ex}"); }
        };
    }
}
