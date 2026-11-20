# AGENTS.md — Lingarr Next

Guidance for humans and coding agents working in **this** repo
(`apoapostolov/lingarr-next`). It is no longer a GitHub fork. Treat it as a
managed fork of [Lingarr](https://github.com/lingarr-translate/lingarr):
cherry-pick and diff against that upstream, and ship only on this repository.

## Source of truth

| Role | Location |
|------|----------|
| **Deploy / Docker image** | This repository only → image `lingarr-next:*` |
| **Official upstream (import only)** | https://github.com/lingarr-translate/lingarr (`upstream`, branch `main`) |
| **Upstream snapshot** | Commit `5eadc933` (#502). It is not the tip of `main`. |
| **Active line** | `main` only. Do not recreate `next`. |
| **Local product clone** | `C:/git-public/lingarr-next` (Bedroom: `/mnt/c/git-public/lingarr-next`) |
| **Older fork (not a source)** | `C:/git-public/lingarr`, `apoapostolov/lingarr`, branch `next` |
| **Ops skill** | Hermes `lingarr-next` (`references/sqlite-writer-contention.md`) |
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

## Providers added in Lingarr Next

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

## Managed upstream

This repository is not a fork on GitHub. The archived remote `fork`
(`apoapostolov/lingarr`) is old history. Do not push to it, and do not
cherry-pick from it. Upstream work comes only from `upstream`:

```text
https://github.com/lingarr-translate/lingarr
```

`gh` with no `-R` is the wrong repository. Upstream PRs and commits use
`-R lingarr-translate/lingarr`. This repo's Actions and releases use
`-R apoapostolov/lingarr-next`.

`main` is the only long-lived branch, and it is the product line. Do not
recreate `next`. Do not merge `upstream/main` into `main`. The old upstream
snapshot is commit `5eadc933` (#502). It is not an ancestor of `main`. Next was
not replayed onto that snapshot, because the two histories conflict. New
upstream work still lands as one reviewed cherry-pick or hand port.

### What arrived since the snapshot

```bash
git fetch upstream
git log --reverse --oneline main..upstream/main
gh pr list -R lingarr-translate/lingarr --state merged --base main --limit 30
```

`main..upstream/main` is the candidate list. It still contains commits that
were already ported or deliberately skipped. Before proposing a cherry-pick,
search `DEVELOPMENT_PLAN.md` and `git log main --grep '#<N>'` for that PR.
Do not pick it again when it is already recorded.

Already ported onto `main`: #527, #529, #550 (behavior port, not a clean
pick), #551, #552, #553 (empty subtitle language becomes `unknown`),
the unauthenticated signup and onboarding guard from `9fba9f7` (1.4.0),
and from 1.4.1: #554, #557, #587, #556, #586, #584, #558, #574
(schema line), #583, #576, and #575.

Still skipped: telemetry #510, date handling #514, translated context #530,
path mapping removal #561, Local AI structured-output toggle #574, the
issue-validation workflow deletion, Dependabot trains, and the remaining
1.3.0 majors (Pinia 4, Node 26 types, Tailwind range, oxlint/oxfmt).

### Diff one new commit against current main

`git diff main upstream/main` is the whole divergence, not the patch to apply.
Judge one upstream commit, `<sha>`, against the current product commit
(`git rev-parse main`):

```bash
git show --stat <sha>
if tree=$(git merge-tree --write-tree --merge-base=<sha>^ main <sha>); then
  git diff --stat main "$tree"
else
  echo "conflict"
fi
```

`merge-tree` replays that one commit onto current `main`. Check its exit
status before reading the diff. On conflict, the command prints the conflict
and the `if` body does not run.

- Exit 0 and an empty diff: `main` already has the result. Skip it.
- Exit 0 and a non-empty diff: that stat is the change `main` would gain.
  Read it before taking the commit.
- Non-zero exit: the commit conflicts with `main`. Read the conflict output.
  Port the behavior by hand, or skip it. Do not merge the branch to force it
  through.

For a merged PR, inspect it before choosing commits:

```bash
gh pr view <N> -R lingarr-translate/lingarr \
  --json title,mergeCommit,mergedAt,baseRefName,commits
gh pr diff <N> -R lingarr-translate/lingarr
```

Cherry-pick onto a branch cut from `main`. Use
`git cherry-pick -x <sha>` for a normal commit so the upstream SHA stays in
the message. Use `git cherry-pick -x -m 1 <merge-sha>` only when the PR commit
has more than one parent. Do not push upstream tags or version numbers onto
this repo. Next keeps its own version line.

**Hard rule: ingest, do not paste.** Upstream is a backend and behavior
catalog. When a change is worth having, port the server contract into Next and
keep the UI in Lingarr Next's own language. Never drop upstream Vue pages,
icons, or settings chrome in wholesale. Skip telemetry, a dependency major
Next has not adopted, and a weaker copy of a feature Next already has. Do not
add rules that forbid future providers or capabilities just because Next
already has a lot of them.

Do not deploy a cherry-pick until its tests have been run.

### Alignment contract

Lingarr Next and upstream Lingarr share history but not a product line. The rule is one-way: upstream is a source of reviewed imports, never a target. A change lands on `main` only when it improves Next without removing a capability Next already ships.

Version numbers are local. Next 1.2.3 is not older than upstream 1.5.0; the two lines are independent. Never move an upstream tag, branch, release number or version string into this repository.

Every upstream commit in a release gets a written verdict before any code is touched. Record it in `DEVELOPMENT_PLAN.md` under the upstream release heading as three columns: upstream PR or SHA, verdict, and reason. The verdict is one of:

- **Take**: the behaviour is missing here and the patch applies without touching a protected file. Cherry-pick with `-x`.
- **Adapt**: the behaviour is worth having but the patch collides with a Next implementation. Port the server contract into Next's own classes, settings and UI, and keep the upstream SHA in the commit message.
- **Decline**: Next already has a stronger version, the change is a dependency major Next has not adopted, or it belongs to upstream's product decisions (telemetry, path mapping removal, issue workflows, Pages publishing).

A clean `merge-tree` result is permission to inspect, not permission to merge. Read the diff against current `main` before deciding. One import per coherent behaviour; do not bring a companion UI, migration or dependency change along without its own verdict.

### Protected capabilities

An upstream import must not remove, rename, weaken or replace these with an upstream copy. The canonical file is the one to check before and after the port.

| Capability | Canonical files | Rule |
|------------|-----------------|------|
| Translation chain with per-chain model and repeated providers | `Lingarr.Server/Services/Translation/TranslationChain.cs`, `TranslationFactory.cs`, `docs/ai-providers-model-fallback-chain.md` | Keep rich JSON `service_type`, legacy parsing and `IModelOverridable`. Named instances may be added only as an additive layer. |
| Next-only providers | `Lingarr.Server/Services/Translation/` (`OpenRouterService`, `ZaiService`, `OpenCodeGoService`, `QwenService`, `QwenMtService`, `XaiOAuthTranslationService`, `MistralService`, `DeepSeekService`) | Upstream factory or registry changes must not delete these ids or their registration. `openrouter/free` stays first in the model dropdown. |
| Provider timeouts and cancellation | `RequestBudget.cs`, `GTranslatorService.cs`, `ProviderCancelRetry.cs`, `docs/provider-timeouts.md` | An upstream retry must fit inside the existing attempt and whole-request budgets. |
| Source subtitle guard and quality rules | `SourceSubtitleGuard.cs`, `TranslationQualityService.cs` | Structure-based refusal and ruleset 2 stay; no markup-share or plain repetition threshold. |
| Cancelled progress continuation | `CancelledProgressCache.cs`, `M0033_CacheCancelledProgress.cs` | Keep continuation on cancel, the partial score gate and the ungated Resume button. |
| Provider health and SQLite contention | `ProviderHealthService.cs`, `SqliteWriteRetry.cs`, `LogHints.cs` | Reads must not write; retries stay SQLite-only; retention stays housekeeping after the readings. |
| Next subtitle sources | `Lingarr.Server/Services/SubtitleDownload/` (`SubtitleDownloadCatalog.cs` and the SubSource/SubDL flow) | Upstream source selection must not replace the paced provider flow or refusal reporting. |
| Embedded subtitle extraction | `Lingarr.Server/Services/Subtitle/EmbeddedSubtitleExtractor.cs`, `NonTextSubtitlePolicy.cs`, `SsaParser.cs` | An upstream ffmpeg extractor may complement, never overwrite, the existing parser and naming rules. |
| Prompt and instruction profiles | `TranslationPromptProfileService.cs`, `PromptProfileEditor.vue`, `ProofreadPrompts.cs` | Per-request templates and profiles outrank an upstream global prompt setting. |
| Toolchain pins | `Lingarr.Client/package.json`, `.github/dependabot.yml`, `Directory.Packages.props` | `vue-tsc` stays exactly `3.3.11`; TypeScript 7.x stays ignored until a stable checker exists. No GitHub Pages workflow. |

When an import touches a protected file, the PR must name the capability and show a test that still passes. When upstream and Next solve the same problem differently, Next's behaviour wins unless a product decision says otherwise.

### Validation for an import

1. Narrow tests for the ported behaviour, with real output pasted into the handoff.
2. Full server suite, compared with the documented baseline in `DEVELOPMENT_PLAN.md`. Known Windows-only `PluginToolTests` failures are not a regression; any other failure is.
3. Client type-check and build with the pinned checker. Migration tests need Docker and otherwise fail with `DockerUnavailableException`.
4. New migrations number above the highest existing migration and never reuse upstream numbers. New setting keys are seeded without overwriting a value a user already set.
5. `CHANGELOG.md`, `DEVELOPMENT_PLAN.md` (including the upstream verdict table), `Readme.MD` **What's new** as required in **Release and branch hygiene**, and any affected `docs/` proposal change in the same PR.
6. Wait for CodeRabbit, Greptile and Sourcery after CI goes green, and read comments created after the last push, before merging. Deploy to Bedroom only after the merge.

## User-facing response strings

Text the user reads from a test, a validation check, or an action result uses a product tone.

- Name the system, then the outcome: "Bazarr connection succeeded."
- A failure uses the same shape: "Bazarr connection failed." Add one fact when it helps: "Bazarr connection failed. The server returned HTTP 401."
- Missing input names the fields: "Enter the Bazarr address and API key."
- An action names the action and the result: "Library scan started."
- One sentence. No exclamation, no apology, and no chat.
- Do not use "answered", "did not answer", "Test successful.", or "Set … first."
- Log lines may stay technical. This rule is only for text shown in the interface.

## System logs

The log is in memory. It keeps Information and above, at most 1000 lines, and drops a line after 24 hours. Settings → System → Logs opens on the newest page. Older lines load when that pane scrolls up. A warning or error includes `hint`, one sentence naming the next step. Prefer that sentence over inventing a fix. Do not print secrets that show up in a line.

Use the same authentication as the rest of the API. `X-Api-Key` works on the JSON routes. A browser `EventSource` cannot send that header, so use `wait` when authentication is on.

- `GET /api/logs?limit=80` returns the newest page. Lines inside the page are oldest first. `before={id}` loads older lines. `after={id}` loads newer lines. `minLevel` is `Warning` or `Error` (that level and above).
- The body is `{ items, hasOlder, oldestId, newestId, latestId }`. Each item has `id`, `logLevel`, `message`, `exception`, `hint`, `timestamp`, and `category`.
- `GET /api/logs/wait?after={id}&minLevel=Error&timeout=25` returns the next matching lines, or an empty list when the wait ends. Call it again with the highest `id` you have seen. This is how an agent hears about a new error.
- `GET /api/logs/stream?after={id}&minLevel=Error` is server-sent events. `event: log` is every matching line. `event: problem` repeats that line when it is a warning or an error. Listen to one of those events, not both. Omit `after` to follow only new lines.
- `DELETE /api/logs` clears the buffer. Ids keep increasing.

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

- `main` is the only long-lived branch. Do not recreate `next`.
- Treat old feature branches as disposable only after checking ancestry,
  patch-equivalence, open pull requests, and whether they contain unique
  unported behavior.
- Lingarr Next uses its own version line and tags; upstream tags and branches
  are reference material, not release targets. Compare new upstream commits
  with the procedure in **Managed upstream**.
- A release commit must include the changelog, version metadata, migration
  notes, validation result, and a plain-language entry for that version in
  `Readme.MD` under **What's new**. A release is not complete until the README
  names the version just released; do not leave the section on an older one.
- Pushes and remote branch deletion require an explicit user request.

## Documentation

- `docs/README.md` — index
- `docs/architecture.md` — system architecture
- `docs/testing.md` — unit + smoke tests
- `docs/ai-providers-model-fallback-chain.md` — AI/fallback product design
- `tests/smoke/smoke_api.py` — live API smoke
