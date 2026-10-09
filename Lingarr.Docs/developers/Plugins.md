# Plugins

You can teach Lingarr a new trick without forking it. Build a small .NET
library, drop the DLL in a folder, and restart. Lingarr reads the DLL at
startup and gives it a tab, a card, and a button.

The folder comes from the `PLUGINS_PATH` environment variable. Leave that
variable empty and Lingarr skips plugins. Point it at a real folder and
Lingarr loads every DLL there. A DLL that fails to load is skipped. The rest
of the app still starts.

```text
PLUGINS_PATH=/plugins
```

A plugin shares the process with Lingarr. It can read files the server can
read. Install DLLs you trust. There is no sandbox and no hot reload. A change
means a new DLL and a restart.

## What you get

A plugin is a class library aimed at the same .NET version as Lingarr. It
references `Lingarr.Contracts` and nothing from the server project. That
package is the stable surface.

Mark the assembly:

```csharp
using Lingarr.Contracts.Plugins;

[assembly: LingarrPluginApiVersion(1, 0)]
```

The major number has to be 1. Lingarr ignores the minor number.

These types matter.

| You implement | Lingarr does |
| --- | --- |
| `IPluginManifest` | Lists your name, your settings, and any panels |
| `IPluginActionHandler` | Runs a button you declared |
| `ITranslationService` | Translates through your own service |
| `ISubtitlePostProcessor` | Rewrites the finished target file |
| `IExtractTool` | Extracts a track the built-in tools missed |
| `ISubtitleSource` | Drops a source subtitle when Bazarr missed |
| `IPluginCommand` | Runs one program, with a timeout and no shell |
| `IMediaServerPlugin` | Refreshes one item and selects its subtitle |
| `IWebhookInbox` | Turns your JSON into a queued movie or episode |
| `IPluginNotifier` | Hears when a translation finishes or fails |
| `IPluginTask` | Adds a UTC cron job under System → Tasks |
| `IMediaAction` | Runs one action against one title |

Put `[PluginProvider("your-id")]` on a translation class and on an action
class. The id is what you show up as. Skip ids Lingarr already uses for
built-in translators: `anthropic`, `openai`, `gemini`, `deepseek`,
`localai`, `deepl`, `libretranslate`, `google`, `bing`, `microsoft`,
`yandex`, and the other built-in model providers.

## A tab of your own

`IPluginManifest.Panels` is how you get screen space. Each entry is its own
card. Several plugins can share a tab. Lingarr draws the cards under one
wide header, in two columns, and lets a short card sit beside a tall one.
The columns end up close to the same height.

Put those cards in the `plugins` section. A plugin can also use
`connections`, `translation`, `automation`, or `system`.

```csharp
public IReadOnlyList<PluginPanelContribution> Panels { get; } =
[
    new()
    {
        Id = "desk",
        Section = "plugins",
        TabId = "style",
        TabLabel = "Style",
        Title = "House style",
        Description = "Punches up the file after translation.",
        Fields =
        [
            new()
            {
                Key = "house_style_quotes",
                Label = "Use French quotes",
                Type = PluginSettingType.Toggle,
                Default = "false"
            }
        ],
        Actions = [new() { Id = "test", Label = "Test" }]
    }
];
```

Field types include text, URL, secret, toggle, a remote dropdown, and OAuth.
A secret is stored encrypted. Lingarr saves a value only when the key belongs
to your manifest or your panel. A write to someone else's key is refused.

The Test button needs a class Lingarr can construct:

```csharp
[PluginProvider("house-style")]
public sealed class HouseStyleActions : IPluginActionHandler
{
    private readonly ISettingsAccess _settings;

    public HouseStyleActions(ISettingsAccess settings)
    {
        _settings = settings;
    }

    public string Provider => "house-style";

    public async Task<string> ExecuteAsync(
        string actionId,
        CancellationToken cancellationToken)
    {
        var enabled = await _settings.GetSettingAsync("house_style_quotes");
        return enabled == "true"
            ? "House style test succeeded."
            : "House style is off.";
    }
}
```

`ISettingsAccess` reads settings. The Settings screen writes them.

Open Settings, then Plugins. Every plugin starts off. A plugin that has cards
shows them dim until you use the switch on the card. Each third-party plugin
has its order and failure policy on Installed.

- **Enabled.** The switch sits in the upper right of the plugin's card. Off
  dims that card, and its buttons do not run.
- **Order.** Lower numbers run first once a plugin does pipeline work. Cards
  on a shared tab follow the same number.
- **Failure policy.** Continue after an error, stop this file and keep
  earlier edits, or fail the translation and keep the old file. Lingarr
  stores the choice with the plugin.

