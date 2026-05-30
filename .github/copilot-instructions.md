# Task Priority

When asked to continue working on the project, check ProjectPlan folder for Backlog.md, Roadmap.md, Status.md
and start working on the next tasks. Keep the session focused between each continue work prompt for easier testing.

Also update these files when a task is completed or if you get prompted with feature ideas, bugs, and change requests.

# Solution File

Use `MeshWave.slnx` (modern solution format) for all build and test operations. This is the canonical solution file.
The legacy `MeshWave.sln` is retained for backward compatibility but should not be used for new work.

**Build command:** `dotnet build MeshWave.slnx`
**Test command:** `dotnet test MeshWave.slnx`

The `.slnx` file includes:
- 5 source projects (MeshWave, Common.Core, LibraryManager, Synchronizer, Bootstrap)
- 4 test projects (7 test suites, 77 total tests)
- Documentation and ProjectPlan markdown files

# Code Style and Patterns

- Follow existing code conventions in the codebase
- Use explicit null checking where applicable (nullable reference types enabled)
- Async/await preferred for I/O operations (P2P mesh, file I/O)
- Signed RSA operations for manifest operations (do not bypass signature verification)
- Test coverage required for: P2P protocol changes, manifest operations, core sync logic
