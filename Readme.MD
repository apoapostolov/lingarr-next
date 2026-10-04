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
on disk, and can hand the result back to Plex as the selected track. The
current release is **1.1.3**.

<p align="center">
  <img src="docs/images/hero.jpg" alt="A dim home theater with a film on the screen and a laptop showing a dark teal dashboard" width="100%" />
</p>

You run it yourself. There is no hosted translation service. Connect your own
libraries and your own providers: free web translators, DeepL, or a local or
hosted model.

Lingarr Next started from
[Lingarr](https://github.com/lingarr-translate/lingarr) and is now its own
project, maintained by Apostol Apostolov. AI-assisted changes are welcome.
Each one is still reviewed and tested before it ships.

## What's new

Current release: [1.1.3](https://github.com/apoapostolov/lingarr-next/releases/tag/1.1.3).
The full history is in [CHANGELOG.md](CHANGELOG.md).

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
  details, and compact relative completion times.

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
under **Settings → Translation → Setup**.

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

See [Settings.MD](Settings.MD) for the full environment-variable reference.

## Documentation

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