`samples/StyleSample` registers two tabs under Plugins, Style and Language.
International quotes and the style sample share Style. Spanish language sits on
Language. Build it, point `PLUGINS_PATH` at the output folder, and restart.
Those cards stay on their tabs when the plugin is off, and the card is dim
until you turn it on. A switch on a card survives a restart because it is an
ordinary settings row.

## After the translation

Lingarr writes the target subtitle first. `name.fr.srt` for a normal file,
`name.fr.ocr.srt` when the English source was OCR. Then it runs
`ISubtitlePostProcessor` plugins that are enabled on Settings → Plugins.

The order is fixed:

1. Language processors, lowest Order number first.
2. Style processors, same rule.
3. Line fitters.
4. Quality gates. A rejection stops the rest and keeps the text from the
   previous step.

A language plugin fixes the words. Spanish language puts ¿ and ¡ back on
dialogue lines. A style plugin changes how the line looks. International
quotes follows the target language. A Japanese file becomes 「hi」. The
dropdown can overwrite that with another country, so the same file can use
German „hi“ instead. Timestamps stay as they
are. The source subtitle is never deleted. The `ocr` token in the file name
stays.

Each processor returns the full text of the target file, or nothing if it
has no change. Lingarr writes that text to a temporary file beside the
subtitle and replaces the target in one move. A crash in the middle leaves
the previous file in place.

The plugin starts off. Two switches matter. Enabled on the Plugins page lets
it run. International quotes then uses the country in its dropdown.
Spanish marks has its own toggle. Leave the plugin off and the file is
unchanged.

Failure policy decides a thrown plugin:

- **Continue after an error.** Lingarr logs the error and runs the next
  plugin. Edits from earlier plugins still apply.
- **Stop this file, keep earlier edits.** Lingarr runs nothing else on this
  file.
- **Fail the translation, keep the old file.** Lingarr does not replace the
  file. The translation
  request is marked failed. The original translation stays on disk.

```csharp
[PluginProvider("international-quotes")]
public sealed class InternationalQuotesProcessor : ISubtitlePostProcessor
{
    public string Provider => "international-quotes";
    public SubtitlePostProcessKind Kind => SubtitlePostProcessKind.Style;

    public Task<SubtitlePostProcessResult> ProcessAsync(
        SubtitlePostProcessInput input,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new SubtitlePostProcessResult
        {
            Text = InternationalQuotesProcessor.Apply(input.TargetText, "fr")
        });
    }
}
```

`input.TargetText` is the subtitle as it stands. `input.SourceIsOcr` is true
when the source or the target name carries `ocr`. `input.SourcePath` is
yours to read. Leave that file on disk.

A quality gate sets `Reject` and a `Reason` instead of new text. Lingarr
logs the reason and keeps the subtitle from the previous step.

## A command, or a subtitle you already have

`IPluginCommand` runs one program. You pass the executable and each argument
on its own. Lingarr refuses `bash`, `sh`, and `powershell`. The call has a
timeout. When the time is up, Lingarr stops the process.

`IExtractTool` runs after ffmpeg, seconv, and ccextractor miss. Fixture
extract looks for `clip.track` beside the video and copies it to
`clip.en.srt` with `/bin/cp`. That is a stand-in for a converter you trust.

`ISubtitleSource` runs when Bazarr is off, or Bazarr already searched and
missed. It does not run on a hit, so two downloaders do not fight. Desk
source copies `clip.desk.txt` to `clip.en.srt`. Lingarr then sees that
sidecar the same way it sees a file you dropped in the folder, and the
normal translation path takes over.

Both start off. Enable them on the Plugins page. Lower Order runs first.
The first source that writes a file wins.

```csharp
public async Task<bool> TrySupplyAsync(
    SubtitleSourceRequest request,
    CancellationToken cancellationToken)
{
    var notes = Path.Combine(
        request.Directory,
        request.MediaFileName + ".desk.txt");
    if (!File.Exists(notes))
    {
        return false;
    }

    var destination = Path.Combine(
        request.Directory,
        request.MediaFileName + "." + request.Language + ".srt");
    File.WriteAllText(destination, File.ReadAllText(notes));
    return true;
}
```

A German archive can work the same way. Drop `film.desk.txt` next to the
video, enable Desk source, and Lingarr translates `film.en.srt` on the next
pass. A Japanese tool that emits SRT can be an extract plugin: point
`IPluginCommand` at that binary, pass the video path as one argument, and
write `name.ja.srt` only when your tool exits 0.

## Servers, notices, and chores

