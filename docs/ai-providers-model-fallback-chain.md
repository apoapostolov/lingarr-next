# Proposal: AI providers, model picker, and provider+model fallback chain

**Status:** Implemented on the Lingarr Next fork (iterate in-branch)
**Branch:** `next`
**Deploy:** build `lingarr-next` from `next`
**Official reference (import only):** https://github.com/lingarr-translate/lingarr  
**Lingarr Next:** https://github.com/apoapostolov/lingarr-next
**Agent guide:** [`AGENTS.md`](../AGENTS.md) — keep this proposal and AGENTS.md updated together

## Goals

1. Full first-class providers: **OpenRouter**, **Z.ai (GLM)**, **DeepSeek**,
   **OpenCode Go**, **Qwen**, **xAI**, and **Mistral**
2. **Cached model lists** with refresh
3. **Model dropdown** between provider selector and API key fields
4. **Fallback chain** as ordered **provider + model** rows (+ add, − remove, reorder), not provider-only

## Personas

- Frontend Engineer + UI/UX Designer (settings IA, chain editor, model select)
- Full-stack for factory, parse migration, catalog cache, OpenAI-compat services

## Design decisions

1. **Single source of truth:** extend `service_type` to a JSON array of objects; migrate legacy string arrays on read/write.
2. **Allow same provider twice** with different models (required for useful AI fallbacks).
3. **Services page = two cards:** Primary + Fallback chain (dedicated Fallbacks child route optional later).
4. **Model row** only for multi-model / AI providers; hidden for NMT scrapers (microsoft/google/…).
5. **Chain row model overrides** global `*_model` setting at translate time.
6. **Shared OpenAI-compatible base** where practical (OpenRouter / Z.ai / OpenCode Go / DeepSeek patterns).
7. **Model catalog cache** server-side (memory), TTL ~6h, `?refresh=true` bypass.
8. **Docker:** only fork-built `lingarr-next` — never official GHCR for Bedroom.
9. **OpenRouter default metamodel:** `openrouter/free` is first in the model dropdown and the preferred Bedroom default for bulk / low-quality-OK subtitle work (free router). `openrouter/auto` is second. Paid catalogue models follow. Always inject `openrouter/free` if the API catalogue omits it.
10. **Provider-specific request timeouts:** each provider reads `<provider>_request_timeout`, with the legacy global `request_timeout` retained as a fallback. Microsoft defaults to 15 minutes because its free GTranslate endpoint has repeatedly exceeded the former 5-minute limit; other built-ins default to 5 minutes.

## `service_type` schema

Legacy (still accepted):

```json
["microsoft", "deepseek"]
```

Target:

```json
[
  {
    "id": "row-1",
    "provider": "openrouter",
    "model": "openrouter/free",
    "systemPromptProfileId": 3,
    "contextPromptProfileId": 7
  },
  { "id": "row-2", "provider": "deepseek", "model": "deepseek-chat" },
  { "id": "row-3", "provider": "microsoft" }
]
```

Plain string `microsoft` (Bedroom compose env) still parses as a one-entry chain.
Stable ids are generated when rich rows are normalized. Prompt profile fields are
optional: a built-in AI row without them inherits the active System and Context
defaults. Non-AI rows ignore and do not expose these fields.

## Providers

| Id | Notes |
|----|--------|
| `deepseek` | Existing + model override + catalog cache |
| `openrouter` | OpenAI-compat; **dropdown order: free → auto → rest**; seed model `openrouter/free` |
| `zai` | **GLM Coding Plan** only — `https://api.z.ai/api/coding/paas/v4`; models `glm-5.2` / `glm-5-turbo` / `glm-4.7` (not general `paas/v4`) |
| `opencode-go` | OpenAI-compat; implement even if Bedroom subscription is inactive |
| `qwen` | General Alibaba Cloud Model Studio chat models; live catalogue, request templates, and instruction profiles |
| `qwen-mt` | Purpose-built Qwen-MT translation; explicit source/target language codes and no free-form prompt profiles |
| `xai` | Official xAI developer API using an encrypted API key |
| `xai-oauth` | Experimental SuperGrok / Premium+ device login; encrypted server-side access/refresh tokens |
| `mistral` | Official Mistral API; live catalogue, instruction profiles, fallback-chain support |

## OpenRouter model UX (locked)

```
Model [ openrouter/free • Free metamodel (default for bulk) ▾ ]  ← position 0
      [ openrouter/auto • Auto router                       ]
      [ … paid / other catalogue …                          ]
```

- Use free for low-quality / bulk path when cost matters.
- Higher quality → pick a paid model or chain free → paid fallback later if desired.

## UI layout

```
Primary service
  Provider [ … ▾ ]
  Model    [ … ▾ ] [↻]     ← between provider and API (AI only)
  System prompt  [ Use default ▾ ]  ← AI only
  Context prompt [ Use default ▾ ]  ← AI only
  API key  [ … ]
  (extra fields…)

Fallback chain
  #1 Provider [ … ] Model [ … ] ↑ ↓ ✕
  #2 …
  [ + Add fallback ]
```

## Implementation status

| Phase | Scope | Status |
|-------|--------|--------|
| P0 | Parse/normalize rich chain; factory + model override; allow duplicate providers | Done |
| P1 | Model catalog cache; Services UI primary+fallbacks with model column | Done |
| P2 | OpenRouter port + **free-first ordering** | Done |
| P3 | Z.ai + OpenCode Go | Done (OpenCode Go untested if unsubscribed) |
| P4 | Polish empty states, automation warnings, docs/AGENTS | In progress |
| P5 | Provider-specific request timeout settings; Microsoft 15-minute default | Done |
| P6 | Qwen general + Qwen-MT, xAI API key + SuperGrok/Premium+ OAuth | Done |

## Out of scope

- Per-row temperature/max-tokens (optional follow-up)
- Auto-merge from official upstream without human review
- Publishing to official GHCR

## Security / ops

- API keys remain encrypted settings
- Automation cost warnings for paid AI
- After ship: `build-bedroom-image.sh` + recreate container; re-check plain/env `SERVICE_TYPE` migration
- Setting keys must be seeded (empty row) before encrypted set works
- Timeout setting keys are seeded by migration 15; do not rely on `SetSetting` to create them
- Qwen API keys and API hosts are region-specific. `qwen` and `qwen-mt` share
  those credentials, but retain separate model selections and timeouts.
- xAI OAuth uses a short-lived server-side device-flow record. Access and
  refresh tokens are persisted only through Lingarr's encrypted settings.
- xAI consumer OAuth availability and subscription entitlements are controlled
  by xAI; keep the conventional `xai` API-key provider available as the stable
  alternative.

## Related files

- `AGENTS.md` — agent/human operating rules for this fork
- `Lingarr.Server/Services/Translation/OpenRouterService.cs` — free-first catalogue
- `Lingarr.Server/Services/Translation/TranslationChain.cs` — parse/serialize chain
- `Lingarr.Client/src/components/features/settings/ServicesSettings.vue` — UI
- Hermes skill `lingarr-local` for Bedroom rebuild/import
