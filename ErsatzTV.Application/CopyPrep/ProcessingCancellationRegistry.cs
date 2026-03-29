using System.Collections.Concurrent;

namespace ErsatzTV.Application.CopyPrep;

public static class ProcessingCancellationRegistry
{
    private static readonly ConcurrentDictionary<int, CancellationTokenSource> ProcessingCancellations = new();

    public static IDisposable Register(int queueItemId, CancellationTokenSource cancellationTokenSource)
    {
        ProcessingCancellations.AddOrUpdate(
            queueItemId,
            cancellationTokenSource,
            (_, existing) =>
            {
                existing.Cancel();
                existing.Dispose();
                return cancellationTokenSource;
            });

        return new Registration(queueItemId, cancellationTokenSource);
    }

    public static bool Signal(int queueItemId)
    {
        if (!ProcessingCancellations.TryGetValue(queueItemId, out CancellationTokenSource cancellationTokenSource))
        {
            return false;
        }

        cancellationTokenSource.Cancel();
        return true;
    }

    private sealed class Registration(int queueItemId, CancellationTokenSource cancellationTokenSource) : IDisposable
    {
        public void Dispose()
        {
            if (ProcessingCancellations.TryGetValue(queueItemId, out CancellationTokenSource existing) &&
                ReferenceEquals(existing, cancellationTokenSource))
            {
                ProcessingCancellations.TryRemove(queueItemId, out _);
            }
        }
    }
}
