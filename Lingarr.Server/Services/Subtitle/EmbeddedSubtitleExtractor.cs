using System.Diagnostics;
using System.Text.Json;

namespace Lingarr.Server.Services.Subtitle;

public sealed record EmbeddedSubtitleStream(
    int Index,
    string Codec,
    string? Language,
    string? Title);

public enum EmbeddedSubtitleKind
{
    Text,
    Image,
    Caption
}

public sealed record EmbeddedExtractPlan(
    int StreamIndex,
    string DestinationFileName,
    EmbeddedSubtitleKind Kind);

public static class EmbeddedSubtitleExtractor
{
    private static readonly HashSet<string> TextCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "subrip", "srt", "ass", "ssa", "mov_text", "webvtt", "text"
    };

    private static readonly HashSet<string> ImageCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "hdmv_pgs_subtitle", "pgssub", "dvd_subtitle", "dvdsub", "dvb_subtitle", "xsub"
    };

    private static readonly HashSet<string> CaptionCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "eia_608", "eia_708", "dvb_teletext"
    };

    public static IReadOnlyList<EmbeddedSubtitleStream> ParseProbeJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("streams", out var streams))
        {
            return [];
        }

        var result = new List<EmbeddedSubtitleStream>();
        foreach (var stream in streams.EnumerateArray())
        {
            var index = stream.TryGetProperty("index", out var indexElement)
                ? indexElement.GetInt32()
                : -1;
            var codec = stream.TryGetProperty("codec_name", out var codecElement)
                ? codecElement.GetString() ?? ""
                : "";
            string? language = null;
            string? title = null;
            if (stream.TryGetProperty("tags", out var tags))
            {
                if (tags.TryGetProperty("language", out var languageElement))
                {
                    language = languageElement.GetString();
                }

                if (tags.TryGetProperty("title", out var titleElement))
                {
                    title = titleElement.GetString();
                }
            }

            result.Add(new EmbeddedSubtitleStream(index, codec, language, title));
        }

        return result;
    }

    public static EmbeddedExtractPlan? SelectEnglishTextTrack(
        IReadOnlyList<EmbeddedSubtitleStream> streams,
        string mediaFileName)
    {
        var candidates = streams
            .Where(stream => IsEnglish(stream) && TextCodecs.Contains(stream.Codec))
            .Where(stream => !IsSignsOnly(stream))
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var preferred = candidates.FirstOrDefault(stream => !IsSdh(stream)) ?? candidates[0];
        return Plan(preferred, mediaFileName, EmbeddedSubtitleKind.Text);
    }

    public static EmbeddedExtractPlan? SelectEnglishSourceTrack(
        IReadOnlyList<EmbeddedSubtitleStream> streams,
        string mediaFileName,
        NonTextSubtitlePolicy? policy = null)
    {
        policy ??= NonTextSubtitlePolicy.All;
        var text = SelectEnglishTextTrack(streams, mediaFileName);
        if (text != null)
        {
            return text;
        }

        var picture = PreferDialogue(streams.Where(stream =>
            IsEnglish(stream)
            && ImageCodecs.Contains(stream.Codec)
            && !IsSignsOnly(stream)
            && policy.Allows(stream.Codec)));
        if (picture != null)
        {
            return Plan(picture, mediaFileName, EmbeddedSubtitleKind.Image);
        }

        var caption = streams.FirstOrDefault(stream =>
            CaptionCodecs.Contains(stream.Codec)
            && policy.Allows(stream.Codec)
            && (string.IsNullOrWhiteSpace(stream.Language) || IsEnglish(stream)));
        return caption == null ? null : Plan(caption, mediaFileName, EmbeddedSubtitleKind.Caption);
    }

    private static EmbeddedSubtitleStream? PreferDialogue(IEnumerable<EmbeddedSubtitleStream> streams)
    {
        var list = streams.ToList();
        return list.FirstOrDefault(stream => !IsSdh(stream)) ?? list.FirstOrDefault();
    }

    private static EmbeddedExtractPlan Plan(
        EmbeddedSubtitleStream stream,
        string mediaFileName,
        EmbeddedSubtitleKind kind) =>
        new(
            stream.Index,
            SubtitleNaming.BuildDestinationFileName(
                mediaFileName,
                "en",
                kind == EmbeddedSubtitleKind.Image ? SubtitleCaption.Ocr : SubtitleCaption.None,
                ".srt"),
            kind);

    public static bool HasOnlyImageSubtitles(IReadOnlyList<EmbeddedSubtitleStream> streams) =>
        streams.Count > 0
        && streams.All(stream => ImageCodecs.Contains(stream.Codec));

    public static async Task<bool> TryExtractEnglish(
        string directory,
        string fileName,
        CancellationToken cancellationToken,
        NonTextSubtitlePolicy? policy = null,
        bool nonTextOnly = false)
    {
        var videoPath = FindVideo(directory, fileName);
        if (videoPath == null)
        {
            return false;
        }

        var probe = await Run(
            BuildProbeCommand(videoPath),
            TimeSpan.FromSeconds(45),
            cancellationToken);
        if (probe.ExitCode != 0)
        {
            return false;
        }

        var plan = SelectEnglishSourceTrack(ParseProbeJson(probe.StandardOutput), fileName, policy);
        if (plan == null || (nonTextOnly && plan.Kind == EmbeddedSubtitleKind.Text))
        {
            return false;
        }

        var destination = Path.Combine(directory, plan.DestinationFileName);
        if (File.Exists(destination))
        {
            return true;
        }

        return plan.Kind switch
        {
            EmbeddedSubtitleKind.Text => await ExtractText(videoPath, plan, destination, cancellationToken),
            EmbeddedSubtitleKind.Image => await ExtractImage(videoPath, plan, destination, cancellationToken),
            EmbeddedSubtitleKind.Caption => await ExtractCaption(videoPath, destination, cancellationToken),
            _ => false
        };
    }

    private static async Task<bool> ExtractText(
        string videoPath,
        EmbeddedExtractPlan plan,
        string destination,
        CancellationToken cancellationToken)
    {
        var extract = await Run(
            BuildExtractCommand(videoPath, plan.StreamIndex, destination),
            TimeSpan.FromMinutes(2),
            cancellationToken);
        return extract.ExitCode == 0 && File.Exists(destination);
    }

    private static async Task<bool> ExtractImage(
        string videoPath,
        EmbeddedExtractPlan plan,
        string destination,
        CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mks");
        try
        {
            var copy = await Run(
                BuildImageCopyCommand(videoPath, plan.StreamIndex, tempFile),
                TimeSpan.FromMinutes(3),
                cancellationToken);
            if (copy.ExitCode != 0 || !File.Exists(tempFile))
            {
                return false;
            }

            var ocr = await Run(
                BuildSeconvCommand(tempFile, Path.GetDirectoryName(destination)!, Path.GetFileName(destination)),
                TimeSpan.FromMinutes(15),
                cancellationToken);
            return ocr.ExitCode == 0 && new FileInfo(destination).Length > 0;
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    private static async Task<bool> ExtractCaption(
        string videoPath,
        string destination,
        CancellationToken cancellationToken)
    {
        var extract = await Run(
            BuildCaptionCommand(videoPath, destination),
            TimeSpan.FromMinutes(5),
            cancellationToken);
        return extract.ExitCode == 0 && File.Exists(destination);
    }

    public static ProcessStartInfo BuildImageCopyCommand(string videoPath, int streamIndex, string destinationPath) =>
        new()
        {
            FileName = "ffmpeg",
            ArgumentList =
            {
                "-y",
                "-i", videoPath,
                "-map", $"0:{streamIndex}",
                "-c", "copy",
                "-f", "matroska",
                destinationPath
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

    public static ProcessStartInfo BuildSeconvCommand(
        string imageFile,
        string outputDirectory,
        string outputFileName) =>
        new()
        {
            FileName = "seconv",
            ArgumentList =
            {
                imageFile,
                "subrip",
                "--ocr-engine:tesseract",
                "--ocr-language:eng",
                $"--output-folder:{outputDirectory}",
                $"--output-filename:{outputFileName}",
                "--no-language-suffix",
                "--overwrite"
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

    public static ProcessStartInfo BuildCaptionCommand(string videoPath, string destinationPath) =>
        new()
        {
            FileName = "ccextractor",
            ArgumentList = { videoPath, "-o", destinationPath, "-out=srt" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private static readonly string[] VideoExtensions = [".mkv", ".mp4", ".m4v", ".avi", ".ts"];

    private static string? FindVideo(string directory, string fileName)
    {
        try
        {
            foreach (var extension in VideoExtensions)
            {
                var exact = Path.Combine(directory, fileName + extension);
                if (File.Exists(exact))
                {
                    return exact;
                }
            }

            return Directory.EnumerateFiles(directory)
                .FirstOrDefault(path =>
                {
                    var stem = Path.GetFileNameWithoutExtension(path);
                    return stem.Equals(fileName, StringComparison.OrdinalIgnoreCase)
                           || stem.StartsWith(fileName + ".", StringComparison.OrdinalIgnoreCase);
                });
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> Run(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // The timed-out probe is already abandoned.
            }

            return (-1, await stdout, "timed out");
        }

        return (process.ExitCode, await stdout, await stderr);
    }

    public static ProcessStartInfo BuildProbeCommand(string videoPath) =>
        new()
        {
            FileName = "ffprobe",
            ArgumentList =
            {
                "-v", "error",
                "-select_streams", "s",
                "-show_entries", "stream=index,codec_name:stream_tags=language,title",
                "-of", "json",
                videoPath
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

    public static ProcessStartInfo BuildExtractCommand(
        string videoPath,
        int streamIndex,
        string destinationPath) =>
        new()
        {
            FileName = "ffmpeg",
            ArgumentList =
            {
                "-y",
                "-i", videoPath,
                "-map", $"0:{streamIndex}",
                "-c:s", "srt",
                destinationPath
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

    private static bool IsEnglish(EmbeddedSubtitleStream stream)
    {
        var language = SubtitleNaming.NormalizeLanguage(stream.Language);
        if (language == "en")
        {
            return true;
        }

        return string.Equals(stream.Language, "en", StringComparison.OrdinalIgnoreCase)
               || string.Equals(stream.Language, "eng", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSignsOnly(EmbeddedSubtitleStream stream) =>
        stream.Title != null
        && stream.Title.Contains("sign", StringComparison.OrdinalIgnoreCase)
        && !stream.Title.Contains("dialogue", StringComparison.OrdinalIgnoreCase);

    private static bool IsSdh(EmbeddedSubtitleStream stream) =>
        stream.Title != null
        && (stream.Title.Contains("sdh", StringComparison.OrdinalIgnoreCase)
            || stream.Title.Contains("caption", StringComparison.OrdinalIgnoreCase));
}
