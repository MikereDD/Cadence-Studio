# v1.0-dev.10.11.2 Premium Now Playing Stage Test

1. Run `./build.ps1 -Run`.
2. Start playback and open Expanded Now Playing.
3. Confirm blurred album-art ambience appears behind the hero area.
4. Confirm artwork, title, artist, album, metadata chips, spectrum, queue, and lyrics remain readable.
5. Switch between Aurora Pulse and Obsidian Gold and confirm accent lighting follows the active theme.
6. Test Compact/Expanded switching, queue play/remove, lyrics tabs, seek, volume, shuffle, repeat, and app restart.
7. Confirm no clipping at Windows scaling levels you normally use.

# v1.0-dev.10.11.2 Premium Main-Window Test

Focus this pass on the visible structural changes:

- Confirm the taller premium title bar renders correctly at restored and maximized sizes.
- Confirm the global header search filters the indexed library and its placeholder disappears while typing.
- Switch through all themes and verify the live theme badge, border accents, and transport dock remain readable.
- Check Library, Playlists, Queue, and Settings navigation for clipping at Large and Extra Large text sizes.
- Verify the larger album artwork and transport controls fit without overlap.
- Test at 100%, 125%, and 150% Windows scaling if practical.

# Cadence Studio v1.0-dev.10.9 testing

This is the first premium-overhaul test build. The focus is visual behavior and regression safety; the new frequency engine and detached Window Visualizer arrive in later milestones.

## Build and launch

1. Run `./build.ps1 -Run` from PowerShell.
2. Confirm the app builds and launches without an exception dialog.
3. Confirm the title bar and About dialog report `v1.0-dev.10.9`.
4. Confirm the existing library, queue, playlists, session, EQ, visualizer, and metadata state loads normally.

## Premium theme pass

1. Open **Settings → Premium visual language**.
2. Confirm the new premium foundation banner appears without clipping.
3. Test all six themes:
   - Dark Monochrome
   - OLED Black
   - Graphite
   - Midnight Indigo
   - Obsidian Gold
   - Aurora Pulse
4. Confirm every theme updates the full app immediately.
5. Confirm the selected theme card has a clear accent rail, light, border, and active state.
6. Confirm the theme survives a clean restart.
7. Pay special attention to text contrast in Obsidian Gold and Aurora Pulse.

## Premium controls

1. Test normal pills, accent pills, playback controls, queue actions, navigation buttons, chips, and theme cards.
2. Confirm hover states feel raised and luminous rather than flat.
3. Confirm press states visibly depress without shifting layout.
4. Confirm keyboard focus remains clearly visible.
5. Enable **Reduced motion** and confirm nonessential hover scaling is suppressed while focus/press feedback remains usable.
6. Confirm disabled controls remain readable but clearly inactive.

## Dropdowns and menus

1. Open Metadata Workshop for a track.
2. Open the MusicBrainz release dropdown and confirm:
   - the closed field uses the new premium layered surface;
   - the popup has a dark elevated surface and polished shadow;
   - highlighted and selected entries are distinct;
   - the selected edition remains readable after artwork reload.
3. Open **Tag full album** and repeat the same dropdown test.
4. Confirm loading, disabled, selected, and keyboard-navigation states remain dark and readable.

## Layout and scaling

1. Test at 100%, 125%, 150%, and 200% Windows display scaling when practical.
2. Resize the main window down to its minimum size and back up.
3. Confirm theme cards wrap correctly and do not overlap.
4. Test Standard, Large, and Extra Large text sizes in each new theme.
5. Confirm no premium shadows or glows clip important content.

## Functional regression

1. Start, pause, seek, skip next/previous, adjust volume, shuffle, and repeat.
2. Confirm transport controls still execute exactly once per click.
3. Confirm queue drag/drop, playlist load/save, and library navigation still work.
4. Confirm EQ and the current FFT visualizer still work.
5. Confirm Metadata Workshop and Album Metadata Workshop still back up, write, and verify correctly on expendable test files.
6. Close Cadence Studio and confirm session restore remains clean.

## Report back

Capture screenshots of any clipped surface, unreadable state, incorrect theme color, stock-looking dropdown/menu, or control that feels inconsistent with the premium direction.


## v1.0-dev.10.11.8 Window Visualizer checks

- Switch all four presets while music plays; verify smooth transitions.
- Pause playback; verify the visualizer enters a calmer ambient idle state.
- Use Left/Right and PageUp/PageDown to change presets.
- Enable Reduced Motion and verify orbital/tunnel movement becomes restrained.
- Resize, close, and reopen the visualizer; verify size, position, and preset persist.
- Test F11 and Esc repeatedly, including after moving the window to another monitor.
