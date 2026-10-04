# AGENTS.md — Lingarr Next

Guidance for humans and coding agents working in **this** repo (`apoapostolov/lingarr-next`), not upstream.

## Source of truth

| Role | Location |
|------|----------|
| **Deploy / Docker image** | This repository only → image `lingarr-next:*` |
| **Official upstream (import only)** | https://github.com/lingarr-translate/lingarr |
| **Local clone** | `/mnt/c/git-public/lingarr` |
| **Ops skill** | Hermes `lingarr-local` (`references/bedroom-fork-build.md`) |
| **Product proposals** | `docs/` (keep in sync when behaviour changes) |
| **Current development plan** | `DEVELOPMENT_PLAN.md` (execution state, not product rationale) |

Never point Bedroom compose at official GHCR. Rebuild the bedroom image after meaningful server/client changes.

## AI-led development workflow

Project-local instructions and explicit user requests take precedence over
generic agent defaults. For every non-trivial change, the agent should:

1. Orient in the code, the nearest proposal or architecture doc, the current
   `DEVELOPMENT_PLAN.md`, and the working tree before editing.
2. Write a short execution plan with the goal, non-goals, canonical files,
   validation commands, deployment impact, and a stop condition.
3. Make the smallest coherent change, preserving unrelated user work. Do not
   invent provider behavior from memory: inspect the existing implementation,
   official provider documentation, or a checked-in reference first.
4. Add or update regression coverage for meaningful server, API, migration,
   provider, or cross-cutting UI changes.
5. Validate in layers: narrow tests first, then the client/server build, then
   the documented live smoke check. Record any skipped check and its residual
   risk in the plan or handoff.
6. Update the relevant proposal and user-facing changelog in the same change
   when behavior has changed. Keep `DEVELOPMENT_PLAN.md` focused on what is
   next; keep design rationale in `docs/`.

Agents should not spawn subagents by default. Use delegation only when the user
requests it or when the work has clearly separable discovery and validation
phases with explicit ownership and an integration review by the main agent.

The final handoff must state what changed, what was validated, what was
deployed, and any remaining risk. Do not commit or push unless the user asks or
this repository's release/deployment request clearly includes it.

## Active proposal docs

- `docs/ai-providers-model-fallback-chain.md` — AI providers, model picker, provider+model fallback chain

When you change product behaviour covered by a proposal, **update that doc in the same PR/commit**.

## OpenRouter conventions (Bedroom)

- **Preferred default model:** `openrouter/free` (free metamodel).
- Intended use: bulk / low-quality-OK subtitle translation without burning paid credits.
- **Model dropdown order** (must stay true in `OpenRouterService.GetModels`):
  1. `openrouter/free` — always first; inject if catalogue omits it
  2. `openrouter/auto`
  3. remaining catalogue (label sort)
- Seed / default setting key `openrouter_model` → `openrouter/free` when empty on new installs.
- Do not reorder auto above free without an explicit product decision.

## Translation chain (`service_type`)

- Prefer rich JSON: `[{ "provider": "openrouter", "model": "openrouter/free" }, …]`
- Legacy plain string / string-array still parse.
- Same provider may appear twice with different models.
- Chain row `model` overrides global `*_model` via `IModelOverridable` / `ResolveModel`.

## Providers added on this fork

| Id | Notes |
|----|--------|
| `openrouter` | OpenAI-compat; free metamodel first |
| `zai` | **GLM Coding Plan only** — base `https://api.z.ai/api/coding/paas/v4` (not general `.../api/paas/v4`). Models: `glm-5.2`, `glm-5-turbo`, `glm-4.7`. Wrong endpoint → 1113 insufficient balance. |
| `opencode-go` | Implemented; may be unsubscribed in Bedroom |
| `deepseek` | Upstream-style + model override |
| `qwen` | General Qwen Model Studio chat; model catalogue and instruction profiles |
| `qwen-mt` | Purpose-built Qwen subtitle translation with explicit language codes |
| `xai` | Official xAI API-key provider |
| `xai-oauth` | Experimental SuperGrok / Premium+ device-login provider |
| `mistral` | Official Mistral API; OpenAI-compat, model catalogue, instruction profiles |

## Secrets

- API keys live as encrypted Lingarr settings (and may be seeded from lifestyle `.env` for ops).
- **Never** commit real keys, library paths, or real movie/TV/comic/ebook titles in issues, PRs, commits, or docs. Use placeholders.

## Build / deploy (Bedroom)

```bash
# from clone
docker build -f Lingarr.Server/Dockerfile -t lingarr-next:latest \
  --build-arg TARGETARCH=amd64 --network=host .

docker compose -p media \
  -f /mnt/c/git/lifestyle/linux/dockhand/stacks/Bedroom/media/compose.yaml \
  up -d --force-recreate --no-deps lingarr
```

Or: `bash ~/.hermes/skills/devops/lingarr-local/scripts/build-bedroom-image.sh`

After recreate: re-check `SERVICE_TYPE` / plain vs JSON and that settings rows exist for new keys (Lingarr `SetSetting` does not create missing keys).

## Importing upstream

```bash
bash ~/.hermes/skills/devops/lingarr-local/scripts/sync-upstream-into-fork.sh
# review conflicts, then rebuild image
```

Do not auto-deploy after merge without a smoke check.

**Hard rule: ingest, do not paste.** Upstream is a backend/behavior catalog.
When a feature is worth having, port the server contract into Next and design
the UI in Lingarr Next's own language. Never drop upstream Vue pages, icons,
or settings chrome in wholesale. Do not add rules that forbid future providers
or capabilities just because Next already has a lot of them.

## Code style notes

- Prefer extending shared OpenAI-compatible patterns over copy-paste services.
- Model lists: go through `IModelCatalogService` (cache + `?refresh=true`).
- UI: model select sits **between** provider and API credentials on Services settings.
- Public OSS text (if any): no personal media fingerprints — see Hermes `gh-local` skill.

## Testing checklist (AI providers)

1. `GET /api/plugin` includes `openrouter`, `zai`, `opencode-go`, `deepseek`
2. `GET /api/plugin/openrouter/models` → first option value is `openrouter/free`
3. Translate line smoke for providers with working keys
4. Restore `service_type` to a free scraper (e.g. microsoft) after paid tests if desired

## Release and branch hygiene

- `main` and `next` are protected release lines; do not rewrite either.
- Treat old feature branches as disposable only after checking ancestry,
  patch-equivalence, open pull requests, and whether they contain unique
  unported behavior.
- Lingarr Next uses its own version line and tags; upstream tags and branches
  are reference material, not release targets.
- A release commit must include the changelog, version metadata, migration
  notes, and validation result. Pushes and remote branch deletion require an
  explicit user request.

## Documentation

- `docs/README.md` — index
- `docs/architecture.md` — system architecture
- `docs/testing.md` — unit + smoke tests
- `docs/ai-providers-model-fallback-chain.md` — AI/fallback product design
- `tests/smoke/smoke_api.py` — live API smoke
