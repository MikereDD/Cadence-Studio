# Cadence Studio roadmap

Cadence Studio is a separate C#/WPF/XAML/MVVM application. Cadence Classic remains the stable PowerShell/WinForms project.

Stable v1.0 is gated by quality, not schedule. The app will not be called stable until it is reliable, visually cohesive, accessible, performant, and properly packaged.

## Completed foundation

- **v1.0-dev.1** — architecture, MVVM shell, settings, logging, Studio UI
- **v1.0-dev.2** — NAudio playback, queue, seek, volume, shuffle, repeat
- **v1.0-dev.3** — metadata, embedded/folder art, accurate source details
- **v1.0-dev.4** — persistent indexed library and real folder hierarchy
- **v1.0-dev.5** — queue management, playlists, M3U/M3U8, responsive UI polish
- **v1.0-dev.6** — full paused session, workspace, playlist, and window restore
- **v1.0-dev.7** — 10-band equalizer, presets, preamp, custom profile, and headroom protection
- **v1.0-dev.8 / 8.1 / 8.2** — real FFT visualizer, polished presentation, and corrected full-spectrum response
- **v1.0-dev.9** — selectable Studio themes and suite-wide UI polish
- **v1.0-dev.10 through 10.3** — MusicBrainz, lyrics, artist biography, cover art, fast startup, and queue enrichment
- **v1.0-dev.10.4 through 10.7.4** — Eye Candy passes, expanded Now Playing, typography, diagnostics, shortcuts, and reliability fixes
- **v1.0-dev.10.8 through 10.8.9** — Metadata Workshop, album batch workflow, release correction, backups, approved tag/artwork writing, and verification

## Current milestone

### v1.0-dev.10.9 — Premium visual language foundation

- move the full app beyond strict minimalism toward a bold, cinematic, premium identity;
- establish richer layered surfaces, metallic depth, glow, shadows, and coordinated motion;
- overhaul themes, pills, primary/secondary buttons, transport controls, navigation, theme cards, dropdowns, and menus;
- add flagship **Obsidian Gold** and **Aurora Pulse** themes;
- preserve readability, medium-or-heavier typography, keyboard focus, reduced motion, and performance;
- stage persistent settings and enums for the future Now Playing Visualizer and detached Window Visualizer.

## Planned milestones

### v1.0-dev.10.10 — Frequency engine and Now Playing Visualizer

- analyze sub-bass, bass, low mids, mids, upper mids, presence, and brilliance;
- add adaptive normalization, smoothing, peak awareness, and consistent behavior across recordings;
- use frequency data to drive motion, intensity, and coordinated color behavior;
- rebuild the embedded Now Playing Visualizer with album-driven color and a richer presentation.

### v1.0-dev.10.11 — Window Visualizer

- detached top-level visualizer outside the main app;
- ultra-thin custom border, independent resize and monitor placement;
- pop-out, floating/always-on-top, and fullscreen modes;
- remembered geometry and monitor;
- hidden playback controls;
- flagship **Celestial Resonance** preset with dramatic audio-reactive geometry, waveform, particles, glow, and reflections.

### v1.0-dev.10.12 — Visualizer settings and Now Playing notifications

- dedicated Visualizer settings page;
- separate **Now Playing Visualizer** and **Window Visualizer** sections;
- quality, frame-rate, reduced-motion, album-color, intensity, controls, and auto-open settings;
- custom song-change popup with artwork, metadata, transport controls, placement, and duration options.

### v1.0-dev.10.13 — Full-app eye-candy pass

- revisit Library, Artists, Albums, Search, Queue, Playlists, Settings, Metadata Workshop, dialogs, menus, compact mode, expanded mode, loading states, and error states;
- ensure the entire app matches the new premium visual direction.

### v1.0-dev.10.14 — Album Immersion Mode

- large animated artwork, full album sequence, credits, total album progress, disc/side awareness, integrated visualizer, palette transitions, gapless playback, and distraction-free fullscreen.

### v1.0-dev.10.15 — Cadence Scenes and musical transitions

- save theme, EQ, layout, visualizer presets, window mode/monitor, notification style, album-color behavior, motion, and glow as one Scene;
- optional activation by genre, output device, or time of day;
- smooth same-album, cross-album, and cross-style visual transitions.

### v1.0-dev.10.16 — Signal Path Display

- transparent audio-chain view for codec, bit depth, sample rate, channels, ReplayGain, EQ, volume processing, resampling, output device, and final output format.

### v1.0-dev.10.17 — Private Listening Journal

- local-only history, completed albums, listening time, most-played tracks/artists, skips/completions, first/latest play, notes, favorites, and timestamp bookmarks;
- no cloud account, telemetry, or social requirement.

### v1.0-dev.10.18 — Intelligent Library Recovery

- missing-drive detection, moved-folder recovery, metadata/fingerprint relocation, playlist and queue repair, duplicate discovery, damaged tag/artwork detection, and reviewed recovery previews.

### v1.0-dev.10.19 — Performance, accessibility, and reliability hardening

- GPU/CPU efficiency, long-session stability, frame-rate limits, reduced motion, contrast, keyboard/focus behavior, multi-monitor recovery, rendering-failure isolation, and safe fallbacks.

### v1.0-dev.10.20 — Packaging and release experience

- proper installer, Start Menu shortcut, optional desktop shortcut, clean upgrades, user-data preservation, uninstall behavior, versioned release folders, release notes, and optional portable ZIP.

### v1.0-rc.2 — Release candidate

Feature freeze. Test fresh install, upgrade install, portable build, large-library startup, long playback, queue/playlist persistence, compact/expanded modes, every theme and typography profile, keyboard shortcuts, About/diagnostics, offline startup, provider outages, missing/moved files, corrupt session/cache recovery, visualizer failure recovery, clean shutdown, installer, and uninstall.

### v1.0 — Stable

Release only when Cadence Studio is genuinely worthy of the stable label.
