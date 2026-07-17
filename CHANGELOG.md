# Changelog

All notable Cadence Studio changes are recorded here in reverse chronological order. Repeated micro-build entries from the development cycle have been consolidated under their final milestone versions for clarity.

## v1.0 — Stable release

- Promoted the validated v1.0 release candidate to the first stable Cadence Studio release.
- Replaced release-candidate interface badges with **Stable Release**.
- Finalized product versioning, README presentation, release notes, installer metadata, and packaging output for v1.0.
- Preserved the complete validated feature set with no new post-RC functionality.

## v1.0-rc.6 — Final compact Queue and presentation polish

- Increased compact Queue title space and reduced artwork and action-control footprints.
- Reserved stable columns for duration, play, and remove actions.
- Added full file-path tooltips to truncated Queue titles.
- Changed the header subtitle to **Audio Experience**.
- Updated About and workspace stage badges for release-candidate validation.
- Simplified the Settings visual-system banner and removed development-era presentation text.

## v1.0-rc.4 — Documentation and release-history polish

- Rewrote the README around the finished Cadence Studio product rather than the old development narrative.
- Added clear installer, portable, source-build, packaging, user-data, privacy, and project-boundary documentation.
- Reorganized and consolidated the changelog into a single reverse-chronological history.
- Preserved the v1.0 feature freeze and all validated runtime and packaging behavior.

## v1.0-rc.3 — Final interface wording cleanup

- Removed the obsolete Now Playing promotional card from the navigation sidebar.
- Renamed the Settings heading from **Premium visual language** to **Visual Language**.
- Preserved the release-candidate feature freeze and validated packaging workflow.

## v1.0-rc.2 — Release-candidate presentation cleanup

- Replaced development-era interface wording with finished-product language.
- Replaced the `DEV BUILD` badge with `RELEASE CANDIDATE`.
- Removed stale milestone references from the Settings visual-system introduction.
- Preserved all features and release-engineering behavior from rc.1.

## v1.0-rc.1 — First release candidate

- Froze the v1.0 feature set for production validation.
- Added automatic Inno Setup discovery through PATH, standard machine-wide locations, and the standard per-user location.
- Added optional `-InnoCompilerPath` support to the complete release command.
- Added post-build artifact and SHA-256 verification.
- Added the dedicated release-candidate test plan.
- Preserved the approved Music Library alignment and packaging fixes.

## v1.0-dev.10.15.1 — Packaging reliability and global search completion

- Fixed portable packaging by preventing `dotnet publish` console output from contaminating the returned publish-directory path.
- Added publish-directory validation and clearer packaging failures.
- Completed the floating global-search overlay for expanded mode.
- Grouped results by artist, album, track, and genre without shifting the main layout.
- Added exact, prefix, and partial-match ranking, click-to-play results, keyboard navigation, clear behavior, and a no-results state.
- Produced self-contained and framework-dependent portable packages, manifests, release notes, and SHA-256 checksums.
- Added the per-user Inno Setup installer with Start Menu integration and an optional desktop shortcut.

## v1.0-dev.10.14.8 — Music Library action alignment

- Moved Library row actions into a consistent far-right column.
- Prevented long folder names from shifting action controls.
- Preserved nested-folder indentation, readable labels, and compact workspace behavior.

## v1.0-dev.10.14.4 — Shared control and action polish

- Rebuilt the global pill system with cleaner borders, restrained highlights, better padding, and consistent hierarchy.
- Added dedicated utility, primary-action, segmented, status, and selected-state treatments.
- Enlarged compact Library, Queue, and Playlist action icons.
- Improved selected-state contrast and rebalanced crowded action groups.
- Replaced stale milestone version text in the Now Playing workspace.

## v1.0-dev.10.14.1 — Secondary workspace polish

- Polished Playlists, Queue, Settings, About, Keyboard Shortcuts, and metadata dialogs.
- Strengthened card hierarchy, title treatments, row readability, spacing, and theme consistency.
- Refined the single-track and album Metadata Workshop surfaces and review tables.

## v1.0-dev.10.14.0 — Compact Now Playing visualizer balance

- Increased the compact spectrum stage height and pulled it closer to the artwork.
- Added subtle album-art ambience behind the compact Now Playing stage.
- Preserved the expanded Now Playing composition.

## v1.0-dev.10.13.1 — Main-workspace balance

