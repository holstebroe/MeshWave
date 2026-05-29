# Implementation Plan - Bugs & Architecture

## Phase 1: Critical Bug Fixes ✅ COMPLETED
1. ✅ Fix dark theme text visibility (already properly configured)
2. ✅ Fix Stop button breaking playback
   - Updated AudioPlaybackService.Stop() to keep file loaded
   - Added ability to Play() after Stop()
3. ✅ Consolidate Play/Pause to single toggle button
   - Added PlayPauseToggle command
   - Button displays "▶ Play" or "⏸ Pause" based on IsPlaying state

## Phase 2: Settings Infrastructure ✅ COMPLETED
1. ✅ Create Settings model (AppSettings, P2PSettings, PlaybackSettings)
2. ✅ Create SettingsService (load/save to AppData, folder management)
3. ✅ Create SettingsViewModel with commands
4. ✅ Create Settings view UI (base folder, playback, theme sections)
5. ✅ Implement AppData storage (%APPDATA%\MeshWave\settings.json)
6. ✅ Base folder selection with folder browser

## Phase 3: Storage Architecture (IN PROGRESS)
1. ⏳ Implement folder structure (My Music / Other Music / .cache / .comments)
2. ⏳ Create import workflow
3. ⏳ Metadata caching system

## Phase 4: User Profile
1. ⏳ Profile model and storage
2. ⏳ Profile editor UI
3. ⏳ Icon generation

---

**Current Task**: Phase 3 - Storage Architecture  
**Next Step**: Implement folder structure and import workflow
