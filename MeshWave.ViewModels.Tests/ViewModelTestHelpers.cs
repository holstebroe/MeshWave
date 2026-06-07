namespace MeshWave.ViewModels.Tests;

public static class ViewModelTestHelpers
{
    /// <summary>
    /// Waits for an item matching the predicate to appear in a collection.
    /// Re-evaluates the collection provider frequently to handle cases where the collection object itself is replaced.
    /// </summary>
    public static async Task WaitForItemPollingAsync<T>(Func<IEnumerable<T>> collectionProvider, Func<T, bool> predicate, int timeoutMs = 45000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var currentCollection = collectionProvider();
                if (currentCollection != null)
                {
                    // Use a snapshot to avoid concurrent modification exceptions during enumeration
                    var snapshot = currentCollection.ToList();
                    if (snapshot.Any(predicate)) return;
                }
            }
            catch (Exception)
            {
                // Ignore collection modification or other transient errors during polling
            }
            await Task.Delay(250);

            // Allow synchronization tasks to pump if any are pending
            await Task.Yield();
        }
        throw new TimeoutException($"Timed out after {timeoutMs}ms waiting for item in collection matching predicate.");
    }
}
