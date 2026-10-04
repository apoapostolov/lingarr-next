# Lingarr Next architecture

This document describes **apoapostolov/lingarr-next** as deployed on Bedroom (`lingarr-next` image). Upstream conceptual design is the same; Bedroom-specific behaviour is called out explicitly.

## 1. What Lingarr Next is

Lingarr Next is a self-hosted **subtitle translation** application. It:

1. Syncs media metadata from **Sonarr** / **Radarr** (optional path mapping).
2. Discovers subtitle files next to media.
3. Translates subtitles via a configurable **provider chain** (scrapers + AI).
4. Writes translated subtitle files and tracks jobs in a DB + SignalR UI.

UI: Vue 3 SPA (`Lingarr.Client`) served by ASP.NET Core (`Lingarr.Server`).

## 2. Solution layout

```
Lingarr.slnx
├── Lingarr.Client/          # Vue 3 + Pinia + Tailwind SPA
├── Lingarr.Server/          # ASP.NET Core host, API, jobs, translation services
├── Lingarr.Core/            # EF entities, DbContext, SettingKeys, version
├── Lingarr.Contracts/       # Shared DTOs, plugin contracts, ITranslationService
├── Lingarr.Migrations/      # FluentMigrator schema/settings seeds
├── Lingarr.Server.Tests/    # xUnit unit + regression tests
├── Lingarr.Migrations.Tests/
├── docs/                    # Architecture, testing, Bedroom proposals (this tree)
├── tests/smoke/             # Live API smoke script
└── AGENTS.md                # Fork operating rules for humans/agents
```

| Project | Responsibility |
|---------|----------------|
| **Contracts** | Stable API shapes: `ITranslationService`, `ModelsResponse`, batch types, plugin field types |
| **Core** | `LingarrDbContext`, entities (`Movie`, `Show`, `TranslationRequest`, `Setting`), `SettingKeys` |
| **Migrations** | Versioned DB changes + default settings rows |
| **Server** | HTTP API, Hangfire jobs, translation providers, encryption, SignalR hubs |
| **Client** | Settings, media browser, translation queue UI |

## 3. Runtime deployment (Bedroom)

```
Browser → http://host:9876
            │
            ▼
    Docker: lingarr-next:latest
            │  image built from this fork (never official GHCR)
            ├── Kestrel (API + SPA wwwroot)
            ├── SQLite (default) under mounted config volume
            └── Hangfire (background translation / automation)
```

- **Compose image:** `lingarr-next:latest`, `pull_policy: never`
- **Build:** `Lingarr.Server/Dockerfile` (multi-stage: Node client → .NET publish)
- **Fork ops:** Hermes skill `lingarr-local` + `AGENTS.md`
- **Deployment-safe client loading:** the HTML shell is never cached, hashed
  assets are immutable, missing old asset hashes return 404, and the client
  performs one guarded refresh if an open tab crosses a deployment.

## 4. Request / data flow

### 4.1 Settings

```
Client (Pinia setting store)
  → POST /api/setting | /api/setting/encrypted
  → SettingService (EF Settings table + memory cache)
  → SettingChangedListener (may reschedule Hangfire jobs)
```

- Plain settings: `service_type`, schedules, language lists, model ids, endpoints.
- Encrypted settings: API keys (`*_api_key`) via `IEncryptionService`.
- **Important:** `SetSetting` updates existing rows only — migrations must seed keys.

### 4.2 Translation chain (`service_type`)

Canonical format (Bedroom):

```json
[
  {
    "id": "row-1",
    "provider": "openrouter",
    "model": "openrouter/free",
    "systemPromptProfileId": 3,
    "contextPromptProfileId": 7
  },
  { "id": "row-2", "provider": "microsoft" }
]
```

Legacy accepted:

- `"microsoft"`
- `["microsoft","deepseek"]`

Parsed by `TranslationChain.Parse` → `ITranslationServiceFactory.CreateTranslationServices(entries)`.

Per-row `model` is applied via `IModelOverridable.OverrideModel` before translate.
Stable row ids keep model and prompt-profile assignments attached through reorder.
Built-in AI rows can override the active System and Context Prompt profiles;
traditional translators never receive prompt content.

### 4.3 Manual / API line translate

```
POST /api/translate/line
  → TranslationChain.Parse(service_type)
  → CreateTranslationServices(chain)
  → SubtitleTranslationService.TranslateSubtitleLine
       for each candidate in order:
         GetLanguagePair → TranslateAsync
         on failure → next candidate
```

### 4.4 File / job translate

```
UI or automation
  → TranslationRequest row (InProgress)
  → Hangfire TranslationJob
       read subtitles → SubtitleTranslationService.TranslateSubtitles
       write output path → statistics → complete/fail events
  → SignalR progress to UI
```

### 4.5 Content translate (integrators, e.g. Bazarr)

```
POST /api/translate/content
  → TranslationRequestService.TranslateContentAsync
  → same service chain + batch or per-line path
  → optional subtitle path fields (Bedroom feature) for Translations UI
```

## 5. Translation providers

### 5.1 Factory

`TranslationFactory` maps provider id → service instance:

