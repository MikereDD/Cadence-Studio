# Cadence Studio architecture

Cadence Studio remains split into three projects:

```text
CadenceStudio.App            WPF/XAML, MVVM state, commands, dialogs, runtime themes
CadenceStudio.Core           contracts, settings, models, product identity
CadenceStudio.Infrastructure NAudio, TagLibSharp, JSON persistence, indexing
```

Dependencies point inward:

```text
App -> Core <- Infrastructure
```

## v1.0-dev.9 theme system

`AppTheme` lives in Core so the selected preset can be stored without introducing WPF dependencies into the domain model.

`ThemeManager` lives in the App project and applies a complete palette to the shared WPF resources. Theme-dependent XAML references use `DynamicResource`, allowing the open window, dialogs, tooltips, scrollbars, controls, and gradients to update immediately.

At startup:

```text
session.json -> AppSettings.Theme -> ThemeManager.Apply -> MainWindow construction
```

This ensures the saved theme is visible before the main window appears. Dark Monochrome is always the fallback for unknown or older settings.

## Audio graph

The playback graph is rebuilt for each opened track:

```text
AudioFileReader
  -> EqualizerSampleProvider
  -> SpectrumSampleProvider
  -> WaveOutEvent
```

`EqualizerSampleProvider` owns independent NAudio `BiQuadFilter` instances for every channel and every EQ band. Settings can change while playback is active without reopening the source.

`SpectrumSampleProvider` observes the post-equalizer float stream without changing it. It preserves each interleaved channel in its own FFT buffer, applies a Hamming window, performs a 2048-point FFT per channel, and combines channel energy after transformation. This avoids destructive stereo phase cancellation that can leave only centered bass visible. Bins are grouped logarithmically across approximately 30 Hz–18 kHz and normalized in decibels before frames are published.

The WPF view model owns visual smoothing, decay, density, palette brushes, and pause/stop reset behavior. FFT calculation stays in Infrastructure; presentation remains in App.

## Persistence

`AppSettings` schema version 5 persists:

- playback, queue, session, workspace, and window state;
- library roots and queue order;
- equalizer configuration;
- visualizer enabled state, sensitivity, decay, density, and palette;
- selected application theme.

Settings are normalized when loaded so older sessions migrate safely.


## v1.0-dev.10 enrichment pipeline

```text
Current Track
   │
   ├─ local TagLibSharp lyrics
   ├─ local .lrc / .txt sidecar
   ├─ MusicBrainz recording search (serialized request interval)
   ├─ MusicBrainz artist lookup
   ├─ LRCLIB lyrics fallback
   ├─ Wikipedia artist introduction
   └─ atomic per-track JSON cache
```

`ITrackEnrichmentService` keeps provider and cache behavior outside the WPF layer. `MainWindowViewModel.Enrichment` owns cancellation, tab state, status text, and track-change coordination. Playback never waits for online enrichment.


## v1.0-dev.10.1 artwork pipeline

Artwork remains local-first and non-destructive:

1. `TagLibMetadataService` reads embedded front cover artwork.
2. It checks supported folder-art filenames beside the audio file.
3. If both are absent, `MusicEnrichmentService` uses the matched MusicBrainz release MBID with the Cover Art Archive 500 px front endpoint.
4. If the exact release has no front image, the matched release-group MBID is tried.
5. The binary image is cached under `%LOCALAPPDATA%\CadenceStudio\cache\artwork`; the enrichment JSON stores only the safe cache filename and source.
6. `MainWindowViewModel` overlays the hydrated image onto Now Playing and the current queue item without rewriting the original audio file.
7. Cadence fallback artwork remains the final fallback for no-match, offline, or no-image cases.


## v1.0-dev.10.2 fast library startup

The saved library index is treated as the authoritative startup snapshot. Cadence no longer performs a filesystem existence probe for every indexed track before rendering. Playback and queue operations still validate files at use time, while an incremental scan reconciles stale entries. The folder hierarchy is constructed on a worker thread and attached to the bound `ObservableCollection` only after it is complete. Schema-1 indented JSON indexes are migrated once to compact schema 2 in a delayed background task.

## v1.0-dev.10.5 queue artwork pipeline

