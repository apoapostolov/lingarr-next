# Changelog

All notable changes to Lingarr Next are documented here.

Lingarr Next uses an independent version line. Its versions are not intended
to sort before or after versions published by upstream Lingarr.

## [Unreleased]

## [1.1.3] - 2026-10-04 — Imports

The daily translation check asks Plex, Radarr, and Sonarr which files are
new, and it skips a title that already has its languages.

- Lingarr does not open a folder that already has the source language and
  every target language. It opens a folder only for a new import that is
  still missing one. A full drive walk runs only when Plex, Radarr, and
  Sonarr are all unconfigured and the library disk scan switch is on.
- Translation automation runs once a day at 02:00 UTC, before Sunday
  housekeeping at 03:00 and statistics at 05:00. An existing every-3-hours
  schedule is moved to that daily time.
- Under Quality, a completed translation from a pay-per-token API shows its
  input and output tokens. Subscription and OAuth providers do not.
- The README and the app header use a transparent mark. The navy tile fills
  the image.
- The public repository is apoapostolov/lingarr-next.

## [1.1.2] - 2026-10-04 — Library

Plex can start a translation when it adds a movie or an episode, and the
scheduled walk of every folder stays off until you turn it on.

### Library disk scan

- Scheduled translation, housekeeping, and statistics no longer open every
  movie and episode folder while **Scan all folders on a schedule** is paused.
  That switch is on Settings → Automation and is paused by default. Plex,
  Radarr, and Sonarr webhooks still translate a new item. A folder that was
  already checked is skipped until its folder time changes.

### New movies from Plex

- A Plex webhook for a newly added movie or episode can start a translation.
  A movie is matched by its tmdb, imdb, or tvdb id. An episode is matched by
  the show, season, and episode number, or by the show id when the Plex title
  differs. Lingarr translates only when a source subtitle file is already
  present and a target subtitle is not. The URL is on Settings → Connections.
- Migration `M0028` adds `plex_translate_on_library_new`, on by default.
- Separate Plex webhook switches for movies and episodes are both on.
  Migration `M0030`.

## [1.1.1] - 2026-10-04 — Plex

Sign in to Plex from Settings. After a translation, Lingarr can make that
subtitle the one Plex plays.

### Plex

- Settings → Connections → Media servers can sign in with the Plex PIN page,
  or with a local server address and token. The token stays encrypted.
- Sign out stays signed out when `PLEX_TOKEN` is set on the server. A sign-in
  from the card is the connection used after that, including across restarts.
- A finished movie or episode translation can tell Plex to select the new
  subtitle for one configured language. The item is matched by file path, by
  a tmdb, imdb, or tvdb id in the file name, or by the Plex title, original
  title, and slug.
- If a refresh does not list a sidecar such as `name.bg.srt`, Lingarr uploads
  that file onto the matched item and then selects it.

### Upstream fixes

- `USE_BATCH_TRANSLATION` can be set from the environment.
- Saving an AI service no longer turns batch translation off for a service
  that supports batch.
- A translation job skips a request that is already completed or cancelled,
  and a queued duplicate is removed before resume.
- `JOB_TIMEOUT_MINUTES` sets how long a silent SQLite Hangfire job waits
  before retry. The default is 30.
- Content-API line translations strip subtitle markup when that setting is on.

### Release and compatibility notes

- Migrations `M0026` and `M0027` add the Plex connection settings and run at
  startup.
- The image line remains `lingarr-next`. This patch is tag `1.1.1`.

## [1.1.0] - 2026-08-13 — Keep House

Weekly sidecar housekeeping, Mistral, Revise with AI, a Dashboard that opens
from cache, and the portable backend fixes from upstream 1.3.0.

### Dashboard

- The Dashboard now opens instantly. The last successfully loaded statistics,
  activity, and translation history are shown immediately from a local cache,
  then refresh in the background. When fresh data arrives, only the changed
  numbers animate into place, so the page never blanks out while loading.
- Added subtle "Updating… / Updated Xm ago" indicators next to each Dashboard
  section so the refresh state is always visible without blocking the view.
- Reduced Dashboard load cost on the server. The activity summary is now
  memoized and served stale-while-revalidate, so repeat loads and tab switches
  no longer re-run the full set of translation queries every time.
- Added short-lived browser caching for the Dashboard and Statistics reads so
  repeat navigations skip the network round-trip entirely.
- Reordered the All-time Totals cards to **Files processed**, **Lines
  translated**, **Characters translated**.

### Media library

- Fixed empty, unlabeled subtitle language pills appearing in the Movies list.
  Subtitles with no resolvable language no longer render an empty badge,
  matching the behaviour already used for TV episodes.

### Library housekeeping

- Weekly job that renames orphan subtitle sidecars so they match the movie or
  episode file name (`eng`/`bul` included). Destination collisions are skipped.
