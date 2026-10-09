# Plugin platform

Audit of the Lingarr Next plugin API, and the plan to open the rest of the
product to plugins. This is a proposal. It does not change the running API.

## What exists today

Plugin API major version is 1. A DLL is loaded only when `PLUGINS_PATH` is
set, the folder exists, the assembly has
`[assembly: LingarrPluginApiVersion(1, x)]`, and the major matches. The minor
is stored and then ignored. A bad DLL is skipped. There is no hot reload and
no sandbox. A plugin runs in the Lingarr process with the same rights as the
server. Only DLLs the operator trusts should be installed.

`PluginLoader` registers exactly one kind of type from a third-party DLL:
a class marked `[PluginProvider("id")]` that implements `ITranslationService`.
It is added as a keyed scoped service under that id. `IBatchTranslationService`
and `IProofreadService` are optional interfaces on that same class. They are
not discovered on their own. If the class does not implement
`ITranslationService`, the loader ignores it.

`IPluginManifest` describes a provider name and a flat list of settings
fields. Field types are Text, Url, Secret, RemoteDropdown, and OAuth. On
load, those keys are inserted into the settings table if they are missing.
`ISettingsAccess` can read them. It cannot write them. The Translation Setup
page renders the fields for the selected service. The Plugins page is a
read-only list of third-party DLLs. It does not host settings.

Built-in translators also use `IPluginManifest`, so `GET /api/plugin` mixes
built-in providers and real plugins. The client hides built-ins on the
Plugins page. Reserved ids are the built-in translator names. A duplicate
provider id is dropped with a warning.

The rest of the product is closed:

- Settings sections and tabs are a hardcoded list in
  `SettingsSectionTabs.vue`. A plugin cannot add a section, a tab, or a card.
- `ProcessMedia` decides extract, Bazarr, OCR last-resort, and translation
  with no extension point.
- `TranslationJob` translates, validates, and writes the sidecar with no
  post-process step after the file exists.
- Webhooks, Plex selection, Jellyfin and Emby, picture tools, schedules,
  logs, and the dashboard are host code only.
- Third-party manifests report `SupportsInstructionProfiles` as false.
  Instruction text is not handed to a plugin.

The sample is `samples/CloudflarePlugin`. It is a translation provider and
nothing else.

## Goal

A plugin can add a service, a settings surface, and a hook into the work
Lingarr already does. The first new behavior operators asked for is
subtitle post-processing: after a translation exists, another service can
improve the language or the style, then write the result back.

The host stays in charge of order, permissions, and the screen. A plugin
declares what it is and which stages it joins. It does not ship Vue files
into the client build.

## Non-goals for the first three phases

- A plugin marketplace, a store, or unsigned remote install.
- A sandbox that runs untrusted .NET safely. Trust stays with the operator.
- Plugins that replace the database, the auth cookie, or another plugin's
  secrets.
- Plugins that inject raw HTML or Vue into settings.
- Giving every plugin the instruction-profile text before the built-in
  contract for that text is the one already shipped.

## Shape

Stay on plugin API major 1. Every new interface is optional. A Cloudflare
DLL built against today's contracts still loads as a translator. Bump the
major only when a contract is removed or its meaning changes.

One DLL may expose several capabilities. Each capability has its own small
interface and a stable string id. The loader registers every capability it
recognises. Unknown interfaces are skipped and logged, so an older host can
load a newer DLL's translators and ignore the rest.

The host passes a context, not the EF model and not `IServiceProvider`. The
context can read and write only the keys that plugin declared, write a log
line, and call the shared HTTP settings. Media and subtitle bytes are passed
as values for the stage in progress. A plugin does not query the database.

Settings UI is a schema:

- A contribution names a section (Connections, Translation, Automation,
  System, or Plugins), a tab, and a panel.
- A panel is a title, a description, fields, and actions.
- Field types grow from the five that exist: toggle, number, hours, select,
  and multiline text. Secret stays encrypted.
- The client renders the schema with the same cards and toggles the product
  already uses. No plugin markup.

Hooks are ordered stages. Each stage has a context and a result. The host
runs enabled plugins for that stage in the order saved in settings. A plugin
returns continue, replace, skip, or stop. A thrown exception is logged,
counted as a failure for that plugin, and does not abort the title unless
the plugin's failure policy says to stop. Policies are skip, stop this
title, or fail the job. The default is skip.

Enable, order, and failure policy are host settings, not code inside the
DLL. The Plugins page becomes the place that lists capabilities and turns
them on. A plugin with a panel also appears as that panel.

## Pipeline stages to open

These are the host points. A plugin type subscribes to one or more of them.

