using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace MeshWave.TestUtilities;

public static class ViewModelTestHelpers
{
    public static async Task WaitForItemAsync<T>(this ObservableCollection<T> collection, Func<T, bool> predicate, int timeoutMs = 5000)
    {
        if (collection.Any(predicate)) return;

        var tcs = new TaskCompletionSource<bool>();
        NotifyCollectionChangedEventHandler handler = (s, e) =>
        {
            if (collection.Any(predicate))
            {
                tcs.TrySetResult(true);
            }
        };

        collection.CollectionChanged += handler;
        try
        {
            var delayTask = Task.Delay(timeoutMs);
            var completedTask = await Task.WhenAny(tcs.Task, delayTask);
            if (completedTask == delayTask)
            {
                throw new TimeoutException($"Timed out waiting for item in collection.");
            }
        }
        finally
        {
            collection.CollectionChanged -= handler;
        }
    }

    public static async Task WaitForConditionAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("Condition not met within timeout.");
    }
}
