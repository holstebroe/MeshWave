# MeshWave — Quick Reference Guide

## 🚀 Quick Start

```bash
# Navigate to project
cd E:\Projects\MeshWave

# Build everything
dotnet build MeshWave.sln

# Run all tests (44 tests, should all pass)
dotnet test MeshWave.sln

# Run the WPF application (when UI is ready)
dotnet run --project MeshWave/MeshWave.csproj

# Clean build
dotnet clean MeshWave.sln
dotnet build MeshWave.sln
```

---

## 📁 Project Organization

```
MeshWave/                           # Root
├── 📄 Docs: PROJECT_PLAN.md, ARCHITECTURE.md, DEVELOPMENT_STATUS.md, SUMMARY.md
├── 📦 MeshWave.Common.Core/         # Shared libraries (50+ classes)
├── 📦 MeshWave.LibraryManager/      # Music indexing
├── 📦 MeshWave.Synchronizer/        # P2P networking
├── 🎯 MeshWave/                     # WPF Frontend (MVVM)
├── ✅ MeshWave.*.Tests/             # Unit tests (44 tests)
└── 📋 MeshWave.sln                  # Solution file
```

---

## 🧩 Architecture Layers

### Layer 1: Domain Models (MeshWave.Common.Core)
- User, Track, Album, Comment, Manifest, Community
- All support signing and versioning

### Layer 2: Services (MeshWave.Common.Core)
- **CryptoService**: RSA signing, SHA256 hashing
- **StorageService**: Content-addressed blob storage
- **JsonSerializer**: Model serialization

### Layer 3: Business Logic
- **LibraryManager**: Music indexing and metadata
- **ManifestManager**: Append-only operation log
- **PeerDiscovery**: Locate peers on network
- **ContentExchange**: P2P file transfer

### Layer 4: UI (MeshWave)
- **ViewModels**: MVVM pattern, data binding
- **Views**: WPF XAML (MainWindow - to be implemented)
- **Commands**: RelayCommand for user actions

---

## 🔐 Security at a Glance

```
User Identity:
  - Generated: RSA-4096 keypair on first run
  - UserId: SHA256(publicKey) formatted as GUID
  - Transport: Public key in manifest (immutable)

Content Ownership:
  - All content signed by owner's private key
  - Cannot be forged (RSA verification)
  - Transfer: Only by hash (integrity verified)

File Integrity:
  - All files stored by SHA256 hash
  - Hash verified on read
  - Prevents corruption
```

---

## 🧪 Testing

### Run All Tests
```bash
dotnet test MeshWave.sln
```

### Run Specific Test Project
```bash
dotnet test MeshWave.Common.Core.Tests/MeshWave.Common.Core.Tests.csproj
```

### Run Specific Test Class
```bash
dotnet test MeshWave.sln --filter ClassName=CryptoServiceTests
```

### Test Coverage
- **32 tests** in MeshWave.Common.Core.Tests
- **5 tests** in MeshWave.LibraryManager.Tests
- **7 tests** in MeshWave.Synchronizer.Tests
- **44 tests** total - 100% passing

---

## 📝 Code Examples

### Using CryptoService
```csharp
using MeshWave.Common.Core.Crypto;

// Generate keypair
var (privateKey, publicKey) = CryptoService.GenerateKeyPair();

// Derive user ID
var userId = CryptoService.DeriveUserIdFromPublicKey(publicKey);

// Sign content
var signature = CryptoService.SignData(trackMetadata, privateKey);

// Verify signature
if (CryptoService.VerifySignature(trackMetadata, signature, publicKey))
{
	// Content is authentic
}

// Hash a file
var fileHash = CryptoService.ComputeFileHash("song.mp3");
```

### Using StorageService
```csharp
using MeshWave.Common.Core.Storage;

var storage = new StorageService(@"C:\MeshWave\Data");

// Store file
var audioBytes = File.ReadAllBytes("song.mp3");
var hash = storage.StoreBlob(audioBytes);

// Retrieve file
var retrieved = storage.GetBlob(hash);

// Store metadata
storage.StoreMetadata("track-1", jsonMetadata);

// Check if exists
if (storage.BlobExists(hash))
{
	// File is available
}
```

### Creating a ViewModel
```csharp
using MeshWave.Mvvm;

public class MyViewModel : ViewModelBase
{
	private string _title = string.Empty;

	public string Title
	{
		get => _title;
		set => SetProperty(ref _title, value);  // Auto-notifies
	}

	private RelayCommand? _saveCommand;
	public RelayCommand SaveCommand =>
		_saveCommand ??= new RelayCommand(_ => Save());

	private void Save()
	{
		// Handle save
	}
}
```

