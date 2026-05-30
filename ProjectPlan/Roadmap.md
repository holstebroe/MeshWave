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

- Timeline markers with rounded user icons
- Profile model/editing (name, avatar, generated icon)
- Better comment interactions (jump to marker, preview)
- Play-count registration + sync preparation

## Next Milestones

### Milestone A: Profile + Social UX

- User profile persistence (name, avatar, bio)
- Rounded icon generation from profile image
- Use user icon in comments/timeline markers

### Milestone B: Playback UX Completion

- Click marker/comment to seek
- Marker hover previews and richer visuals
- Stabilize waveform generation/caching telemetry

### Milestone C: Library & Persistence

- File-based DB or lightweight index for faster startup
- More robust artist/album statistics
- Improved search/filter and sorting

### Milestone D: Community Sync

- Community library ingestion flow (`Other Music`)
- Play count sync strategy
- P2P comment/profile metadata exchange