- Tightened compact Now Playing composition and reduced dead space.
- Enlarged the compact visualizer stage.
- Widened Insights and Queue for readability.
- Simplified Library header actions while preserving expanded mode behavior.

## v1.0-dev.10.13.0 — Notification shell refinement

- Removed the remaining accent-colored outer halo from the Now Playing notification.
- Kept a single premium card with a restrained neutral shadow.
- Increased edge and taskbar clearance for all notification corners.

## v1.0-dev.10.12.1 — Notification layout and controls

- Refined the song-change notification layout and removed the doubled-shell appearance.
- Added vector previous, play/pause, and next controls.
- Improved long-title handling, progress contrast, album readability, and taskbar clearance.

## v1.0-dev.10.12.0 — Song-change notifications

- Added theme-aware notifications with artwork, metadata, progress, and playback controls.
- Added configurable duration, screen corner, enable state, and focus suppression.
- Added fade-in/fade-out behavior and click-to-return to Cadence Studio.

## v1.0-dev.10.11.8 — Window Visualizer production polish

- Added smooth preset cross-fades and per-preset visual tuning.
- Added distinct paused and idle presentation.
- Added keyboard preset switching and configurable control auto-hide.
- Added reduced-motion behavior and preserved window geometry and last-used preset.
- Prevented duplicate ambient animation startup.

## v1.0-dev.10.11.6 — Window Visualizer preset suite

- Added Aurora Cascade, Glass Horizon, and Infinite Windows alongside Celestial Resonance.
- Added preset selection in Settings and in the detached visualizer.
- Added remembered preset state and live switching while the window remains open.

## v1.0-dev.10.11.5 — Complete theme isolation

- Fixed theme bleed caused by retained gradient colors from the previously selected theme.
- Rebuilt premium cards, buttons, menus, selections, hover gradients, visualizer palettes, and detached-window chrome on every theme change.
- Ensured all six themes independently own app-wide surfaces and controls.

## v1.0-dev.10.11.3 — Theme-aware visualizer architecture

- Added per-theme seven-band visualizer colors, core glow, atmospheric color, and detached-window styling.
- Unified menus, transport, sliders, Queue, Lyrics, and selected states around dynamic theme resources.

## v1.0-dev.10.11.2 — Premium main window and visualizer foundation

- Rebuilt the main-window composition with a stronger title bar, global search, theme identity, playback status, balanced workspace proportions, and an upgraded player dock.
- Enlarged the Now Playing artwork and typography hierarchy.
- Added the layered spectrum stage, energy line, glow bars, particles, pulse rings, and frequency-zone labels.
- Added the detached Window Visualizer foundation with floating, always-on-top, maximized, fullscreen, remembered placement, and hover controls.
- Rebuilt context menus and visualizer menus with theme-aware premium chrome.

## v1.0-dev.10.9.10 — Global search interaction

- Added grouped Artist, Album, Track, and Genre search results.
- Added result selection, direct playback, relevance ordering, and keyboard entry into the results list.

## v1.0-dev.10.9.9 — Premium Settings and typography system

- Completed the six-theme visual catalog and the Cadence, Precision, and Clear typography profiles.
- Added Standard, Large, and Extra Large text-size options.
- Added stronger accessibility, focus, and readability treatments.

## v1.0-dev.10.9 — Premium visual-language foundation

- Began the full-app move beyond strict minimalism toward a richer, cinematic Cadence identity.
- Added the premium shared-resource layer for cards, navigation, pills, buttons, menus, dropdowns, selections, focus, and hover states.
- Added Obsidian Gold and Aurora Pulse.
- Added persistent visualizer-experience settings and advanced the settings schema while preserving existing user state.

## v1.0-dev.10.8.9 — Album release selector

- Added the MusicBrainz release-candidate selector to Album Metadata Workshop.
- Kept the selected edition locked while release metadata and artwork reloaded.
- Updated album, album artist, year, track total, artwork, and batch preview from the selected release.

## v1.0-dev.10.8.8 — Metadata Workshop suite

- Added safe single-track and full-album metadata workflows.
- Added MusicBrainz release ranking and manual release selection.
- Added editable metadata proposals, local and remote artwork review, and selective field application.
- Added timestamped backups before every write.
- Added post-write readback and verification with per-track success and failure reporting.
- Added selective album writes so bonus tracks and unrelated files can be excluded.
- Replaced native ComboBox chrome with a fully themed release selector and loading state.
- Fixed selected-release application, artwork reload behavior, and official-album ranking.
- Fixed queue artwork prefetch confidence and stale negative cache handling.
- Extended tactile button and pill styling across primary actions, toggles, tabs, and Queue controls.

