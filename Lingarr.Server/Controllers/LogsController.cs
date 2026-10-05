using System.Text.Json;
using System.Text.Json.Serialization;
using Lingarr.Server.Attributes;
using Lingarr.Server.Providers;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[LingarrAuthorize]
[Route("api/[controller]")]
public class LogsController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Returns one page of in-memory logs, oldest first inside the page.
    /// Omit both cursors for the newest page. Use before to load older lines.
    /// </summary>
    [HttpGet]
    public IActionResult Get(
        [FromQuery] int limit = 80,
        [FromQuery] long? before = null,
        [FromQuery] long? after = null,
        [FromQuery] string? minLevel = null)
    {
        if (before != null && after != null)
        {
            return BadRequest(new { message = "Use before or after, not both." });
        }

        if (!TryParseLevel(minLevel, out var minimum))
        {
            return BadRequest(new { message = "Log level was not recognized." });
        }

        limit = Math.Clamp(limit, 1, 200);
        return Ok(InMemoryLogSink.Page(before, after, limit, minimum));
    }

    /// <summary>
    /// Waits until a matching line arrives, or until the timeout ends.
    /// </summary>
    [HttpGet("wait")]
    public async Task<IActionResult> Wait(
        [FromQuery] long after = 0,
        [FromQuery] string? minLevel = null,
        [FromQuery] int timeout = 25,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseLevel(minLevel, out var minimum))
        {
            return BadRequest(new { message = "Log level was not recognized." });
        }

        timeout = Math.Clamp(timeout, 1, 30);
        var items = await InMemoryLogSink.WaitAsync(
            after,
            minimum,
            TimeSpan.FromSeconds(timeout),
            cancellationToken);
        return Ok(new { items });
    }

    /// <summary>
    /// Follows new lines as server-sent events. event: log is every match.
    /// event: problem repeats a warning or error so a follower can listen for that alone.
    /// </summary>
    [HttpGet("stream")]
    public async Task Stream(
        [FromQuery] long? after,
        [FromQuery] string? minLevel,
        CancellationToken cancellationToken)
    {
        if (!TryParseLevel(minLevel, out var minimum))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsync("Log level was not recognized.", cancellationToken);
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Append("X-Accel-Buffering", "no");

        var cursor = after ?? InMemoryLogSink.LatestId;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var items = await InMemoryLogSink.WaitAsync(
                    cursor,
                    minimum,
                    TimeSpan.FromSeconds(15),
                    cancellationToken);
                if (items.Count == 0)
                {
                    await Response.WriteAsync(": ping\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                    continue;
                }

                foreach (var entry in items)
                {
                    var json = JsonSerializer.Serialize(entry, JsonOptions);
                    await Response.WriteAsync($"event: log\ndata: {json}\n\n", cancellationToken);
                    if (entry.LogLevel >= LogLevel.Warning)
                    {
                        await Response.WriteAsync($"event: problem\ndata: {json}\n\n", cancellationToken);
                    }

                    cursor = entry.Id;
                }

                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The follower closed the stream.
        }
    }

    [HttpDelete]
    public IActionResult Clear()
    {
        InMemoryLogSink.Clear();
        return Ok(new { message = "Logs cleared." });
    }

    private static bool TryParseLevel(string? value, out LogLevel? minimum)
    {
        minimum = null;
        if (string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Enum.TryParse<LogLevel>(value, ignoreCase: true, out var parsed) || parsed == LogLevel.None)
        {
            return false;
        }

        minimum = parsed;
        return true;
    }
}
