# Test Failure Diagnostics with Memory Logger

## Summary

The `BrowseViewModelIntegrationTests.BrowsingReleasesTracksWithUpdates()` test now outputs peer memory logs when timeout failures occur. This allows developers to diagnose P2P protocol issues and synchronization problems.

## Implementation

### Test File: BrowseViewModelIntegrationTests.cs

The test has been modified to wrap timeout-prone assertions in try-catch blocks that:

1. **Catch timeout exceptions** from `WaitForItemPollingAsync()`
2. **Output peer logs** to both Debug output and exception message
3. **Wrap in new exception** with formatted logs for visibility in test results

### Key Changes

```csharp
try
{
	await ViewModelTestHelpers.WaitForItemPollingAsync(() => johnBrowseViewModel.Artists, a => a.UserId == jane.UserId, timeoutMs: 5000);
}
catch (Exception ex)
{
	OutputPeerLogs(john, jane);
	throw new Exception($"{ex.Message}\n\n=== JOHN'S LOGS ===\n{john.GetLogsAsString()}\n\n=== JANE'S LOGS ===\n{jane.GetLogsAsString()}", ex);
}
```

## Test Output Example

When a timeout occurs, the test failure output now includes:

```
=== JOHN'S LOGS ===
[2026-06-06 17:34:15.0982][INFO][John] Starting SyncOrchestrator for user d7fac6df-92e2-8834-2b37-5f9d51453431 (listener=True)
[2026-06-06 17:34:15.1160][INFO][John] Starting NAT discovery for port 55514 (TCP/UDP)
[2026-06-06 17:34:15.2154][INFO][John] Found NAT device: Pmp (192.168.0.1:5351). External IP: 185.181.221.55
...

=== JANE'S LOGS ===
[2026-06-06 17:34:16.4868][INFO][Jane] Starting SyncOrchestrator for user f4113140-239f-fff7-a9b5-e4f1d538308e (listener=True)
[2026-06-06 17:34:16.4868][INFO][Jane] Starting NAT discovery for port 55520 (TCP/UDP)
[2026-06-06 17:34:16.5700][INFO][Jane] Found NAT device: Pmp (192.168.0.1:5351). External IP: 185.181.221.55
...
```

## What to Look For in Logs

When debugging test timeouts, examine the peer logs for:

1. **NAT Mapping**: Are both peers successfully mapping ports?
2. **Peer Discovery**: Are peers connecting to each other?
3. **Profile Broadcasting**: Are social manifest updates being logged?
4. **PEX (Peer Exchange)**: Are peers exchanging peer lists?
5. **Manifest Operations**: Are sync operations completing?
6. **Error Messages**: Look for connection failures or exceptions

## Advantages

✅ **Immediate diagnostics**: See detailed peer activity without test infrastructure changes  
✅ **Non-intrusive**: Doesn't modify timeout values or add delays  
✅ **Comprehensive logging**: All NLog entries from peer initialization through sync  
✅ **Per-peer isolation**: John and Jane have separate loggers for clear separation  
✅ **Easy to extend**: Other tests can use the same pattern  

## Running the Test

```powershell
cd E:\Projects\MeshWave
dotnet test MeshWave.slnx --filter "BrowsingReleasesTracksWithUpdates"
```

When the test fails, the exception message will include both peer logs directly in the test result output.