- Optional weekly extract of one English text track from a video when no
  matching sidecar exists. Image-based subs are left alone. Capped per run.
- After either change, Jellyfin and Plex are asked to re-read that folder when
  a URL and token are configured. The job is on the Schedule page.

### Translation providers

- Added **Mistral AI** as a first-class provider with encrypted API key storage,
  live model discovery, instruction profiles, and fallback-chain support.

### AI revision

- Completed translations can be sent back through the first chat model in the
  chain. The job rereads source and target, rewrites only lines that change,
  and runs the existing quality checks again. The action lives on the
  translation detail page as **Revise with AI**. Scrapers such as Microsoft
  cannot revise a file on their own.

### Upstream 1.3.0 ports

- Microsoft Translator now splits lines over 1000 characters and recombines the
  translated chunks, preserving current retry, timeout, and jitter behavior.
- API key generation now runs after onboarding completes and is authorized.
  Password updates use the same hasher as user creation.
- Settings JSON now accepts numbers written as strings, which unblocks threshold
  storage.
- Plugin provider keys are registered in lowercase so lookups stay consistent.
- Webhook URLs include the configured base path.
- The batch-translation toggle follows the chain's primary provider, including
  OpenRouter, Z.ai, OpenCode Go, Qwen, and xAI.
- Local AI batch translation retries when the model returns unparsable JSON.
- PostgreSQL timestamp conversion and FluentMigrator `postgresql` conditions
  match upstream 1.3.0. SQLite rollback of the include/exclude rename is
  idempotent.

### Maintenance

- Took the safe 1.3.0 dependency train: ASP.NET/EF/Sqlite `10.0.10`, Hangfire
  `1.8.24`, Test SDK `18.8.1`, plus axios `1.19`, Vue `3.5.40`, Vite `8.1.5`,
  and vue-tsc `3.3.8`. Pinia 4, Node 26 types, Tailwind 4.3, and oxlint/oxfmt
  were left alone.

### Release and compatibility notes

- Migrations `M0024` (Mistral + revise) and `M0025` (housekeeping settings)
  apply automatically at startup.
- New settings: `subtitle_naming_enabled`, `subtitle_extract_enabled`,
  `subtitle_maintenance_schedule`, `subtitle_extract_max_per_run`. Extract
  stays off in the database default; turn it on in settings or compose.
- The server image now ships `ffmpeg` so extract can run inside the container.
- Bedroom image line remains `lingarr-next:*`. Do not point compose at
  official GHCR.

## [1.0.1] - 2026-07-29 — Moar Providers

### Translation providers

- Added **Qwen General AI** with live model discovery, instruction profiles,
  request templates, and region-aware Alibaba Cloud Model Studio endpoints.
- Added **Qwen Translation**, a separate subtitle-focused option using Qwen-MT's
  explicit source/target language controls. It intentionally does not apply
  free-form System or Context Prompt profiles.
- Added **xAI API** with encrypted API-key storage, Grok model discovery,
  instruction profiles, and fallback-chain support.
- Added an experimental **xAI SuperGrok / Premium+** option using device login
  instead of an API key. OAuth tokens are kept encrypted on the Lingarr server,
  refreshed automatically, and can be disconnected from Settings.

### Branding

- Renamed the fork to **Lingarr Next** across the application interface,
  generated Dashboard language, documentation, contributor material, support
  forms, API metadata, subtitle metadata, and provider-facing identity.

## [1.0.0] - 2026-07-29

This is the first consolidated release of **Lingarr Next**, the separate fork
maintained by Apostol Apostolov.

### Dashboard and translation confidence

- Added a redesigned operational Dashboard centered on useful, recent
  information instead of lifetime-only totals.
- Restored Lingarr Next's readable historical translation graph inside Recent
  Activity.
- Added a configurable recent-activity window, normally 48 hours.
- Added a natural-language summary of completed subtitle files, quality results,
  provider contribution, active work, fallbacks, and provider availability.
- Added input-token, output-token, and approximate-cost reporting when metered
  OpenRouter, OpenAI, DeepSeek, or Anthropic translations occur in the selected
  period.
- Added a remembered primary-language selector to Media Overview. Movie and TV
  bars now describe current subtitle coverage in that language.
- Added Provider Health with explicit operational states, persistent structured
  outcomes, last-success information, expandable diagnostics, and safe provider
  tests.
- Added observe-only subtitle quality assessments from 0 to 100, including
  versioned line findings for suspicious output, preservation, language,
  formatting, readability, and consistency problems.
- Integrated quality results below Recent Activity and into completed
  translation status without blocking or deleting output.

### Translation providers and fallback chains

- Added first-class OpenRouter support with live model discovery and
  provider-reported pricing where available.
- Made `openrouter/free` the default and first model, followed by
  `openrouter/auto`, then the remaining model catalogue.