| Id | Type | Notes |
|----|------|--------|
| `microsoft`, `google`, `bing`, `yandex` | GTranslate scrapers | Free, no model |
| `libretranslate`, `deepl` | Self-host / commercial | |
| `openai`, `anthropic`, `gemini`, `deepseek`, `localai` | AI | Model catalogue via plugin |
| `openrouter` | AI gateway | **free metamodel first** in list |
| `zai` | **GLM Coding Plan** | Base `…/api/coding/paas/v4` (not general API) |
| `opencode-go` | AI gateway | Implemented; may be unsubscribed in Bedroom |

Plugins expose manifests (`IPluginManifest`) for UI fields and `/api/plugin/{id}/models`.

### 5.2 Model catalogue cache

`ModelCatalogService` + `GET /api/plugin/{provider}/models?refresh=true`

- In-memory cache ~6h
- `refresh=true` bypasses TTL
- On fetch failure, may return stale list

### 5.3 Fallback semantics

`SubtitleTranslationService` builds ordered candidates that support the language pair, then tries Translate until one succeeds. Chain order is primary preference; language tier can still reorder within matching logic.

## 6. Client architecture

```
Lingarr.Client/src
├── pages/                 # route-level views
├── components/
│   ├── features/settings/ # ServicesSettings (chain UI), Integration, …
│   ├── common/            # SelectComponent, InputComponent, toasts
│   └── layout/            # AsideNavigation (Development pill)
├── store/                 # Pinia: settings, instance, translate
├── services/              # HTTP wrappers
└── assets/style.css       # CSS variables: primary/secondary/accent themes
```

### 6.1 Settings navigation

The Bedroom settings rail has five stable destinations:

1. **Connections** — Media servers and Path mapping
2. **Translation** — Setup, Subtitles, Prompts, and Advanced
3. **Automation**
4. **System** — Access, Tasks, and Logs
5. **Plugins**

Connections, Translation, and System use route-backed local tabs. Legacy settings paths redirect to
the equivalent new route so saved links continue to work.

Most valid setting fields retain Lingarr Next's immediate-save behavior. Compound Path mapping edits keep
their explicit save action.

### 6.2 Translation Setup

Each translation-service chain row is self-contained:

1. Provider select  
2. Model select (if multi-model)  
3. System and Context Prompt profile selectors (built-in AI only)
4. API key (if provider needs one)
5. Reorder / delete (delete only fallbacks)

No shared “last clicked” credentials panel.

## 7. Persistence

| Store | Contents |
|-------|----------|
| `settings` | Key/value config (incl. encrypted blobs for secrets) |
| `movies` / `shows` / seasons / episodes | Media index from *arr |
| `path_mappings` | Host ↔ container path rewrites |
| `translation_requests` (+ lines, events) | Jobs and progress |
| `statistics` / `daily_statistics` | Counters |
| `users` | Auth when enabled |

Default Bedroom DB: **SQLite**. MySQL/Postgres also supported via env.

## 8. Background work

Hangfire jobs (representative):

- Automated translation scan
- Translation job execution
- Media sync from Sonarr/Radarr

Schedules come from settings; changes fire `SettingChangedListener`.

The Bedroom fork does not include anonymous usage telemetry or its former scheduled submission job.
The separate version check reads public release/tag metadata from GitHub repository
`apoapostolov/lingarr-next`; it does not contact the upstream Lingarr API.

## 9. Auth

Optional API key / JWT (`AuthController`, `AuthService`). Local Bedroom often runs open on LAN; production should enable auth.

## 10. Cross-cutting concerns

| Concern | Implementation |
|---------|----------------|
| Encryption | `EncryptionService` for secret settings |
| Progress | `ProgressService` + SignalR hubs |
| Path mapping | `PathConversionService` / mapping API |
| Subtitles | Parsers/writers under `Services/Subtitle` (SRT, SSA/ASS, …) |
| Version | `/api/version` — Bedroom builds stamp `0.0.0-bedroom.<sha>` |
| Provider health | `/api/provider-health` — structured outcomes, persisted snapshots, and safe provider probes |
| Subtitle quality | `/api/translation-request/{id}/quality` — deterministic scores and line findings |
| Recent activity | `/api/dashboard/activity` — bounded recent metrics and deterministic progress prose |
| Prompt profiles | `/api/instruction-profile` — System/Context drafts, immutable versions, defaults, and history |

## 11. Bedroom-specific deltas vs upstream

1. Self-built image from fork; official GHCR not used for deploy.  
2. Rich `service_type` objects with per-row models.  
3. OpenRouter / Z.ai Coding Plan / OpenCode Go providers.  
4. OpenRouter dropdown pins `openrouter/free` first.  
5. Z.ai forced onto Coding Plan endpoint (rewrites general `paas/v4`).  
6. Content API optional subtitle paths + Translations UI basename.  
7. Theme-aligned Development badge and toast colours.  
8. Services UI: stacked provider/model/key per row.
9. Versioned System and Context Prompt libraries with per-AI-row assignment.
10. Provider health, observe-only subtitle quality, and a human-readable recent-work Dashboard.

Official upstream remains a **source for selective imports only**: https://github.com/lingarr-translate/lingarr

## 12. Extension points

- New AI provider: SettingKeys + service class + plugin manifest + factory case + migration seed empty keys + client `SERVICE_TYPE` / encrypted map.  
- New settings UI card: `components/features/settings/*` + router.  
- New job: Hangfire registration + schedule setting keys.

## 13. Related docs

- [testing.md](./testing.md)  
- [ai-providers-model-fallback-chain.md](./ai-providers-model-fallback-chain.md)  
- [AGENTS.md](../AGENTS.md)
