using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace MeshWave.TestUtilities;

public static class ViewModelTestHelpers
{
    public static async Task WaitForItemAsync<T>(this ObservableCollection<T> collection, Func<T, bool> predicate, int timeoutMs = 15000)
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

    /// <summary>
    /// Waits for an item matching the predicate to appear in a collection.
    /// Re-evaluates the collection provider frequently to handle cases where the collection object itself is replaced.
    /// </summary>
    public static async Task WaitForItemPollingAsync<T>(Func<IEnumerable<T>> collectionProvider, Func<T, bool> predicate, int timeoutMs = 20000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var items = collectionProvider();
            if (items != null && items.Any(predicate)) return;
            await Task.Delay(200);
        }
        throw new TimeoutException($"Timed out waiting for item in collection.");
    }
}
