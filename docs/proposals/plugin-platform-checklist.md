# Plugin platform checklist

The plan is `plugin-platform.md`. This file tracks the five phases. Check an
item only when it is in the product and verified.

## Phase 1 — Host and panels

- [x] Loader records translation, ui, and action capabilities. Unknown types
  are skipped.
- [x] A manifest can contribute a section, a tab, and a panel.
- [x] Saving a panel writes only keys that panel owns. Secrets stay encrypted.
- [x] The Plugins page can enable a plugin, set its order, and set its failure
  policy.
- [x] A v1 translation plugin still loads. No hook runs.
- [x] The style sample adds a Translation tab with a toggle and a Test button.
- [x] The toggle is still set after a restart.

## Phase 2 — Post-process

- [x] `translate.after` runs language, then style, then line fit, then the
  quality gate.
- [x] Processors are off until enabled. Order follows the Plugins page.
- [x] A thrown processor keeps the previous subtitle file.
- [x] An OCR tag stays on the file name.
- [x] A sample language plugin and a sample style plugin exist and default off.

## Phase 3 — Tools and sources

- [x] The host can run one external command with a timeout and without a shell.
- [x] An extract tool can register beside ffmpeg, seconv, and ccextractor.
- [x] A subtitle source runs only when Bazarr is off or has already missed.
- [x] A fixture command converts one test track.
- [x] A fake source plugin can leave a sidecar that enters the normal
  translation path.

## Phase 4 — Servers, webhooks, jobs

- [x] A media-server plugin can refresh one item and select a subtitle. Plex,
  Jellyfin, and Emby stay built in.
- [x] A webhook inbox can turn fixture JSON into a queued movie.
- [x] A sample task appears under System → Tasks.
- [x] A notifier runs at `notify`.
- [x] A media action can run against one title.

## Phase 5 — Long tail

- [x] Dashboard widget.
- [x] List badge.
- [x] Log sink.
- [x] Health check.
- [x] Glossary. Terms reach post-processors on `input.Glossary`. They stay out
  of the model request.
- [x] Content filter.
- [x] Statistics exporter. Aggregate counts only.
- [x] Subtitle merge.
- [x] Prompt contributor. A named block is appended for providers that already
  take instruction profiles. The plugin does not receive the profile store.
- [x] Path mapper.
- [x] Retry policy.
- [x] Sidecar codec.
- [x] Caption policy.
- [x] Library agent.
- [x] File tool.
- [x] Media event sink.

## Later, not scheduled

The five-phase plan ends here. The unchecked items above stay in the catalog
until a real caller needs them. Prompt contributor stays closed until the
instruction-profile hand-off is safe.

A further phase would be a new plan. Candidates already named as out of scope:
a sandbox, hot reload, signed packages, webhook signatures, and a hook that
runs inside the model request.