- Added Z.ai support for the GLM Coding Plan endpoint and its supported GLM
  models.
- Added OpenCode Go support and improved DeepSeek model handling.
- Added a cached model catalogue with manual refresh.
- Replaced provider-only fallback configuration with ordered provider-and-model
  rows.
- Allowed the same provider to appear more than once with different models.
- Preserved compatibility with legacy plain-string and string-array
  `SERVICE_TYPE` settings.
- Added provider-specific request timeouts, including a longer Microsoft default
  for slow free-translation requests.

### AI instruction profiles

- Added separate System Prompt and Context Prompt libraries.
- Added named drafts, immutable published versions, activation, rename,
  duplication, deletion, and version history.
- Added per-provider-row profile assignment with inheritance from active
  defaults.
- Added prompt-capability metadata to plugin manifests.
- Added a comprehensive example translation instruction set covering role,
  priorities, dialogue tone, profanity, forms of address, glossary, captions,
  numbers, readability, ambiguity, and final checks.
- Preserved existing prompt placeholders and added clearer guidance for
  dialogue-context tags.

### Settings and interface

- Reorganized Settings into Connections, Translation, Automation, System, and
  Plugins while preserving Lingarr Next's existing cards and control vocabulary.
- Added local Translation tabs for Setup, Subtitles, Prompts, and Advanced.
- Placed Translation Services before Languages and clarified ordered fallback
  editing.
- Added shared tab and settings-shell components for consistent responsive
  navigation.
- Removed anonymous telemetry from Settings, onboarding, scheduled jobs,
  endpoints, persistence, and client services.
- Pointed update checks to the Apostol Apostolov fork.
- Improved toast visibility with the theme's Development-pill styling, a
  restrained spring animation, and protection against notification floods.
- Fixed stale dynamically imported pages after deployments by using safe update
  and asset-path behavior.

### Translation list and detail views

- Added the episode name as a smaller second line beneath TV episode filenames.
- Replaced source and target pills with quieter rounded-square language markers.
- Moved completed quality into Status as compact `Quality: XX%` text.
- Centered Source, Target, Status, Progress, and Completed presentation where
  appropriate.
- Replaced strict completed dates with compact relative times such as `30 m` and
  `7 d`.
- Refined card, badge, and progress colors to remain compatible with every
  Lingarr Next theme.
- Added optional subtitle paths to content-translation API requests and made
  content titles optional.
- Hardened cross-platform basename handling in the Translations list.

### Reliability and operations

- Reworked subtitle discovery to avoid unbounded recursive scans, exclude
  trailer folders, and continue supporting conventional subtitle folders.
- Added paged automated-library processing with a durable cursor so large
  libraries make steady progress across runs and restarts.
- Added Hangfire SQLite startup recovery and periodic WAL checkpoint
  maintenance.
- Added transient retry with jitter for Microsoft/GTranslate requests.
- Added early rejection of invalid Sonarr and Radarr identifiers.
- Reduced noisy routine sync and already-translated messages to debug-level
  logging.
- Added safe provider operational retention and health reconciliation.
- Fixed fresh MySQL migrations for provider settings and imported prompt
  profiles by using database-correct quoting for the reserved settings key.
- Kept API keys encrypted and excluded subtitle text, prompts, paths, titles,
  and credentials from provider-health and usage records.

### Documentation and testing

- Added fork-specific architecture, testing, reliability, settings, provider,
  fallback-chain, confidence, Dashboard, and prompt-profile documentation.
- Added a live API smoke suite for provider manifests, free translation paths,
  model ordering, content requests, update checks, and health endpoints.
- Added regression coverage for translation chains, fallbacks, model catalogues,
  provider health, quality scoring, prompt profiles, automation, subtitle
  enumeration, Dashboard activity, LLM pricing, and database migrations.
- Added operator guidance that keeps Bedroom deployments on fork-built images
  and treats upstream as a selective import source.

### Release and compatibility notes

- Reset the fork's public release history to the independent `1.0.0` baseline.
- Moved the supported public image to
  `ghcr.io/apoapostolov/lingarr-next:1.0.0`.
- Database migrations through the LLM-usage and Dashboard-language additions are
  applied automatically at startup.
- Back up the application config and database before switching from an upstream
  image or attempting a downgrade.

[1.1.3]: https://github.com/apoapostolov/lingarr-next/releases/tag/1.1.3
[1.1.2]: https://github.com/apoapostolov/lingarr-next/releases/tag/1.1.2
[1.1.1]: https://github.com/apoapostolov/lingarr-next/releases/tag/1.1.1
[1.1.0]: https://github.com/apoapostolov/lingarr-next/releases/tag/1.1.0
[1.0.0]: https://github.com/apoapostolov/lingarr-next/releases/tag/1.0.0