Plex, Jellyfin, and Emby stay built in. A plugin can stand beside them.

`IMediaServerPlugin` runs after a translation, once Plex has had its turn.
`RefreshItemAsync` updates one title. `SelectSubtitleAsync` receives
`item.SubtitlePath` and `item.Language`. Lingarr logs a failure here. The
translated file stays on disk.

```csharp
public Task SelectSubtitleAsync(
    MediaItemRef item,
    CancellationToken cancellationToken)
{
    return SelectOnYourServer(item.SubtitlePath, item.Language);
}
```

`item.SubtitlePath` is the subtitle Lingarr just wrote, such as
`Amélie.fr.srt`. `item.Language` is the target code, such as `fr`.

`IWebhookInbox` has its own URL:

```text
POST /api/webhook/plugin/fixture-inbox
```

The body is yours. Return a movie or an episode with a title, and Lingarr
queues it the same way it queues a Jellyfin or Emby add. Return `Ignore` for
playback and other noise. The sample accepts this:

```json
{"event":"added","title":"Amélie","year":2001,"tmdb":"194"}
```

`IPluginNotifier` hears the end of a translation. `Succeeded` is true when
the file was written. `Title` is the subtitle file name. Lingarr logs a
throw and still counts the translation as finished.

```csharp
public Task NotifyAsync(
    PluginNotice notice,
    CancellationToken cancellationToken)
{
    return Send(notice.Succeeded, notice.Title);
}
```

`IPluginTask` is a cron job in UTC. Morning task uses `0 4 * * *`. After you
enable it and restart, System → Tasks lists `plugin-morning-task`.

```csharp
public string Cron => "0 4 * * *";

public Task ExecuteAsync(CancellationToken cancellationToken) =>
    Task.CompletedTask;
```

`IMediaAction` runs against one title:

```text
POST /api/plugin/mark-title/media/mark
{"title":"Spirited Away"}
```

```csharp
public Task<string> RunAsync(
    MediaItemRef item,
    CancellationToken cancellationToken) =>
    Task.FromResult($"Marked {item.Title}.");
```

The answer is one sentence, such as “Marked Spirited Away.”

These five start off. Enable the one you want on the Plugins page. A task
shows up under Tasks only after the restart that follows that switch.

## Beside the finished file

More hooks sit around that same file. They start off. Enable one on the
Plugins page. Lower Order runs first.

`IContentFilter` sees the target text before language and style processors.
Hearing filter clears a line that is only a bracketed cue, such as
`[music]`, and leaves the dialogue and the timestamps. Lingarr logs a throw,
runs the next filter, and still finishes the translation.

```csharp
public Task<string> FilterAsync(
    string subtitleText,
    CancellationToken cancellationToken)
{
    var lines = subtitleText.Replace("\r\n", "\n").Split('\n');
    for (var index = 0; index < lines.Length; index++)
    {
        if (lines[index].Trim() == "[music]")
        {
            lines[index] = "";
        }
    }

    return Task.FromResult(string.Join("\n", lines));
}
```

`IGlossary` collects names for the target language. Lingarr joins them and
sets `input.Glossary` on every post-processor. Cast glossary returns Amélie
and Nino for `fr`, and Chihiro and Haku for `ja`. Your processor can prefer
those spellings. The model request stays the one Lingarr already built.

`IFileTool` runs once the target file is on disk. Trailing line adds a final
newline when the file has none. Lingarr logs a throw. The translation still
counts as finished.

`ILogSink` receives a recent log line: level, category, and message. Lingarr
keeps a short queue and drops a line when that queue is full, so a slow sink
leaves the server log fast. Shelf log stores the latest warning. Keep
`Write` free of its own log calls. Lingarr drops that second line.

```csharp
public void Write(string level, string category, string message)
{
    if (level == "Warning" || level == "Error")
    {
        ShelfNotes.LastWarning = message;
    }
}
```

`IPluginHealthCheck` returns one sentence.

```text
POST /api/plugin/shelf-health/health
```

Shelf health answers “Shelf is ready.” With the plugin off, the same call
answers “Plugin health check ignored.”

`IDashboardWidget` adds a card on the dashboard. Shelf widget uses the title
Shelf. The sentence is “Shelf has not seen a file yet.” until a watch plugin
records a name.

`IMediaEventSink` runs when Lingarr starts work on a file.
`OnDiscoveredAsync` receives the directory and the file name. Either value
can be empty. Shelf watch stores the file name, and the widget then says
“Shelf last saw Amélie.mkv.”

Lingarr logs a throw from a filter, a glossary, a file tool, a log sink, a
notice, a widget, or a watch. The subtitle written by the translation stays.