The active queue uses a separate artwork-only enrichment path so bulk cover lookup does not trigger lyrics, Wikipedia, or full artist-detail requests for every track. Queue entries are grouped by tagged artist/album or, when the album tag is absent, by their real parent folder. One representative track is matched at a high confidence threshold, and the resulting cached Cover Art Archive image is applied to the complete group on the WPF dispatcher. The workflow is cancellable, serialized through the existing MusicBrainz rate limiter, and never writes to source audio files.


## v1.0-dev.10.5 Eye Candy layer

`Themes/EyeCandy.xaml` is merged after the baseline control dictionary and overrides presentation resources without changing commands, bindings, or feature services. This keeps visual experimentation isolated from playback and data architecture. A persisted `ReduceMotion` preference suppresses nonessential hover scaling while retaining color, focus, and pressed-state feedback.


## v1.0-dev.10.5 Now Playing presentation

The persisted `IsNowPlayingExpanded` preference controls a responsive presentation layer only. Compact mode preserves the proven Library / Now Playing / Queue workspace. Expanded mode collapses the side cards at the Grid-column level and reuses the same playback, enrichment, visualizer, and session services in an artwork-led focus composition. No playback engine or metadata pipeline is duplicated.

## v1.0-dev.10.5.2 expanded stage balance

Expanded Now Playing keeps the shared `QueueView`, `SelectedQueueTrack`, and existing queue commands rather than creating a second queue model. A horizontally virtualized `ListBox` renders compact artwork cards beneath the visualizer. The same drag/drop handlers used by Compact Queue preserve one ordering source, while mouse-wheel translation provides desktop-friendly horizontal navigation.

### Responsive expanded vertical composition

The expanded workspace keeps Compact mode isolated while sizing its visualizer and horizontal queue from the current window height. `VisualizerBarViewModel.ExpandedHeight` gives the hero spectrum a larger rendering range without changing the compact spectrum. The queue remains virtualized and horizontally scrollable.


## v1.0-dev.10.5.3 expanded queue polish

Expanded mode keeps its existing visualizer contract while allocating a taller responsive stage to the horizontal queue. Card templates remain bound to the same QueueView and commands, so the visual polish does not fork queue behavior or persistence.

## v1.0-dev.10.6 supportability and hardening

The App layer now owns two presentation-only support windows: `AboutDialog` and `KeyboardShortcutsDialog`. `DiagnosticsReportBuilder` assembles a user-copyable report from public view-model state, runtime information, and `AppPaths`; it never mutates player state or reads source audio files.

Startup acquires a per-user named mutex before constructing services so only one process can own session, cache, playlist, and playback resources. `AppPaths` performs best-effort stale `.tmp` cleanup, while `FileAppLogger` rotates the active log near 4 MB and retains four timestamped archives. Startup and session-load timings are written to the log for real-world performance diagnosis.

Keyboard handling stays in `MainWindow` because it is a WPF input concern. Existing MVVM commands remain the execution path for playback, navigation, queue, playlist, and appearance actions; shortcuts do not duplicate business logic.
## v1.0-dev.10.7 typography architecture

Typography is a first-class appearance system independent from color themes. `TypographyManager` updates dynamic WPF resources for body, display, and technical font families plus a coordinated size scale. `AppSettings` persists the selected `TypographyProfile` and `TextScale`; the ViewModel exposes live selection state to Settings. The three curated profiles intentionally avoid arbitrary system-font browsing and never use Thin or Light weights.



## Motion and presentation layer

`MainWindow` owns short, non-blocking presentation animations for mode changes, album-art refreshes, insight tabs, and queue-card realization. These animations never change playback state and are skipped when `ReduceMotion` is enabled. Control depth, slider geometry, lyric surfaces, queue states, and loading overlays remain centralized in `Themes/EyeCandy.xaml`.

## Premium visual-language layer (v1.0-dev.10.9)

`Themes/PremiumFoundation.xaml` is merged after the baseline control and Eye Candy dictionaries. It intentionally overrides shared resource keys instead of duplicating bindings or rewriting workflows. Existing screens therefore inherit premium cards, navigation, buttons, transport controls, chips, theme cards, dropdowns, and menu surfaces while retaining their commands and view models.

Theme colors remain centralized in `ThemeManager`; the premium brushes derive from those dynamic color resources, allowing all six themes to drive the same component system. `VisualizerExperienceSettings` stores presentation preferences separately from the existing FFT tuning model so the future embedded Now Playing Visualizer and detached Window Visualizer can share persistent behavior without coupling rendering failures to playback.