## v1.0-dev.10.7.4 — Next-track reliability

- Rewired visible Next controls through an explicit click path.
- Hardened next-track selection around restored sessions, missing indices, shuffle, repeat-all, and duplicate queue entries.
- Added diagnostic logging for manual Next requests.

## v1.0-dev.10.7.3 — Artwork transition reliability

- Prevented album-art transitions from retriggering on routine playback snapshots.
- Added byte-array identity and SHA-256 checks so visually identical artwork is not animated repeatedly.
- Preserved intentional crossfades when artwork genuinely changes.

## v1.0-dev.10.7.2 — Eye Candy polish

- Redesigned transport controls, sliders, Queue cards, lyrics surfaces, loading overlays, and empty states.
- Added compact/expanded, artwork, insight-tab, and Queue entrance transitions.
- Connected new motion to Reduce Motion.

## v1.0-dev.10.7.1 — Typography startup fix

- Fixed a startup exception caused by a TwoWay binding against read-only typography display properties.
- Changed active typography summary bindings to OneWay.

## v1.0-dev.10.7 — Typography and accessibility

- Added approved typography profiles and adjustable text sizing.
- Standardized medium, semibold, and bold weights for clarity.
- Improved large-text behavior in Lyrics and Now Playing.

## v1.0-dev.10.6.1 — Dialog chrome fix

- Corrected dialog title-bar, border, button, and theme consistency issues.

## v1.0-dev.10.6 — Reliability, diagnostics, About, and shortcuts

- Added About, diagnostics reporting, log/cache/session shortcuts, and the Keyboard Shortcuts reference.
- Added single-instance handling and stronger session-recovery behavior.
- Improved clean shutdown, offline startup, and unavailable-service handling.

## v1.0-dev.10.5.3 — Expanded Queue polish

- Refined expanded Queue spacing, selected state, action placement, and artwork presentation.

## v1.0-dev.10.5.2 — Expanded stage balance

- Rebalanced the expanded artwork, metadata, visualizer, insights, and Queue proportions.

## v1.0-dev.10.5.1 — Expanded Queue layout

- Added the expanded Queue composition and stronger row hierarchy.

## v1.0-dev.10.5 — Now Playing workspace

- Added compact and expanded Now Playing modes.
- Added artwork-led presentation, queue visibility, lyrics and biography insights, and integrated visualizer space.

## v1.0-dev.10.4 — Core interface polish

- Improved shared cards, controls, navigation, spacing, loading states, and theme consistency across the main application.

## v1.0-dev.10.3 — Queue artwork enrichment

- Added background artwork enrichment for queued tracks with caching and rate limiting.

## v1.0-dev.10.2 — Fast Library startup

- Added the persistent Library index and cached startup path for very large collections.
- Preserved real folder hierarchy and rescanning behavior.

## v1.0-dev.10.1 — Online cover art

- Added Cover Art Archive retrieval, caching, fallback behavior, and offline reuse.

## v1.0-dev.10 — Music enrichment

- Added MusicBrainz matching, LRCLIB lyrics, Wikipedia artist biographies, local caching, and rate-limited background enrichment.

## v1.0-dev.9 — Theme presets and Studio UI polish

- Added the original theme-preset architecture and centralized runtime color application.
- Converted theme-dependent brushes to dynamic resources so open windows update immediately.
- Updated window, sidebar, cards, controls, scrollbars, tooltips, and fallback chrome together.

## v1.0-dev.8.2 — Full-spectrum FFT response

- Fixed bass-only response by analyzing channel energy independently instead of summing stereo samples before FFT.
- Prevented phase cancellation from hiding wide stereo vocals, guitars, synths, and cymbals.
- Reworked logarithmic analysis across approximately 30 Hz to 18 kHz.

## v1.0-dev.8.1 — Visualizer presentation patch

- Fixed immediate switching between 24, 32, and 48 spectrum bars.
- Enlarged and polished the Now Playing visualizer presentation.

## v1.0-dev.8 — Real FFT visualizer

- Added a real 2048-point FFT driven by the post-equalizer playback stream.
- Added density, sensitivity, decay, palette, and persistence controls.

## v1.0-dev.7 — Equalizer and audio processing

- Added the real-time 10-band equalizer, preamp, presets, custom profile, and headroom protection.
