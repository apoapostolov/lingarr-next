namespace Lingarr.Server.Providers;

public static class LogHints
{
    public static string? For(LogLevel level, string? message, string? exception)
    {
        if (level < LogLevel.Warning)
        {
            return null;
        }

        var text = $"{message}\n{exception}".ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (text.Contains("directory not found", StringComparison.Ordinal))
        {
            return "Check the folder mapping for this library under Settings → Connections → Path mapping.";
        }

        if (text.Contains("payload", StringComparison.Ordinal)
            && (text.Contains("not json", StringComparison.Ordinal)
                || text.Contains("missing", StringComparison.Ordinal)
                || text.Contains("no payload", StringComparison.Ordinal)))
        {
            return "The webhook body was empty or not JSON. Check the URL and the event in the sending app.";
        }

        if (text.Contains("not have a file yet", StringComparison.Ordinal)
            || text.Contains("not found in database", StringComparison.Ordinal))
        {
            return "Lingarr does not have this file yet. Wait for the library sync, or confirm the file in Radarr or Sonarr.";
        }

        if (text.Contains("subtitle is not valid", StringComparison.Ordinal))
        {
            return "The subtitle failed the quality check. Open the title and translate it again, or adjust the subtitle rules.";
        }

        if (text.Contains("picture subtitle", StringComparison.Ordinal))
        {
            return "The picture subtitle could not be read. Confirm the file is available and that picture tools are enabled under Settings → Translation → Subtitles.";
        }

        if (text.Contains("api settings are not configured", StringComparison.Ordinal))
        {
            return "Choose a translation service and save its address and key under Settings → Services.";
        }

        if (text.Contains("401", StringComparison.Ordinal)
            || text.Contains("403", StringComparison.Ordinal)
            || text.Contains("token was rejected", StringComparison.Ordinal)
            || text.Contains("unauthorized", StringComparison.Ordinal)
            || text.Contains("forbidden", StringComparison.Ordinal))
        {
            return "Check the API key or token for this service under Settings → Connections.";
        }

        if (text.Contains("could not be reached", StringComparison.Ordinal)
            || text.Contains("connection refused", StringComparison.Ordinal)
            || text.Contains("timed out", StringComparison.Ordinal)
            || text.Contains("timeout", StringComparison.Ordinal))
        {
            return "Check that the service is running and that Lingarr can reach its address.";
        }

        if (text.Contains("bazarr", StringComparison.Ordinal))
        {
            return "Open Settings → Connections and test the Bazarr connection.";
        }

        if (text.Contains("plex", StringComparison.Ordinal))
        {
            return "Open Settings → Connections and test the Plex connection.";
        }

        if (level >= LogLevel.Error
            && (text.Contains("translat", StringComparison.Ordinal)
                || text.Contains("provider", StringComparison.Ordinal)))
        {
            return "The translation service rejected the request. Test that service under Settings → Services.";
        }

        if (level >= LogLevel.Error)
        {
            return "Read the source and the details on this line. That is the technical cause.";
        }

        return "Lingarr continued. This line says what was skipped.";
    }
}
