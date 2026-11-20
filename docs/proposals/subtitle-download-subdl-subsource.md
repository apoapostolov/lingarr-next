# Proposal: real SubDL and SubSource subtitle sources

**Status:** Planning — grounded in code and live API probes; nothing implemented, nothing committed

**Scope:** `SubtitleDownloadGateway`'s two key-based providers (`subdl`, `subsource`), their rate
limiting, and their tests

**Related:** [subtitle-maintenance.md](../subtitle-maintenance.md),
[provider-timeouts.md](../provider-timeouts.md),
[plugin-platform.md](./plugin-platform.md)

**Audience:** Contributors touching subtitle acquisition

---

## How to read this

Both providers already exist in the catalogue and are dispatched today. They do not work against the
current SubDL and SubSource APIs, and neither of them respects the rates those services publish. This
proposal replaces the two code paths rather than adding providers, and it does so without new
user-facing settings.

Everything below was checked against the running services on 2026-11-20 with the account's own keys,
so the request/response shapes are observed, not assumed.

## What exists today

| Piece | Location |
|---|---|
| Provider catalogue entries (`subdl`, `subsource` with their settings fields) | `SubtitleDownloadCatalog.cs:349`, `:414` |
| Dispatch | `SubtitleDownloadGateway.cs:120` (SubDL), `:124` (SubSource) |
| SubDL implementation | `SubtitleDownloadGateway.cs:1011` `SubdlAsync` |
| SubSource implementation | the generic `JsonSearchAsync` at `:1284`, configured at `:124-125` |
| URL harvest used by the generic helper | `:1341-1384` `FindUrls` / `CollectUrls` |
| Download + archive unpacking | `:347` `FetchBytes`, `SubtitleArchive.Unpack` |
| HTTP client | `ServiceCollectionExtensions.cs:74` (`subtitle-download`, 60 s timeout, no resilience policy) |

## What is actually wrong

### SubSource is addressed as a different API

The dispatch calls:

```csharp
SubtitleDownloadKind.Subsource => JsonSearchAsync(
    provider, query, "https://api.subsource.net/api/v1/subtitles", "apikey", cancellationToken),
```

Live behaviour of that endpoint:

- It needs `movieId` (a numeric id) and `language` as a **full word** (`english`, `bulgarian`).
  `language=en` returns HTTP 200 with `"total": 0`, which is indistinguishable from "no subtitles
  exist".
- The key goes in an `X-API-Key` **header**, not an `apikey` query parameter. Without the header the
  API answers `401 {"error":"API key required"}`.
- `q=` is not a parameter here at all; title search lives at `/movies/search?searchType=text&q=`,
  which returns the numeric `movieId` that `/subtitles` then needs. Two calls, not one.

So today the provider either 401s (caught and logged as a search failure) or, if the endpoint ever
answered, would be searching with parameters the API ignores.

### The generic helper finds nothing to download

`JsonSearchAsync` never inspects a payload; it collects every string that starts with `http`, ends
with `.srt`, or contains `/download`. A current SubSource `/subtitles` row carries only metadata
(`subtitleId`, `language`, `releaseInfo[]`, `files`, `size`, `hearingImpaired`, `productionType`,
`releaseType`, `downloads`, `comments`, `rating`, `preview`, `framerate`, `commentary`, `uploaderId`)
and **no URL at all**; the download is a separate endpoint, `/subtitles/{id}/download`, that returns a
zip. Probed live: the harvested list is empty, so even a request that somehow succeeded would report
"no subtitle found".

That is the real shape of the defect: wrong endpoint, wrong parameters, wrong key placement, and a
response for which the generic helper has no rule.

### SubDL hits the right endpoint and then throws the answer away

`SubdlAsync` builds a correct search URL and then:

- omits `unpack=1`, so a TV search returns season packs and the code downloads the first `url`, which
  is a whole-season zip rather than the episode that was asked for;
- takes `subtitles[0]`'s first URL with no release-name, episode, or hearing-impaired comparison,
  so even a correct pack can produce the wrong file;
- ignores `subs_per_page` (default 10, max 30) and `client=`, which the API documents specifically so
  integrations can be identified;
- never looks at `/api/v1/me`, where the free key's **50 downloads per day** is visible
  (`downloads.limit = 50`, `remaining` decrements per authenticated download).

### Neither provider paces itself

`subtitle-download` is a plain `HttpClient` with a 60 s timeout. There is no minimum interval, no
daily budget, no `429`/`Retry-After` handling, and no stop-after-N-failures. SubSource publishes
60 req/min, 1,800/hour, 7,200/day and returns the live remainder in `X-RateLimit-Remaining-Hour` and
`-Remaining-Day`. SubDL publishes 2,000 requests and 50 downloads per day for a free key and sends no
rate headers at all. `ProviderHealthService` maps 429 to a `rate_limit` health reading, which is
reporting only; nothing in the path actually slows down.

## Proposal

### 1. A dedicated `SubsourceAsync`

```
GET /api/v1/movies/search?searchType=imdb&imdb=<tt…>      (fall back to searchType=text&q=<title>)
GET /api/v1/subtitles?movieId=<id>&language=<word>&limit=100&page=N&sort=popular
GET /api/v1/subtitles/{subtitleId}/download              → application/zip
```

