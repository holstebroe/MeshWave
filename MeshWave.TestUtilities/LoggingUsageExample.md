# TestPeer Logging Usage

## Overview
Each `TestPeer` instance is automatically configured with:
- **MemoryTarget**: Stores all log entries in memory for retrieval during tests
- **ConsoleTarget**: Outputs logs to console with peer name prefix for real-time visibility

## Retrieving Logs

### Get logs as a list of strings
```csharp
var peer = TestPeerFactory.CreatePeer("PeerA");
// ... perform test operations ...

var logLines = peer.GetLogs(); // IReadOnlyList<string>
foreach (var line in logLines)
{
	Console.WriteLine(line);
}
```

### Get logs as a single string
```csharp
var peer = TestPeerFactory.CreatePeer("PeerA");
// ... perform test operations ...

var allLogs = peer.GetLogsAsString(); // string with newline-separated entries
Console.WriteLine(allLogs);
```

### Assert on log contents
```csharp
var peer = TestPeerFactory.CreatePeer("PeerA");
await peer.StartAsync();

var logs = peer.GetLogs();
Assert.Contains(logs, log => log.Contains("Connected to bootstrap node"));
```

## Log Entry Format

Memory log format (for programmatic access):
```
[{timestamp}][{LogLevel}][{LoggerName}] {Message} {Exception details}
```

Example from MemoryTarget.Logs:
```
[2025-01-15 10:30:45.1234][DEBUG][PeerA] Starting peer discovery
[2025-01-15 10:30:45.5678][INFO][PeerA] Connected to bootstrap node
```

Console log format (for real-time debugging):
```
[{PeerName}] [{timestamp}][{LogLevel}][{LoggerName}] {Message} {Exception details}
```

Example in console output:
```
[PeerA] [2025-01-15 10:30:45.1234][DEBUG][PeerA] Starting peer discovery
[PeerA] [2025-01-15 10:30:45.5678][INFO][PeerA] Connected to bootstrap node
```

## Use Cases

1. **Debugging test failures**: Retrieve peer logs to diagnose network or sync issues
2. **Test verification**: Assert on log contents to verify expected behavior occurred
3. **Monitoring**: Track peer activity across integration tests without file I/O overhead
4. **Cross-peer analysis**: Compare logs from multiple peers to understand P2P protocol interactions

## Implementation Details

- Each peer gets a unique logger name based on the peer's `name` parameter
- The MemoryTarget is configured to store all log levels from Trace to Fatal
- Logging rules route logs to both memory and console targets
- The NLog configuration is stored in the LogManager singleton, so peer-specific loggers persist across multiple peer creations

