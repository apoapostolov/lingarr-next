# Lingarr Next development plan

This is the current execution map for AI-assisted development. It is not a
replacement for product proposals or architecture documentation:

- `docs/` holds durable design rationale and behavior contracts.
- `CHANGELOG.md` holds user-facing release history.
- this file holds only active sequencing, decisions, validation, and follow-up.

## Planning contract

Every non-trivial change starts with a compact plan containing:

1. Goal and observable user outcome.
2. Non-goals and compatibility constraints.
3. Canonical files or services that own the behavior.
4. Ordered implementation steps and the migration/deployment impact.
5. Validation commands and a clear stop condition.

The plan is updated when scope or sequencing changes, not used as a running
transcript. Completed work belongs in the changelog or a dated development log.

## Current state

- **Released:** Lingarr Next 1.2.3, “Gentle Sources”.
- **main:** the only long-lived branch, and the Lingarr Next 1.2.3 product
  line. Do not recreate `next`. Do not merge `upstream/main` into `main`.
  Upstream #502 (`5eadc933`) is an old snapshot and is not the branch tip.
  The daily check uses Plex, Radarr, and Sonarr and does not walk every
  folder. Upstream 1.4.0 was reviewed after pulling `main`. #527, #529,
  #550, #551, and #552 were already present. #553 and the signup/onboarding
  guard from `9fba9f7` are ported. Upstream 1.4.1 ports on `main`: #554
  (forced/regular caption pairing), #557 (case-insensitive language codes),
  #587 (closest language match via `GetBestMatch`), #556 (content translate
  survives disconnect, 409 duplicate, completed reuse), #586 (language JSON
  from `AppContext.BaseDirectory`), and #584 (`textContent` in highlight and
  show-title). Product recs taken from 1.4.1: #558 (M0046, default off,
  full-id removal), #574 schema line, #583 additive season lookup, #576
  toolbar wrap, #575 compose-dev named volumes. Still skipped: telemetry
  #510, date handling #514, translated context #530, path mapping removal
  #561, Local AI structured-output toggle #574, the issue-validation
  workflow deletion, and the Dependabot trains. Remaining 1.3.0 majors
  (Pinia 4, Node 26 types, Tailwind range, oxlint/oxfmt) are still skipped.
- **Protected line:** `main`.
- **Validation baseline:** 679 server tests, of which 677 pass on Windows. The
  two `PluginToolTests.Command_*` failures start `/usr/bin/printf` and `/bin/cp`,
  which only exist on the Linux CI runner. Bedroom was rebuilt from this working
  tree as `lingarr-next:latest` after the Hangfire, provider-timeout, source-guard,
  quality-rule and subtitle-acquisition fixes, and those fixes were then verified
  against the live library. 19 API smoke checks passed for subtitle downloader
  reliability on the previous image; see
  [validation](docs/validation/subtitle-downloader-reliability.md). The
  SubSource and SubDL providers were also round-tripped against the live services
  with the account keys before the release: SubDL returned the pilot of the
  requested season as `01 Pilot.en.srt`, and SubSource returned an unpacked ASS
  from its zip.

## Next decisions

### Remaining 1.3.0 features (not ported)

- Upstream xAI is a weaker copy of Next's xAI + OAuth. Leave it.
- Upstream user-prompt rewrite is a weaker copy of instruction profiles. Leave it.
- Dependabot bumps from 1.3.0: safe NuGet 10.0.10 train + axios/vue/vite/vue-tsc
  taken. Still skipped: Pinia 4, `@types/node` 26, Tailwind 4.3, oxlint/oxfmt.
- Upstream proofread Vue was not copied; Next uses the detail-page revise card.

### Branch and review hygiene

- `fix/microsoft-translate-long-lines` is now ported onto `main`. The old
  branch can be deleted after a remote check.
- Resolve or close the remaining old PR anchors before deleting their branches:
  content API paths and the obsolete request-timeout PR.

## Upstream 1.5.0 import decisions

Every commit in upstream `1.4.1..1.5.0` (16 commits, 143 files, release published
20 Nov 2026) has a verdict. Next keeps its own release line; these numbers are
upstream reference only. The alignment contract in `AGENTS.md` owns the rule.

| Upstream PR / SHA | Verdict | Reason and handling |
|-------------------|---------|---------------------|
| #589 `57a6d9c4` | Taken | Cherry-picked. phpMyAdmin dev image 5.0.1 to 5.2.3; dev-only. |
| `b096c199` | Declined | Re-pins that image to `latest`, undoing the pin. |
| #598 `ad376e53` | Declined | NuGet versions already superseded by fork bump #6. |
| #595 `869de884` | Deferred | Claude 5.5 batch and structured output need an adapted port; raw patch conflicts and its test needs fork settings. |
| #602 `2199288f` | Taken, adapted | Ordered language lists, order as a tier tiebreak, draggable chips and a first-target gate that also limits the downloader. Tier-first selection and usable-sidecar checks stay. |
| #601 `2f3efbc1` | Deferred | Follows #595; parse-retry must fit the fork's provider budgets. |
| #603 `c5e3206b` | Split | Encryption, queue errors, parser selection and small UI fixes evaluated; AI half deferred with the other batch changes. |
| #604 `4879125f` | Split / declined wholesale | Fork extractor is a superset. Stream-language aliases, dispositions and a failure cap are worth grafting; ffmpeg download, SharpCompress, duplicate toggle and UI are not. |
| #605 `18f41f9f` | Taken, adapted | `follow-redirects` moved to 1.16.2 in `package-lock.json` for credential-header redirect advisories. |
| #606 `a75a199b` | Taken, adapted | `FailTranslationRequest` and `EnqueueJob`; a failed enqueue no longer leaves a request Pending with no job. |
| `8f49d5f4` | Declined | Fork commit 68f6d07 already ignores TypeScript 7.x and vue-tsc 3.3.x. |
| #613 `849198fc` | Declined | Requires the deleted `docs.yml` and a released `openapi.json` asset; this fork publishes no Pages. |
| #612 `b5eb56d1` | Declined wholesale | 36 conflicting paths, deletes `service_type` and startup hooks, reuses migration 26 and would overwrite per-entry prompt profiles. `ClearMediaHash` taken as a standalone fix. |
| #614 `6070b62f` | Taken, adapted | Drag handlers and drop outline applied to the fork's chip markup. |
| #615 `fa277b61` | Deferred | Missing-position retry changes the fork's source-line fallback; needs a budget decision. |
| #616 `1d451c69` | Deferred | Test-only companion to #615. |

Deferred means the behaviour is wanted but needs its own reviewed change with the
fork's protected capabilities intact. No upstream tag, version number or Pages
workflow moves into this repository.

## Definition of done

- The requested behavior exists in its canonical owner.
- Relevant tests and documentation are updated.
- Client/server builds and the narrow smoke path pass, or the gap and risk are
  recorded explicitly.
- Migration and deployment effects are understood.
- The handoff names the exact commit, validation, deployment state, and follow-up
  decision without claiming work that was not performed.
