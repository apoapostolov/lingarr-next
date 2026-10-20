# Subtitle downloader reliability validation

Validated on 2026-10-20 against the original 1.2.0 release at `1629a82` plus
the subtitle reliability changes. These changes are included in the revised
1.2.0 release, with no schema, provider-order, or version-number change.

## Evidence and change

- The host media root was readable while the old container's bind returned
  `No such device`. Recreating only Lingarr restored media-root access.
- Collection now names unavailable storage and supplies a storage/mapping hint.
  Downloaders skip missing folders and do not recreate them. A placement I/O or
  permission failure stops that item's downloads without aborting the caller.
- Bazarr retries defer unavailable folders within their existing retry window.
  HTTP errors and malformed responses no longer look like empty search results.
- HTML requests preserve configured headers through detail/download pages;
  explicit links to another origin do not receive configured cookies. Stale
  detail-page HTTP failures still allow later search results to be tried.
- JSON searches retain endpoint queries and resolve relative downloads. Download
  referrers omit query credentials. HTML query entities are decoded.
- Provider failures use warnings with failure types/statuses and omit raw
  credential-bearing exception text. Caller cancellation propagates.

## Checks

```bash
dotnet test Lingarr.Server.Tests -c Release
docker build -f Lingarr.Server/Dockerfile -t lingarr-next:subtitle-reliability \
  --build-arg TARGETARCH=amd64 --build-arg VERSION=1.2.0 --network=host .
LINGARR_URL=http://127.0.0.1:19876 python3 tests/smoke/smoke_api.py
```

- Six initial regression cases failed on the original implementation and passed
  with the patch. Final server suite: **570 passed, zero failed or skipped**.
- Docker server build and publish succeeded; the unchanged client build was
  reused from Docker's cache. Final image ID:
  `sha256:bdcdbf458ab4a0872dc816e4371771393411764c0d86d19ffe96fb74135f7ed3`.
- API smoke: **19 passed** on an isolated instance with temporary config storage
  after completing onboarding. Included a real Microsoft line/content
  translation and model ordering. The temporary instance was removed afterward.
- Production checks: healthy container, HTTP 200 version/plugin/download-provider
  routes, 47 download providers, readable movie/TV roots, and exact restoration
  of the saved translation chain after startup regenerated its row ID.
- A controlled missing-folder collection request returned an empty list and a
  warning with the new media-drive hint in the live logs.
- `git diff --check` passed.

## Deployment and limits

Only Lingarr was recreated through its current managed compose definition.
The live and workspace definitions matched; no mounts or credentials changed.
The previous image is retained as
`lingarr-next:before-subtitle-reliability-20261020` for rollback.

Provider HTTP behavior is covered by deterministic mocked requests. Authenticated
downloads against each real provider and a full-library processing cycle were
not run. External provider DNS failures/timeouts can still occur. Build output
also reports existing dependency advisories for SQLitePCLRaw and the Docker
restore of System.Security.Cryptography.Pkcs; dependency remediation is outside
this patch.