| Stage | Where it sits | What a plugin may change |
| --- | --- | --- |
| `media.discovered` | After a webhook or a library sync finds a file | Accept, delay, or ignore the title |
| `source.missing` | `ProcessMedia` when no real source subtitle exists | Supply a sidecar, or decline |
| `extract.plan` | Before ffmpeg, seconv, or ccextractor | Choose or refuse a track |
| `extract.done` | After a text, OCR, or caption file is written | Replace the file or mark it |
| `source.chosen` | After the source subtitle is selected | Swap the file |
| `translate.before` | Before `TranslationJob` calls the provider | Change the request or skip the title |
| `translate.line` | After a line or a batch returns | Replace the line |
| `translate.after` | After the target sidecar is written | Replace the file |
| `subtitle.select` | Before Plex, Jellyfin, or Emby selection | Choose the stream or skip |
| `notify` | After success or failure | Send a message |

Post-processing for language and style is `translate.line` and
`translate.after`. OCR cleanup is `extract.done`. A Bazarr-like downloader
is `source.missing`. A new media server is `media.discovered` plus
`subtitle.select`.

## Catalog

Thirty-six plugin types. The first twelve are the ones that unlock the
requested post-processing, settings panels, and external tools. The rest are
the long tail, in an order that can ship later without another API break.

1. **Translation provider.** Exists. Keep it.
2. **Batch translator.** Exists only as an extra interface. Register it on its own when a provider translates many lines per call.
3. **Proofreader.** Exists only as an extra interface. A model rereads a line and may return it unchanged.
4. **Language post-processor.** After translation, improve grammar, agreement, and fluency in the target language. Runs at `translate.after`. This is the language-improvement tool.
5. **Style post-processor.** After translation, apply a style: formality, punctuation, reading speed, or a house style. Separate from language repair so the operator can enable one and not the other.
6. **Glossary.** A list of names and terms that translation and post-process must keep. Consulted at `translate.before` and `translate.line`.
7. **OCR corrector.** Clean a text file that came from a picture track: junk glyphs, broken hyphenation, timing left intact. Runs at `extract.done` only when the sidecar is tagged `ocr`.
8. **Line fitter.** Break lines, cap characters per second, and merge cues that are too short. Runs at `translate.after`. Does not call a model unless it says it does.
9. **Quality gate.** Accept, reject, or ask for another pass. Sits beside the built-in score and the selected classifier checks. It does not replace them.
10. **Subtitle source.** Search and download a source subtitle the way Bazarr does. Runs at `source.missing`. The host still applies the score floor, the forced-subtitle rule, and the OCR-replace rule.
11. **Extract tool.** An external program that turns one embedded track into a text file. The built-in ffmpeg, seconv, and ccextractor paths stay. A plugin adds another binary. Runs at `extract.plan`.
12. **External command.** A named tool with an executable, arguments, a timeout, and a working directory. The host runs it. The plugin does not get a shell. Used by extract tools and file tools.
13. **Sidecar codec.** Read or write one subtitle format. Built-in srt, ass, ssa, and vtt stay.
14. **Caption policy.** Decide whether a file is SDH, forced, hearing-impaired, or plain, and whether it is eligible as a source.
15. **Media server.** Connect to a server, refresh one item, and select a subtitle stream. Plex, Jellyfin, and Emby remain built in. A plugin adds another server.
16. **Webhook inbox.** Parse one product's JSON into a movie or an episode add. The host queues it. Playback and delete stay ignored unless the plugin asks for them.
17. **Library agent.** Sonarr, Radarr, or a later download client: find a file by external id. Built-in Sonarr and Radarr stay.
18. **Path mapper.** Extra rules when the container path and the server path differ. The existing mapping table stays the default.
19. **Schedule task.** A named job with an interval, shown under System → Tasks. The host runs it on the existing Hangfire queue.
20. **Retry policy.** Decide when a miss or a failed translation is tried again, and when it stops. Bazarr's 12 hour / 168 hour policy stays the default for Bazarr.
21. **Media event sink.** Listen to `media.discovered` and later stages without changing the file. For an external indexer or a log shipper.
22. **Settings section.** Add a section to the settings sidebar.
23. **Settings tab.** Add a tab inside a section, including one the plugin created.
24. **Settings panel.** A card of fields and buttons on a tab. Saving goes through the host settings store.
25. **Settings action.** A button on a panel that calls one method on the plugin, such as Test or Scan. The result is one sentence in the product tone.
26. **Dashboard widget.** A small read-only block on the dashboard: counts the plugin itself computed.
27. **Media action.** A button on a movie or an episode. The host passes that one title. Example: "Improve style".
28. **List badge.** A short label on the translation list, such as "styled" or "ocr". The host draws it.
29. **Log sink.** Receive the same lines the system log stores. It cannot read secrets from other plugins. The in-memory log stays.
30. **Notifier.** Send success or failure to a destination. Runs at `notify`.
31. **Health check.** A connection test the host can show next to the built-in service tests.
32. **File tool.** Run after a sidecar is written: chmod, upload, or call an external muxer. Runs at `translate.after`, after style and language processors.
33. **Content filter.** Drop or keep cues, credits, or a matched pattern before translation. The classifier switches stay built in.
34. **Prompt contributor.** Add a named instruction block for AI providers that already support instruction profiles. Off until the host defines the safe hand-off. A plugin does not receive the raw profile store.
35. **Statistics exporter.** Read aggregate counts and write them somewhere else. No per-title private paths unless the operator turns that on.
36. **Subtitle merge.** When a better source arrives for a file that was OCRed or already translated, decide whether to replace the source, keep the translation, or queue a new one. The current OCR-replace behavior stays the default.