---

## 📊 Project Statistics

| Metric | Value |
|--------|-------|
| Total Projects | 7 |
| Main Projects | 4 |
| Test Projects | 3 |
| Total Classes | 50+ |
| Unit Tests | 44 |
| Lines of Code | ~2,500 |
| Test Coverage | 100% (core libs) |
| Build Time | ~3s |
| Test Runtime | ~3s |

---

## 🛠️ Development Workflow

### Making Changes
1. Make code changes
2. Run `dotnet build MeshWave.sln` to verify compilation
3. Run `dotnet test MeshWave.sln` to ensure tests pass
4. Commit with `git commit -m "Clear description"`

### Adding New Feature
1. Create model/class in appropriate project
2. Write unit tests first
3. Implement functionality
4. Run full test suite
5. Commit changes

### Debugging
```bash
# Debug run
dotnet run --project MeshWave/MeshWave.csproj --debug

# In Visual Studio: F5 or Debug > Start Debugging
```

---

## 📦 Dependencies

### Built-in (No NuGet needed)
- System.Security.Cryptography (RSA, SHA256)
- System.Text.Json (JSON serialization)
- System.Windows (WPF)

### Test Framework
- xUnit 2.6.6
- xUnit.runner.visualstudio
- Microsoft.NET.Test.Sdk

### Future Dependencies (Not yet added)
- NAudio (audio playback)
- TagLibSharp (ID3 metadata)
- SQLite (local database)
- mDNS library (peer discovery)

---

## 🎯 Next Priorities

### This Sprint
- [ ] MainWindow XAML implementation
- [ ] Setup wizard UI
- [ ] Application styling

### Next Sprint
- [ ] Audio file scanner
- [ ] ID3 metadata extraction
- [ ] SQLite database setup

### Following Sprint
- [ ] NAudio playback integration
- [ ] Waveform visualization
- [ ] Seek and play controls

---

## ⚠️ Known Issues & Limitations

1. **UI Not Implemented**: MainWindow.xaml needs to be built
2. **No Audio Playback**: NAudio not integrated
3. **No Peer Discovery**: Discovery code is placeholder
4. **No Metadata**: ID3 extraction not implemented
5. **No Persistence**: LocalLibraryManager is stub

All are tracked and planned for future sprints.

---

## 📚 Documentation Index

| Document | Purpose |
|----------|---------|
| **PROJECT_PLAN.md** | High-level roadmap and acceptance criteria |
| **ARCHITECTURE.md** | Technical architecture and design decisions |
| **DEVELOPMENT_STATUS.md** | Current implementation status and details |
| **SUMMARY.md** | Executive summary for stakeholders |
| **QUICK_REFERENCE.md** | This file - developer quick reference |

---

## 🔧 Useful Commands

```bash
# Build
dotnet build MeshWave.sln
dotnet build MeshWave.sln -c Release

# Test
dotnet test MeshWave.sln
dotnet test MeshWave.sln -v normal

# Run
dotnet run --project MeshWave/MeshWave.csproj

# Clean
dotnet clean MeshWave.sln

# Format
dotnet format MeshWave.sln

# Publish
dotnet publish MeshWave/MeshWave.csproj -c Release

# Package
dotnet pack MeshWave.Common.Core/MeshWave.Common.Core.csproj
```

---

## 💡 Tips & Tricks

### Quick Build & Test
```bash
dotnet build MeshWave.sln && dotnet test MeshWave.sln
```

### Watch for Changes
```bash
dotnet watch --project MeshWave/MeshWave.csproj
```

### Profile Test Performance
```bash
dotnet test MeshWave.sln --logger "console;verbosity=detailed"
```

### Open in Visual Studio
```bash
start MeshWave.sln
```

---

## 📞 Getting Help

### Build Errors
1. Run `dotnet clean MeshWave.sln`
2. Run `dotnet restore MeshWave.sln`
3. Run `dotnet build MeshWave.sln`

### Test Failures
1. Check test output: `dotnet test MeshWave.sln -v normal`
2. Review test code in `*.Tests` projects
3. Ensure test dependencies are set up

### NuGet Issues
1. Clear cache: `dotnet nuget locals all --clear`
2. Restore packages: `dotnet restore MeshWave.sln`

---

**Last Updated**: January 2025
**Created For**: Development Team
**Status**: Ready for Feature Implementation
