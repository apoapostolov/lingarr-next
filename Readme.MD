<div align="center">

<img src="docs/images/mark.png" alt="" width="256" />

# Lingarr Next

Translate the subtitles you already have. Keep the drive quiet.

[![Version](https://img.shields.io/github/v/release/apoapostolov/lingarr-next?label=release)](https://github.com/apoapostolov/lingarr-next/releases)
[![Container](https://img.shields.io/badge/container-ghcr.io%2Fapoapostolov%2Flingarr--next-2496ED?logo=docker&logoColor=white)](https://github.com/apoapostolov/lingarr-next/pkgs/container/lingarr-next)
[![CI](https://github.com/apoapostolov/lingarr-next/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/apoapostolov/lingarr-next/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg)](LICENSE)

</div>

---

Lingarr Next is a self-hosted subtitle manager for movies and TV. It watches
Sonarr, Radarr, and Plex for new files, translates a subtitle that is already
on disk, and can hand the result back to Plex as the selected track.

<p align="center">
  <img src="docs/images/hero-cinema.jpg" alt="A dim home theater. The film on the wall has a subtitle that is changing language, and the laptop on the table is closed." width="100%" />
</p>

You run it yourself. There is no hosted translation service. Connect your own
libraries and your own providers: free web translators, DeepL, or a local or
hosted model.

Lingarr Next started from
[Lingarr](https://github.com/lingarr-translate/lingarr) and is now its own
project, maintained by Apostol Apostolov. AI-assisted changes are welcome.
Each one is still reviewed and tested before it ships.

## What's new

Current release: [1.1.6](https://github.com/apoapostolov/lingarr-next/releases/tag/1.1.6).
The full history is in [CHANGELOG.md](CHANGELOG.md).

### 1.1.6 — Infinitely Extendable (2026-10-05)

- A plugin is a DLL. Set `PLUGINS_PATH` to its folder and restart. Every
  plugin starts off.
- Settings → Plugins shows each plugin as its own card. The switch is in the
  upper right. An off card is dim, and each card keeps its own height.
- A plugin can add a tab and several cards, change a finished subtitle, run
  one external command, supply a source subtitle, accept a webhook, send a
  notice when a translation ends, and run a scheduled task.
- Plex, Jellyfin, and Emby stay built in. A plugin media server runs after
  them.
- The developer guide is
  [Developer API documentation](Lingarr.Docs/developers/Plugins.md).
  `samples/StyleSample` is a working set, all off until you enable one.
- International quotes follows the target language. Its dropdown can
  overwrite that country with another dialogue style.
- A plugin's failure policy says whether Lingarr continues after an error,
  stops that file, or fails the translation and keeps the old file.
- Strip HTML is on. Tags such as font, bold, and italics are removed before
  translation.
- Translation services and Provider Health list only translators.
- A provider cancel is tried again, 5 times by default, 2 hours apart. The
  status reads Cancelled (1/5). Line retries stay inside the job and wait
  seconds.
- Settings → Translation → Setup has one Provider panel. Drag a row to
  change the order. The selected row's address, key, and account are below.
  Microsoft Translate has no fields of its own.
- Open Translation Setup scrolls to that provider.
- The main chart hover shows the day, that day's count, and the average.
- Translation detail shows Revise with AI and Subtitle Quality side by side
  on a wide screen. Settings and provider guidance was tightened.
- Settings → Translation → Advanced can use TypeSafe Jev or OpenAI Luna
  Decisions. Existing installs stay on Jev, and both checks stay off.
- A subtitle with no language code is unknown. Signup stays closed once
  authentication is on, and onboarding cannot be completed twice.

### 1.1.5 — All The Subs (2026-10-05)

- An English picture or caption track inside a video can become a text
  subtitle. Picture conversion waits 72 hours and is the last resort.
- Bazarr can supply a missing source subtitle. The switch starts off. A miss
  is tried again for up to 7 days. A downloaded subtitle can replace one
  Lingarr made by OCR.
- Jellyfin and Emby can queue a new movie or episode.
- System logs open on the newest lines, load older lines as you scroll up,
  and give a next step for a warning or an error.

### 1.1.4 — Resume and Classify (2026-10-05)

- A cancelled translation can continue when the saved lines still score at
  least 90.
- Under Cancelled, the list shows progress and quality.
- TypeSafe Jev can skip sound cues and credits, and it can drop a result that
  is not a translation. Both uses start off.

### 1.1.3 — Imports (2026-10-04)

- The daily check asks Plex, Radarr, and Sonarr what was imported.
- A title that already has the source language and every target language is
  left alone. The folder is not opened.
- A new import that is still missing a language is the only folder opened.
- A full drive walk runs only when Plex, Radarr, and Sonarr are all missing
  and **Scan all folders on a schedule** is on.
- Translation automation runs once a day at 02:00 UTC, before Sunday
  housekeeping and the daily statistics job.

### 1.1.2 — Library (2026-10-04)

- Plex `library.new` can start a translation for a movie or an episode.
- Movies match by tmdb, imdb, or tvdb id. Episodes match by show, season, and
  episode number, or by the show id when the Plex title differs.
- Movies and episodes each have their own webhook switch. Both start on.
- Scheduled translation, housekeeping, and statistics stay off the disk while
  the library scan switch is paused. That switch starts paused.

### 1.1.1 — Plex (2026-10-04)

- Sign in from Settings with a Plex PIN, or with a server address and token.
  The token stays encrypted. Sign-out stays signed out when a token is also
  set in the environment.
- After a translation, Lingarr can select that subtitle in Plex for one
  language. Matching uses the file path, an external id, or the Plex title,
  original title, and slug.
- If a refresh misses a sidecar such as `name.bg.srt`, Lingarr uploads the
  file and then selects it.
- Batch translation can be set from the environment, and saving an AI service
  no longer turns batch off for a provider that supports it.
- `JOB_TIMEOUT_MINUTES` controls how long a silent SQLite Hangfire job waits.
  The default is 30.

### 1.1.0 — Keep House (2026-08-13)

- The Dashboard opens from a local cache, then refreshes in the background.
- A weekly job renames orphan sidecars so they match the video file, and can
  extract one English text track when no sidecar exists. The server image
  includes ffmpeg.
- Completed translations can be sent back through **Revise with AI**.
- Mistral joins the provider list, with model discovery and instruction
  profiles.
- Portable fixes from upstream 1.3.0: long Microsoft lines are split, webhook
  URLs keep the base path, and the safe dependency updates are included.

### 1.0.1 — Moar Providers (2026-07-29)

- Qwen General AI and Qwen Translation (Qwen-MT) are separate providers.
- xAI is available as an API-key provider and, experimentally, as SuperGrok /
  Premium+ device login. Those tokens stay encrypted.
- The fork name **Lingarr Next** is used across the app, docs, and provider
  identity.

### 1.0.0 (2026-07-29)

The first Lingarr Next release. It is its own version line, not an upstream
1.0.

- Dashboard for language coverage, provider health, recent work, quality, and
  metered model usage.
- An ordered provider-and-model fallback chain.
- OpenRouter, Z.ai Coding Plan, OpenCode Go, and a stronger DeepSeek path.
- Observe-only quality scores, with findings you can inspect.
- Versioned System and Context instruction profiles, assignable per provider.
- Settings grouped into Connections, Translation, Automation, System, and
  Plugins. Anonymous telemetry is gone. Update checks point at this fork.

<p align="center">
  <img src="docs/images/dashboard-overview.png" alt="Lingarr Next Dashboard showing language coverage, provider health, and recent translation activity" width="100%" />
</p>

## Features

### Operational Dashboard

- **Language coverage** shows the share of movies and TV episodes that already
  have subtitles in your chosen primary language. The selection is remembered.
- **Provider Health** lists every translator as Healthy, Not checked, Needs
  attention, Recently unavailable, Unavailable, or Not configured, with
  expandable diagnostics and safe provider tests.
- **Recent Activity** retains the readable historical graph while adding
  completed-file counts, quality results, active work, fallback recoveries, and
  a configurable time window.
- **Human-readable progress** summarizes completed subtitle files and the
  provider responsible for them without exposing subtitle text.
- **Metered LLM usage** reports input tokens, output tokens, and an approximate
  cost when OpenRouter, OpenAI, DeepSeek, or Anthropic handled translations.

### Translation workflow and quality

- Automatic discovery and translation for connected Sonarr and Radarr
  libraries.
- Manual and API-triggered subtitle translation with optional explicit subtitle
  paths.
- Ordered provider fallbacks keep work moving when an earlier translator fails.
- Completed subtitles receive a deterministic score from 0 to 100.
- Quality findings detect suspicious output, damaged markup, missing content,
  script mismatches, poor readability, and other translation risks.
- Quality is currently **observe-only**: it helps you review translations but
  does not silently reject or delete a completed file.
- The Translations list shows source, target, status, quality, progress, episode
  details, and compact relative completion times. An enabled plugin can add a
  short badge on a row, such as `ocr`.
- An English picture subtitle or closed caption inside a video can become a
  text file before translation. Picture conversion waits 72 hours and is the
  last resort. An OCR file keeps an `ocr` tag in the name.
- When no source subtitle is on disk, Lingarr can ask Bazarr. That switch
  starts off. A miss is tried again for up to 7 days, and a downloaded file
  can replace one Lingarr made by OCR.

### Translation providers

| Category | Providers |
| --- | --- |
| Free / web translators | Microsoft Translate, Google Translate, Bing Translate, Yandex Translate |
| Hosted translation APIs | DeepL, LibreTranslate |
| AI providers | OpenRouter, OpenAI, Anthropic, DeepSeek, Google Gemini, Qwen General AI, Qwen Translation, Z.ai Coding Plan, OpenCode Go, xAI API, xAI SuperGrok / Premium+ |
| Self-hosted AI | LocalAI, Ollama, and compatible OpenAI-style endpoints |

AI services expose model selectors where supported. A translation chain may use
the same provider more than once with different models. OpenRouter keeps
`openrouter/free` first as the Lingarr Next default for inexpensive bulk work, with
`openrouter/auto` second and the remaining catalogue after it.

Qwen is offered in two forms: general Qwen models support Lingarr instruction
profiles, while Qwen Translation uses Qwen-MT's purpose-built language controls.
xAI is likewise offered as the conventional developer API and as an experimental
device-login connection for SuperGrok / Premium+ accounts.

### Instruction profiles

- Separate **System Prompt** and **Context Prompt** libraries.
- Create, rename, duplicate, version, activate, and delete profiles.
- Assign profiles to individual AI provider rows or inherit the active default.
- Built-in examples include translation role, tone, profanity, names, glossary,
  captions, readability, ambiguity, and final-output rules.
- Existing prompt placeholders remain available, including source/target
  language and dialogue-context tags.

### Settings and operator experience

- Settings are grouped into **Connections**, **Translation**, **Automation**,
  **System**, and **Plugins**.
- Plugins load from `PLUGINS_PATH`. Each plugin is its own card. The switch
  in the upper right turns it on. An off card is dim. A plugin can register
  its own tab and place more than one card on that tab.
- Jellyfin Item Added and Emby library.new queue a new movie or episode the
  same way a Plex library.new event does. Radarr and Sonarr webhooks stay.
- System logs open on the newest lines. Older lines load as you scroll up. A
  warning or error includes a short next step.
- Translation has local **Setup**, **Subtitles**, **Prompts**, and **Advanced**
  tabs.
- Translation Services are placed before Languages and retain the familiar
  Lingarr Next card and field vocabulary.
- Save notifications use a visible theme-aligned toast with restrained spring
  motion and rate limiting.
- Anonymous telemetry and its onboarding step are completely removed.
- Update checks use the Apostol Apostolov fork rather than the upstream
  repository.

<p align="center">
  <img src="docs/images/translation-settings.png" alt="Lingarr Next Translation settings with local tabs, provider fallback chain, language selection, and provider health" width="100%" />
</p>

### Reliability

- Subtitle discovery avoids unbounded recursive scans, ignores trailer folders,
  and still finds conventional subtitle directories.
- The daily translation check uses Plex, Radarr, and Sonarr. It does not walk
  every folder. A full scan is the fallback when those three are missing and
  the library scan switch is on.
- Hangfire SQLite receives startup recovery and periodic WAL checkpoint
  maintenance.
- Microsoft/GTranslate requests use longer provider-specific timeouts and
  transient retry with jitter.
- Invalid Sonarr/Radarr identifiers are rejected early and routine skip messages
  no longer dominate logs.
- API keys remain encrypted settings; provider-health and usage records never
  store subtitle text, prompts, credentials, or media paths.

## Quick start with Docker Compose

The simplest installation uses SQLite and the published multi-architecture
container:

```yaml
services:
  lingarr:
    image: ghcr.io/apoapostolov/lingarr-next:latest
    container_name: lingarr
    restart: unless-stopped
    ports:
      - "9876:9876"
    environment:
      ASPNETCORE_URLS: http://+:9876
      DB_CONNECTION: sqlite
      SERVICE_TYPE: microsoft
      MAX_CONCURRENT_JOBS: 1
    volumes:
      - ./config:/app/config
      - /path/to/media/movies:/movies
      - /path/to/media/tv:/tv
```

Start the service:

```bash
docker compose up -d
```

Open `http://localhost:9876`, connect Sonarr and/or Radarr under
**Settings → Connections**, configure paths, then choose translation providers
under **Settings → Translation → Setup**. To load plugins, set `PLUGINS_PATH`
and restart. They stay off until you enable each card.

MySQL and PostgreSQL remain supported for larger or existing installations.
Docker secrets can be supplied by using the `_FILE` form of supported
environment variables. `JOB_TIMEOUT_MINUTES` sets how long a silent SQLite
Hangfire job waits before it is retried. The default is 30.

## Updating and migration

Lingarr Next begins its own version line at **1.0.0**. That number is
independent from upstream Lingarr versions and should not be compared
numerically with an upstream `1.x` tag.

Before changing from upstream Lingarr or an older fork build:

1. Back up `/app/config` and your application database.
2. Review [CHANGELOG.md](CHANGELOG.md), especially migrations and provider
   configuration.
3. Change the image to `ghcr.io/apoapostolov/lingarr-next:latest`.
4. Start the container and allow the automatic database migrations to finish.
5. Confirm the translation chain, paths, prompt profiles, and provider health
   before starting a large automated run.

Legacy plain-string and string-array `SERVICE_TYPE` values remain accepted and
are normalized into the richer provider/model chain format.

## Configuration

Common environment variables:

| Variable | Purpose |
| --- | --- |
| `ASPNETCORE_URLS` | Internal listen URL; normally `http://+:9876` |
| `DB_CONNECTION` | `sqlite`, `mysql`, or `postgresql` |
| `SQLITE_DB_PATH` | Optional application SQLite path |
| `DB_HANGFIRE_SQLITE_PATH` | Optional Hangfire SQLite path |
| `MAX_CONCURRENT_JOBS` | Number of translation workers |
| `SERVICE_TYPE` | One provider or an ordered JSON provider/model chain |
| `SOURCE_LANGUAGES` | Minified JSON list of allowed source languages |
| `TARGET_LANGUAGES` | Minified JSON list of target languages |
| `RADARR_URL` / `RADARR_API_KEY` | Radarr connection |
| `SONARR_URL` / `SONARR_API_KEY` | Sonarr connection |
| `AUTH_ENABLED` | Enable Lingarr Next authentication |
| `BASE_PATH` | Optional reverse-proxy path prefix |
| `PLUGINS_PATH` | Folder of plugin DLLs. Empty disables plugins |

See [Settings.MD](Settings.MD) for the full environment-variable reference.

## Documentation

- [Developer API documentation](Lingarr.Docs/developers/Plugins.md)
- [Documentation index](docs/README.md)
- [Architecture and request flows](docs/architecture.md)
- [Testing and live smoke checks](docs/testing.md)
- [AI providers, model selection, and fallback chains](docs/ai-providers-model-fallback-chain.md)
- [Provider health, subtitle quality, Dashboard, and instruction profiles](docs/proposals/translation-confidence-and-instruction-profiles.md)
- [Settings information architecture](docs/proposals/settings-page-ia-and-qol.md)
- [Current AI-assisted development plan](DEVELOPMENT_PLAN.md)
- [API documentation sources](Lingarr.Docs/)

## Development and contributing

Contributions, bug reports, and carefully reviewed AI-assisted work are welcome.
Contributors must understand and be able to explain submitted changes; low-effort
generated output is not accepted merely because AI use is allowed.

Start with [CONTRIBUTING.md](CONTRIBUTING.md), [AGENTS.md](AGENTS.md), and the
[testing guide](docs/testing.md).

## Upstream and credits

Lingarr Next is built on the work of the
[Lingarr project](https://github.com/lingarr-translate/lingarr) and its
contributors. Upstream remains a source for selective, reviewed imports; this
fork has its own roadmap, releases, container images, update checks, and support
expectations.

Additional project credits:

- Icons: [Lucide](https://lucide.dev/icons)
- Subtitle parsing: [SubtitlesParser](https://github.com/AlexPoint/SubtitlesParser)
- Translation support: LibreTranslate, GTranslate, and the provider projects
  listed above

## License

GNU Affero General Public License v3.0. See [LICENSE](LICENSE).