Types 4 and 5 are the language and style tools. Types 22, 23, and 24 are how a plugin registers tabs and panels. Type 12 is the external-tool escape hatch. Types 10, 11, 14, 15, and 16 are how new services join the library flow without a fork.

## Post-processing, in detail

Language and style run only after a target subtitle file exists and has passed
the current validation. Order is fixed unless the operator reorders it:

1. Content filter, if it is a pre-pass, has already run.
2. Translation writes `name.bg.srt` or `name.bg.ocr.srt`.
3. Language post-processors, in operator order.
4. Style post-processors, in operator order.
5. Line fitter.
6. Quality gate. A rejection keeps the previous file and records why.
7. File tools, then media-server selection, then notify.

Each processor receives the source file, the target file, the language codes,
and whether the source was OCR. It returns a new target file or the same
file. It must not delete the source. The host writes the result atomically
and keeps the caption tag. A style run does not strip `ocr` from the name.
Replacing an OCR source is still the Bazarr path, not the style path.

A processor declares whether it calls a model. If it does, it uses a
translation provider id the operator picked, or its own credentials on its
own panel. It does not borrow another provider's key.

The panel for a language or style plugin lives on Translation → Subtitles,
below the picture tools, unless the plugin asks for its own tab.

## Settings registration

The client asks `GET /api/plugin/ui` once when settings open. The response
is the merged schema: sections, tabs, panels, fields, actions. Built-in
pages stay as they are. Plugin tabs are appended. A plugin tab that fails
to load shows the plugin name and "This panel could not be loaded." The
other tabs still work.

Saving a field uses the existing settings API. The server rejects a write
to a key the calling user's panel does not own. Secrets use the encrypted
setting path. Actions are `POST /api/plugin/{id}/actions/{action}` and
return `{ "message": "..." }` in the product tone.

Enable and order for hook plugins live on the Plugins page, not only inside
the plugin's own panel. A plugin can be installed and still be off.

## Phases

### Phase 1 — Host and panels

Loader registers a capability list. Manifest can contribute tabs and panels.
Settings write is limited to the plugin's own keys. Plugins page shows
enable, order, and failure policy. v1 translators still load. No new hook
runs yet.

Exit: a sample plugin adds a tab under Translation with a toggle and a Test
button, and the toggle survives a restart.

### Phase 2 — Post-process

Implement `translate.after` with language, style, line fit, and the quality
gate. One sample language plugin and one sample style plugin, both optional
and off by default. OCR files stay tagged.

Exit: a translated Bulgarian file can be restyled by the sample without a
second full translation, and a processor that throws does not lose the
original file.

### Phase 3 — Tools and sources

External command runner, extract tool, and subtitle source. The picture
pipeline calls extract tools after the built-in ones when the operator
enables them. A source plugin runs only when the built-in Bazarr step is
off or has already missed, so two downloaders do not fight.

Exit: a fixture command can convert one test track, and a fake source
plugin can drop a sidecar that then enters the normal translation path.

### Phase 4 — Servers, webhooks, jobs

Media server, webhook inbox, schedule task, notifier, and media action.
Plex, Jellyfin, and Emby stay built in.

Exit: a sample webhook inbox can queue a movie from a fixture JSON payload,
and a sample task appears under System → Tasks.

### Phase 5 — The long tail

Dashboard widget, list badge, log sink, health check, glossary, content
filter, statistics exporter, subtitle merge, prompt contributor, path
mapper, retry policy, sidecar codec, caption policy, library agent, file
tool, and media event sink. Each one ships when a real caller needs it.
The interface is reserved in the catalog so later work does not break the
loader.

## Risks

- A post-processor that rewrites a good file badly is worse than no plugin.
  Default off, keep the previous file, and record the processor name on the
  request.
- Two source plugins can download different subtitles for one title. Phase 3
  runs only one enabled source unless the operator sets a chain.
- Schema UI will not cover every custom layout. A panel that needs a custom
  screen waits, instead of allowing raw HTML.
- Loading into the default assembly context can collide on dependency
  versions. The docs already warn about that. Phase 1 logs the loaded
  assembly names. Isolation is a later project, not part of this plan.
- `SupportsInstructionProfiles` stays false for third-party translation
  plugins until the prompt hand-off is specified. Type 34 does not sneak
  around that.

## Documentation and tests

- Update `Lingarr.Docs/developers/Plugins.md` in the same change as Phase 1.
- Keep `samples/CloudflarePlugin` on the v1 translator path so the old
  contract stays honest.
- Add `samples/StylePlugin` in Phase 2.
- Unit tests: loader skips an unknown capability, rejects a settings write
  outside the manifest, runs two post-processors in the saved order, and
  keeps the original file when the second one throws.
