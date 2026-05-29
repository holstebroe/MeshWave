# MeshWave - Known Bugs & Issues

## 🔴 **CRITICAL - Blocking Core Functionality**

### 1. Dark Theme Text Visibility
**Status**: Open  
**Priority**: Critical  
**Severity**: High

**Description**:  
Black text titles are very hard to see against the dark background (#1A1A1A).

**Impact**:  
Poor readability, bad user experience, accessibility issue.

**To Reproduce**:
1. Open any view (Library, Playback, etc.)
2. Look at text labels and titles
3. Notice poor contrast

**Expected Behavior**:  
All text should use `TextPrimaryColor` (#FFFFFF) or `TextSecondaryColor` (#B0B0B0) from shared styles.

**Fix Needed**:
- Audit all XAML views for hardcoded `Foreground` colors
- Ensure all TextBlocks use `{StaticResource TextPrimaryColor}` or `{StaticResource TextSecondaryColor}`
- Update menu items, labels, and headers

---

### 2. Playback Not Working After Stop Pressed
**Status**: Open  
**Priority**: Critical  
**Severity**: High

**Description**:  
After pressing the Stop button, pressing Play does not resume playback. The audio service appears to be in an invalid state.

**Impact**:  
Core playback functionality broken. User must restart app to play again.

**To Reproduce**:
1. Play a track
2. Press Stop button
3. Press Play button
4. Nothing happens

**Expected Behavior**:  
Stop should pause playback and reset position to 0:00. Play should start playback again from the beginning.

**Root Cause**:  
Likely the `AudioPlaybackService.Stop()` method disposes the audio file and wave output, but `Play()` doesn't check if they need to be reinitialized.

**Fix Needed**:
- Update `AudioPlaybackService.Stop()` to keep file loaded but stop playback
- Or update `Play()` to reload file if disposed
- Consider separating Stop (dispose everything) from Pause (keep loaded)

---

## 🟡 **MEDIUM - UX Issues**

### 3. Play/Pause Button UX Confusion
**Status**: Open  
**Priority**: Medium  
**Severity**: Low

**Description**:  
Currently there are 3 separate buttons: Play, Pause, Stop. This is confusing and takes up space.

**Impact**:  
Non-standard UI, wastes space, not intuitive.

**Expected Behavior**:  
- Single Play/Pause toggle button
- Icon/text switches based on `IsPlaying` state:
  - When stopped/paused: Show "▶ Play"
  - When playing: Show "⏸ Pause"
- Keep Stop button separate if needed for reset functionality
- Or remove Stop entirely and use Pause + click to start from beginning

**Fix Needed**:
- Update PlaybackView.xaml to use single button
- Bind to single command that checks `IsPlaying` state
- Update button content/icon based on state using data trigger or converter

---

## 🟢 **LOW - Minor Issues**

### 4. Waveform is Placeholder Data
**Status**: Known Limitation  
**Priority**: Low  
**Severity**: Low

**Description**:  
Waveform visualization uses random placeholder bars instead of actual audio data.

**Impact**:  
Visual representation doesn't match audio. Not a blocker but reduces polish.

**Expected Behavior**:  
Display actual waveform generated from audio file peaks.

**Fix Needed**:  
See TODO.md - Real Waveform Generation

---

### 5. File Path Stored in FileHash Field
**Status**: Technical Debt  
**Priority**: Low  
**Severity**: Low

**Description**:  
Track file path is temporarily stored in the `FileHash` field as a workaround. This is confusing and semantically incorrect.

**Impact**:  
Code confusion, prevents proper file hashing for content addressing.

**Expected Behavior**:  
Add proper `FilePath` property to Track model.

**Fix Needed**:
- Update `Track.cs` to add `FilePath` property
- Update `LocalLibraryManager` to use `FilePath` instead of `FileHash`
- Implement actual SHA-256 hashing for `FileHash`

---

### 6. Multiple File Watcher Refreshes on Bulk Operations
**Status**: Known Issue  
**Priority**: Low  
**Severity**: Low

**Description**:  
When multiple files are added/removed at once, the file watcher triggers multiple library refreshes.

**Impact**:  
Performance hit, UI flicker, unnecessary re-scans.

**Expected Behavior**:  
Debounce file watcher events to trigger single refresh after batch operations complete.

**Fix Needed**:
- Add debounce timer to `MusicFolderWatcher`
- Wait 500-1000ms after last change event before triggering callback
- Cancel pending refresh if new change detected

---

## 🔵 **RESOLVED**

### ✅ Stack Overflow on Double-Click Track
**Status**: Fixed  
**Priority**: Critical (was)  
**Severity**: High (was)  
**Fixed In**: 2025-05-29  
**Fixed By**: Adding `_isUpdatingPosition` flag to break circular update loop

**Description**:  
Double-clicking a track caused infinite recursion between `CurrentPosition` setter and `PositionChanged` event handler.

**Root Cause**:  
Circular update: `PositionChanged` → set `CurrentPosition` → call `SetPosition` → fire `PositionChanged` → loop

**Fix Applied**:  
Added guard flag `_isUpdatingPosition` to prevent setter from calling `SetPosition` when update originates from the service.

---

## 📊 **Bug Statistics**

- **Total Open Bugs**: 6
- **Critical**: 2
- **Medium**: 1
- **Low**: 3
- **Resolved**: 1

---

## 🔍 **Testing Notes**

### Not Yet Tested:
- [ ] Large library performance (1000+ tracks)
- [ ] Playback of various audio formats (FLAC, OGG, M4A)
- [ ] File watcher behavior with network drives
- [ ] Memory leaks during long playback sessions
- [ ] Concurrent playback attempts
- [ ] Invalid file handling (corrupt MP3, unsupported format)

### Needs Regression Testing After Fixes:
- [ ] Stop → Play workflow
- [ ] Text visibility in all views
- [ ] Play/Pause button toggle

---

**Last Updated**: 2025-05-29  
**Reported By**: User testing feedback