- Key in `X-API-Key`; `Accept: application/json`.
- Language word from a small map (`en`→`english`, `bg`→`bulgarian`, …) seeded from
  `LanguageCodeService`; an unknown code means "provider unavailable for this language", not a guess.
- TV rows carry no `season`/`episode` fields. The episode is inside `releaseInfo[]` strings
  (`Breaking.Bad.S03E01.1080p.BluRay.x265-RARBG_forced`), so selection scans those, then falls back
  to the year/episode title. A row whose release list does not contain the wanted `SxxExx` is rejected
  rather than guessed.
- Rank candidates by episode match, token overlap with the media file name, `rating.good/bad`,
  `downloads`, and a penalty for `hearingImpaired`.
- `SubtitleArchive.Unpack` already handles the zip; `SubtitleBytes.MismatchesLanguage` stays as the
  final gate.

### 2. Harden `SubdlAsync` instead of replacing it

- Add `unpack=1`, `subs_per_page=30`, `client=custom_integration`, and prefer `hi=0` when a
  non-SDH option exists.
- Select from `unpack_files[]` by its own `episode` field (each entry carries `file_n_id`, `name`,
  `release_name`, `season`, `episode`, `language`, `hi`, `format`, `size`, `md5`, `url`), not from the
  pack. Verified live: the first file of a `Breaking Bad` S01 search reports
  `{"name":"01 Pilot.en.srt","season":0,"episode":1,"language":"EN","hi":false,"format":"srt"}`.
- Download the single-file URL (`/subtitle/{n_id}/{file_n_id}`). Verified live: it answers 200 both
  anonymously and with `x-api-key: <key>`, and the URL SubDL returns is already key-signed, so the
  header is a belt to that braces rather than a requirement.
- Keep the pack zip as a fallback only when no unpacked file matches.

### 3. One pacer for the two providers

`SubtitleProviderPacer`, registered as a singleton, holding per-provider state:

| Concern | Behaviour |
|---|---|
| Minimum interval | SubSource 2.5 s, SubDL 3.5 s, plus forward-only jitter |
| Daily request budget | SubSource 1,200, SubDL 300 (a fraction of the published day) |
| Daily download budget | SubSource 200, SubDL 40 (50 is the hard limit) |
| Provider headers | read `X-RateLimit-Remaining-Hour` / `-Day`; pause when the hour is nearly spent, stop when the day is |
| `429` | honour `Retry-After` (default 15 min), record a `ProviderOperationalEvent` with kind `rate_limit` |
| `5xx` | 60 s doubling to a 30 min cap; three consecutive failures skip that provider for the rest of the run |
| Persistence | in-memory counters keyed by UTC date; no new table, no new migration |

The counters are process-local on purpose. A restart mid-run can spend a little of the daily budget
twice, which is acceptable; a migration and a table for this is not.

### 4. Settings

No new fields. `subdl` keeps `api_key`, `subsource` keeps `apikey`; the pacing constants are product
defaults, not knobs, per the governance rule in
[feature-bloat-governance.md](./feature-bloat-governance.md). A future "pro plan" flag is out of
scope.

### 5. Tests

`Lingarr.Server.Tests/Services/SubtitleDownload/` already has the right homes
(`SubtitleDownloadGatewayTests`, `SubtitleDownloadHttpTests`, `SubtitleDownloadReliabilityTests`).

Recorded fixtures, no network in tests:

1. A real SubSource `/subtitles` payload for a film with 16 English rows, asserting the poster URLs are
   never treated as a download, and that the chosen id is the expected `subtitleId`.
2. A SubSource TV payload where only `releaseInfo[]` carries `S01E01`, asserting a mismatched-episode
   row is refused and a matching one is taken.
3. A SubDL `unpack=1` season-pack payload, asserting the file whose `episode` equals the query is
   downloaded and the pack zip is not.
4. A `429` with `Retry-After: 120`, driven through a fake `TimeProvider`, asserting the provider is
   skipped for the next two minutes and that no second request is issued.
5. A daily-budget test: after the configured number of requests the provider is skipped with a single
   log line, not an exception.

### 6. Acceptance on the real deployment

Against the Bedroom instance with the two live keys: a missing English episode is fetched and placed
as a sidecar, `provider_operational_events` gains no `rate_limit` rows, and the SubDL account's
`downloads.remaining` drops by exactly one per download.

## Non-goals

- No change to translation providers or to the translation pipeline. This is source acquisition only.
- No new UI page, no per-provider rate settings, no scheduler.
- No removal of the other Bazarr-ported providers, even the ones that are similarly stale; each gets
  its own pass.

## Open questions

1. Should the SubSource text-search fallback use the media title or the file name? The title is
   cleaner; the file name occasionally disambiguates remakes.
2. Do we want the pacer to expose a health reading ("24 of 300 requests used today") on the providers
   page, or is the existing operational-event stream enough?
3. `ProviderHealthService` already maps 429 to a `rate_limit` reading. Should the pacer feed that
   service, so a cooled-down provider shows as degraded rather than healthy-with-no-hits?
