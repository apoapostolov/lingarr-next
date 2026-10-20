# Subtitle housekeeping

Weekly Hangfire job `SubtitleMaintenanceJob` prepares sidecars so automation
can see them. It runs only while **Scan all folders on a schedule** is on.
That switch is paused by default, so the job does not walk the library.

## Name standardization

Matches orphan sidecars in the same folder to the Radarr/Sonarr `file_name`,
then renames them to `{file_name}.{lang}[.sdh|.hi|.forced].srt`.

- Same folder only. Different `SxxExx` never mix.
- `eng` becomes `en`, `bul` becomes `bg`.
- Destination exists → skip.
- Clears `media_hash` so the next translation cycle re-evaluates the item.

## Embedded extract

Optional. Only runs when no matching English sidecar exists.

- `ffprobe` headers, then `ffmpeg` extract of one English **text** track.
- Skips PGS / VobSub / DVB image subs.
- Prefers dialogue over signs, and a non-SDH track when both exist.
- Capped by `subtitle_extract_max_per_run` (default 80).
- Stops after three I/O errors so a sick USB DAS does not get hammered.

Requires `ffmpeg` in the image.

## Subtitle download reliability

Collection reads existing media folders. An absent or inaccessible folder is
reported as unavailable, not as an empty provider search. Check the storage
mount and path mapping before reindexing. A healthy API does not confirm that
the media mount is readable.

Built-in downloaders check the destination folder before searching and never
create a missing media folder. A placement I/O or permission error stops further
languages for that item while allowing the library job to continue. Bazarr
retries defer unavailable folders using the existing interval and deadline.
HTTP errors and malformed Bazarr responses are search failures, not score-floor
misses. Cancellation still propagates to the caller.

HTML providers carry configured User-Agent and same-origin cookies through
search, detail, and download requests. HTML query separators are decoded. JSON
providers retain existing endpoint queries and resolve relative download URLs
against the search endpoint. Provider warnings name the failure type and HTTP
status without printing credential-bearing request URLs or exception text.

## Media servers

After changes, the job asks Jellyfin (`/Library/Media/Updated`) and Plex
(item refresh) to re-read the folder. Missing URL or token → rename/extract
still happen; the existing host refresh cron can catch up.

Plex credentials are entered on **Settings → Connections → Media servers**.
Sign in opens the Plex PIN page, or paste the server address and an
`X-Plex-Token`. `PLEX_URL` and `PLEX_TOKEN` are used until you sign in or
sign out on that card. Sign out stops using the environment token. A sign-in
on the card is kept, and a later restart does not replace that address.

The same card can select one default subtitle language. After a movie or
episode translation into that language, Lingarr refreshes the matching Plex
item and marks the new subtitle selected. The Plex item is matched by file
path, by tmdb/imdb/tvdb id, or by its title, original title, and slug, so a
library title such as an international name still matches. A refresh often
misses a sidecar
whose name has more than one extension, such as `Movie.bg.srt`. When the new
stream does not appear, Lingarr uploads that file onto the item with an
explicit language code, then selects it.

Plex can also notify Lingarr when a movie is added. Paste the URL from
Settings → Connections → Media servers into Plex under Settings → Webhooks.
Lingarr handles `library.new` for movies and episodes. A movie matches
`{tmdb-id}`, `{imdb-id}`, or `{tvdb-id}` in the Lingarr path, and Radarr is
asked for that id when the movie is not in Lingarr yet. An episode matches the
show name plus the season and episode number, or the show id from Plex when
the title differs. Sonarr is asked when that episode is not in Lingarr yet.
Translation still requires a source subtitle file beside the file and a missing
target subtitle. An English track that exists only inside the video is skipped
until housekeeping has extracted it. Movies and episodes each have a
switch, both on by default.

Settings / env:

| Key / env | Purpose |
| --- | --- |
| `subtitle_naming_enabled` / `SUBTITLE_NAMING_ENABLED` | Default true |
| `subtitle_extract_enabled` / `SUBTITLE_EXTRACT_ENABLED` | Default false |
| `subtitle_maintenance_schedule` | Default `0 3 * * 0` (Sunday 03:00 UTC) |
| `PLEX_URL`, `PLEX_TOKEN`, `PLEX_TOKEN_FILE` | Optional. |
| `plex_set_selected_subtitle` | Off until enabled. |
| `plex_default_subtitle_language` | Language selected in Plex. |
| `plex_translate_movies_on_library_new` | On. New Plex movies. |
| `plex_translate_episodes_on_library_new` | On. New Plex episodes. |
| `JELLYFIN_URL`, `JELLYFIN_API_KEY`, `JELLYFIN_TOKEN_FILE` | Optional |

Trigger housekeeping from **Settings → System → Tasks**, then run
SubtitleMaintenanceJob.