## Paths, lists, and the next try

These hooks start off. Enable one on the Plugins page. The built-in path
table, SRT, ASS, and SSA, Sonarr, Radarr, and the Bazarr retry window stay
in place.

`IPathMapper` runs after that table. Film map rewrites `/server/films` to
`/media/films`.

`ILibraryAgent` runs when the mapped folder is not on disk. It receives an
external id and a title. Shelf library returns a folder only when you point
it at one. Radarr and Sonarr stay the default.

`ISidecarCodec` reads and writes one extra format. Built-in `.srt`, `.ass`,
and `.ssa` stay with Lingarr. Sub codec reads a `.sub` file whose text
already contains `-->`.

`ICaptionPolicy` can mark a file SDH, forced, hearing-impaired, or plain,
and say whether it may be a source. SDH policy marks caption `sdh` as
ineligible. A plain English file still translates.

`IListBadge` adds a short label on the translation list. OCR badge shows
`ocr` when the file name carries that tag. The host draws it.

`IRetryPolicy` can shorten or stop another try. Quiet retry stops a Bazarr
search when the file name contains `quiet-retry`. The 12 hour / 168 hour
window stays the default when the plugin has no advice.

`ISubtitleMerge` chooses what to do when a better source arrives for an OCR
file. OCR merge keeps the OCR subtitle when the incoming name contains
`.keep.`. Otherwise Lingarr keeps its usual replace rule.

`IPromptContributor` adds a named block for providers that already take
instruction profiles. Name prompt adds “Keep Amélie and Nino spelled as
written.” for French, and the Chihiro and Haku line for Japanese. The
plugin receives the language codes. It does not receive the profile store.

`IStatisticsExporter` receives movie, episode, and subtitle-file counts
after the statistics job saves them. Count export remembers those three
numbers. Paths stay out.

## Practical uses

Pick one annoyance in your own library.

**A private translator.** Your office runs a model on a machine Lingarr does
not know. Implement `ITranslationService`, add the URL and the key as
settings, and pick that provider like any built-in service. The Cloudflare
sample in `samples/CloudflarePlugin` is that shape: one manifest, one
translator, one account id, one token.

**A desk for a weird token.** Some tool wants three headers and a tenant id.
A panel under Connections keeps those fields next to Radarr and Sonarr.
Secrets stay encrypted. A Test button calls the tool and returns one
sentence.

**Dialogue marks and Spanish marks.** International quotes picks a country
and rewrites straight quotes. Spanish marks restores ¿ and ¡. Turn the
plugin on, and the next translation picks up the change. Details are under
After the translation.

**A name list for a show.** A model keeps renaming a character every episode.
A text field on your plugin card holds the preferred spelling. The translator
you wrote reads that field through `ISettingsAccess` before it sends the
line.

**A health ping for a box you own.** The Test action is enough. It reads the
URL you stored, hits `/health`, and answers “Desk translator connection
succeeded.” or “Desk translator could not be reached.” Shelf health is the
smaller version: one sentence, no settings.

**Hearing-impaired cues.** Hearing filter drops a `[music]` line from the
French file after translation. The spoken line stays, and so do the
timestamps.

**Names that must stay spelled one way.** Cast glossary puts Amélie and Nino
on `input.Glossary` for a style plugin, and Chihiro and Haku for
Japanese. The next episode uses the same list.

**A line on the dashboard.** Enable Shelf watch and Shelf widget. The home
page names the last file Lingarr started.

## Build and load

From the repository root:

```bash
dotnet build samples/StyleSample/StyleSample.csproj -c Release
```

Copy `Lingarr.Plugin.StyleSample.dll` into the plugins folder, or point
`PLUGINS_PATH` at `samples/StyleSample/bin/Release/net10.0`. Restart Lingarr.
The log line you want looks like this:

```text
Loaded plugin /plugins/Lingarr.Plugin.StyleSample.dll (26 manifest(s)).
```

Lingarr also creates the settings rows on that startup. Copy a new DLL and
the first save can fail until that restart has happened. The message tells
you to restart and try again.

## Rules that save you a night

- Depend on `Lingarr.Contracts` only.
- Keep dependency versions compatible with the Lingarr you run. Plugins load
  into the default assembly context, so two copies of one library can
  collide.
- Use snake_case keys with your own prefix, such as `house_style_quotes`.
- Return one plain sentence from an action. Name the system, then the
  outcome.
- Ask only for keys you declared.
- Read the shared HTTP timeout and retry with `GetHttpSettingsAsync` on
  `ISettingsAccess`, and use those numbers for your own requests.
