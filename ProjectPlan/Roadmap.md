# MeshWave Development Roadmap

## Current Phase Summary

### Completed

- Core .NET 10 solution/project structure
- Dark theme styling baseline and readability fixes
- Audio playback via NAudio
- Stable stop/play behavior
- Shared playback session across tab navigation
- Icon-based playback controls
- Settings persistence in AppData
- Base folder support (`My Music`, `Other Music`)
- Editable/persisted supported extension list
- My Music import with progress + cancellation
- Artist -> Album -> Track hierarchical library browsing
- Separate Library (community) and My Music views
- Cover extraction/cache and display in library + playback
- Waveform cache loading; background generation when cache missing
- Timeline comment markers (first implementation)
- Scrollable artist/album/track lists

### In Progress

- Profile model/editing (name, avatar, generated icon)
- Play-count registration + sync preparation
- My Music release/version lifecycle (draft/released state and track update flow)

## Next Milestones

### Milestone A: Profile + Social UX

- User profile persistence (name, avatar, bio)
- Rounded icon generation from profile image
- Use user icon in comments/timeline markers

### Milestone B: Playback UX Completion

- Click marker/comment to seek
- Marker hover previews and richer visuals
- Stabilize waveform generation/caching telemetry
- Comment filtering by target track version (all/current toggle)

### Milestone C: Library & Persistence

- File-based DB or lightweight index for faster startup
- More robust artist/album statistics
- Improved search/filter and sorting

### Milestone D: Community Sync

- Community library ingestion flow (`Other Music`)
- Play count sync strategy
- P2P comment/profile metadata exchange
