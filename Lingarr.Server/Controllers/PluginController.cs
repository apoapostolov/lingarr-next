using Lingarr.Contracts.Models;
using Lingarr.Contracts.Plugins;
using Lingarr.Server.Attributes;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Services.Plugins;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[LingarrAuthorize]
[Route("api/[controller]")]
public class PluginController : ControllerBase
{
    private readonly IPluginRegistry _registry;
    private readonly ISettingService _settings;
    private readonly ITranslationServiceFactory _translationServiceFactory;
    private readonly IModelCatalogService _modelCatalog;
    private readonly IEnumerable<IPluginActionHandler> _actions;
    private readonly IEnumerable<ISubtitlePostProcessor> _postProcessors;
    private readonly IEnumerable<IExtractTool> _extractTools;
    private readonly IEnumerable<ISubtitleSource> _sources;
    private readonly PluginLoader _plugins;
    private readonly PluginSignals _signals;
    private readonly ILogger<PluginController> _logger;

    public PluginController(
        IPluginRegistry registry,
        ISettingService settings,
        ITranslationServiceFactory translationServiceFactory,
        IModelCatalogService modelCatalog,
        IEnumerable<IPluginActionHandler> actions,
        IEnumerable<ISubtitlePostProcessor> postProcessors,
        IEnumerable<IExtractTool> extractTools,
        IEnumerable<ISubtitleSource> sources,
        PluginLoader plugins,
        PluginSignals signals,
        ILogger<PluginController> logger)
    {
        _registry = registry;
        _settings = settings;
        _translationServiceFactory = translationServiceFactory;
        _modelCatalog = modelCatalog;
        _actions = actions;
        _postProcessors = postProcessors;
        _extractTools = extractTools;
        _sources = sources;
        _plugins = plugins;
        _signals = signals;
        _logger = logger;
    }

    /// <summary>
    /// Lists every registered plugin together with the settings fields needed to render its
    /// configuration form.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PluginResponse>>> List()
    {
        var responses = new List<PluginResponse>();
        foreach (var plugin in _registry.All)
        {
            responses.Add(await BuildResponse(plugin));
        }
        return Ok(responses);
    }

    /// <summary>
    /// Tabs and panels contributed by enabled plugins.
    /// </summary>
    [HttpGet("ui")]
    public async Task<ActionResult<object>> Ui()
    {
        var tabs = new List<object>();
        var seenTabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var panels = new List<object>();
        foreach (var plugin in _registry.All.Where(item => !item.IsBuiltIn))
        {
            var response = await BuildResponse(plugin);
            foreach (var panel in plugin.Manifest.Panels.Where(PluginCatalog.PanelIsValid))
            {
                var tabKey = panel.Section + "\n" + panel.TabId;
                if (seenTabs.Add(tabKey))
                {
                    tabs.Add(new
                    {
                        section = panel.Section,
                        tabId = panel.TabId,
                        label = panel.TabLabel,
                        provider = plugin.Manifest.Provider
                    });
                }

                panels.Add(new
                {
                    provider = plugin.Manifest.Provider,
                    panel.Id,
                    panel.Section,
                    panel.TabId,
                    panel.TabLabel,
                    panel.Title,
                    panel.Description,
                    panel.Fields,
                    panel.Actions,
                    enabled = response.Enabled,
                    order = response.Order,
                    failurePolicy = response.FailurePolicy
                });
            }
        }

        return Ok(new { tabs, panels });
    }

    /// <summary>
    /// Dashboard lines from enabled widget plugins.
    /// </summary>
    [HttpGet("widgets")]
    public async Task<ActionResult<IReadOnlyList<PluginWidgetText>>> Widgets(CancellationToken cancellationToken)
    {
        return Ok(await _signals.WidgetsAsync(cancellationToken));
    }

    /// <summary>
    /// One sentence from an enabled health-check plugin.
    /// </summary>
    [HttpPost("{provider}/health")]
    public async Task<ActionResult<object>> Health(string provider, CancellationToken cancellationToken)
    {
        var message = await _signals.ProbeAsync(provider, cancellationToken);
        if (message == null)
        {
            return NotFound(new { message = "Lingarr could not find that health check." });
        }

        if (message.Length == 0)
        {
            return Ok(new { message = "Plugin health check ignored." });
        }

        return Ok(new { message });
    }

    /// <summary>
    /// Returns the manifest for one provider.
    /// </summary>
    [HttpGet("{provider}/manifest")]
    public async Task<ActionResult<PluginResponse>> GetManifest(string provider)
    {
        var plugin = _registry.Find(provider);
        if (plugin is null)
        {
            return NotFound();
        }

        return Ok(await BuildResponse(plugin));
    }

    [HttpGet("{provider}/settings")]
    public async Task<IActionResult> GetSettings(string provider)
    {
        var plugin = FindThirdParty(provider);
        if (plugin is null)
        {
            return NotFound();
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in PluginCatalog.Fields(plugin.Manifest))
        {
            if (field.Type == PluginSettingType.Secret)
            {
                continue;
            }

            values[field.Key] = await _settings.GetSetting(field.Key) ?? field.Default ?? string.Empty;
        }

        return Ok(new { values });
    }

    [HttpPut("{provider}/settings")]
    public async Task<IActionResult> SaveSettings(string provider, [FromBody] PluginSettingsWrite body)
    {
        var plugin = FindThirdParty(provider);
        if (plugin is null)
        {
            return NotFound();
        }

        if (body.Values is null || body.Values.Count == 0)
        {
            return BadRequest(new { message = "Choose a setting to save." });
        }

        foreach (var pair in body.Values)
        {
            if (!PluginCatalog.OwnsKey(plugin.Manifest, pair.Key))
            {
                return BadRequest(new { message = "That setting is not part of this plugin." });
            }
        }

        foreach (var pair in body.Values)
        {
            var field = PluginCatalog.Fields(plugin.Manifest)
                .First(item => string.Equals(item.Key, pair.Key, StringComparison.OrdinalIgnoreCase));
            var value = pair.Value ?? string.Empty;
            if (field.Type == PluginSettingType.Toggle && value is not ("true" or "false"))
            {
                return BadRequest(new { message = $"Enter true or false for {field.Label}." });
            }

            var saved = field.Type == PluginSettingType.Secret
                ? await _settings.SetEncryptedSetting(field.Key, value)
                : await _settings.SetSetting(field.Key, value);
            if (!saved)
            {
                return BadRequest(new { message = "Restart Lingarr after adding the plugin, then save again." });
            }
        }

        return Ok(new { message = "Plugin settings saved." });
    }

    [HttpPut("{provider}/host")]
    public async Task<IActionResult> SaveHost(string provider, [FromBody] PluginHostWrite body)
    {
        var plugin = FindThirdParty(provider);
        if (plugin is null)
        {
            return NotFound();
        }

        if (!PluginCatalog.TryNormalizePolicy(body.FailurePolicy, out var policy))
        {
            return BadRequest(new { message = "Failure policy must be skip, stop, or fail." });
        }

        var id = plugin.Manifest.Provider;
        var saved = await _settings.SetSettings(new Dictionary<string, string>
        {
            [PluginCatalog.EnabledKey(id)] = body.Enabled ? "true" : "false",
            [PluginCatalog.OrderKey(id)] = PluginCatalog.ParseOrder(body.Order.ToString()).ToString(),
            [PluginCatalog.PolicyKey(id)] = policy
        });
        if (!saved)
        {
            return BadRequest(new { message = "Restart Lingarr after adding the plugin, then save again." });
        }

        return Ok(new { message = "Plugin settings saved." });
    }

    [HttpPost("{provider}/actions/{actionId}")]
    public async Task<IActionResult> RunAction(string provider, string actionId, CancellationToken cancellationToken)
    {
        var plugin = FindThirdParty(provider);
        if (plugin is null)
        {
            return NotFound();
        }

        var known = plugin.Manifest.Panels
            .SelectMany(panel => panel.Actions)
            .Any(item => string.Equals(item.Id, actionId, StringComparison.OrdinalIgnoreCase));
        if (!known)
        {
            return NotFound();
        }

        var enabled = PluginCatalog.IsEnabled(await _settings.GetSetting(PluginCatalog.EnabledKey(plugin.Manifest.Provider)));
        if (!enabled)
        {
            return BadRequest(new { message = $"Turn on {plugin.Manifest.DisplayName} on the Plugins page." });
        }

        var handler = _actions.FirstOrDefault(item =>
            string.Equals(item.Provider, plugin.Manifest.Provider, StringComparison.OrdinalIgnoreCase));
        if (handler is null)
        {
            return NotFound();
        }

        var message = await handler.ExecuteAsync(actionId, cancellationToken);
        return Ok(new { message });
    }

    [HttpPost("{provider}/media/{actionId}")]
    public async Task<IActionResult> MediaAction(
        string provider,
        string actionId,
        [FromBody] MediaActionBody? body,
        CancellationToken cancellationToken)
    {
        var message = await _signals.RunMediaActionAsync(
            provider,
            actionId,
            new MediaItemRef
            {
                Title = string.IsNullOrWhiteSpace(body?.Title) ? "Untitled" : body.Title,
                Path = body?.Path,
                FileName = body?.FileName,
                Year = body?.Year
            },
            cancellationToken);
        if (message == null)
        {
            return NotFound();
        }

        return Ok(new { message });
    }

    /// <summary>
    /// Fetches the provider's AI model catalogue for manifest dropdowns. Returns 404 when the
    /// provider is unknown; providers without a model catalogue (for example DeepL or
    /// LibreTranslate) return an empty response.
    /// </summary>
    [HttpGet("{provider}/models")]
    public async Task<ActionResult<ModelsResponse>> GetModels(string provider, [FromQuery] bool refresh = false)
    {
        var plugin = _registry.Find(provider);
        if (plugin is null)
        {
            return NotFound();
        }

        try
        {
            var models = await _modelCatalog.GetModelsAsync(plugin.Manifest.Provider, refresh);
            return Ok(models);
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to list models for provider {Provider}.",
                plugin.Manifest.Provider);
            return Ok(new ModelsResponse
            {
                Message = "Error fetching models: " + exception.Message
            });
        }
    }

    /// <summary>
    /// Returns the configuration status for a provider.
    /// </summary>
    [HttpGet("{provider}/status")]
    public async Task<ActionResult<PluginStatusResponse>> GetStatus(string provider)
    {
        var plugin = _registry.Find(provider);
        if (plugin is null)
        {
            return NotFound();
        }

        var missingFields = new List<string>();
        foreach (var field in plugin.Manifest.Settings)
        {
            if (!field.Required)
            {
                continue;
            }

            string? settingValue;
            if (field.Type == PluginSettingType.Secret)
            {
                settingValue = await _settings.GetEncryptedSetting(field.Key);
            }
            else
            {
                settingValue = await _settings.GetSetting(field.Key);
            }

            if (string.IsNullOrEmpty(settingValue))
            {
                missingFields.Add(field.Key);
            }
        }

        return Ok(new PluginStatusResponse
        {
            Provider = plugin.Manifest.Provider,
            Configured = missingFields.Count == 0,
            MissingFields = missingFields
        });
    }

    private RegisteredPlugin? FindThirdParty(string provider)
    {
        var plugin = _registry.Find(provider);
        if (plugin is null || plugin.IsBuiltIn)
        {
            return null;
        }

        return plugin;
    }

    private async Task<PluginResponse> BuildResponse(RegisteredPlugin plugin)
    {
        var enabled = true;
        var order = 100;
        var policy = PluginCatalog.PolicySkip;
        if (!plugin.IsBuiltIn)
        {
            var id = plugin.Manifest.Provider;
            var stored = await _settings.GetSettings(PluginCatalog.HostKeys(id));
            enabled = PluginCatalog.IsEnabled(Value(stored, PluginCatalog.EnabledKey(id)));
            order = PluginCatalog.ParseOrder(Value(stored, PluginCatalog.OrderKey(id)));
            PluginCatalog.TryNormalizePolicy(Value(stored, PluginCatalog.PolicyKey(id)), out policy);
        }

        var translates = plugin.IsBuiltIn || _plugins.ProvidesTranslation(plugin.Manifest.Provider);
        return new PluginResponse
        {
            Provider = plugin.Manifest.Provider,
            DisplayName = plugin.Manifest.DisplayName,
            Description = plugin.Manifest.Description,
            IsBuiltIn = plugin.IsBuiltIn,
            SourceFile = plugin.SourceFile,
            Settings = plugin.Manifest.Settings,
            HasRequestTemplate = plugin.Manifest.HasRequestTemplate,
            SupportsInstructionProfiles = plugin.Manifest.SupportsInstructionProfiles,
            Enabled = enabled,
            Order = order,
            FailurePolicy = policy,
            Capabilities = Capabilities(plugin, translates),
            Panels = plugin.Manifest.Panels
        };
    }

    private IReadOnlyList<string> Capabilities(RegisteredPlugin plugin, bool translates)
    {
        var capabilities = PluginCatalog.Capabilities(
            plugin.Manifest,
            translates,
            _plugins.ProvidesAction(plugin.Manifest.Provider)).ToList();
        if (_plugins.ProvidesPostProcess(plugin.Manifest.Provider))
        {
            var kind = _postProcessors.FirstOrDefault(item =>
                string.Equals(item.Provider, plugin.Manifest.Provider, StringComparison.OrdinalIgnoreCase))?.Kind;
            if (kind != null)
            {
                capabilities.Add(kind.Value switch
                {
                    SubtitlePostProcessKind.Language => "language",
                    SubtitlePostProcessKind.Style => "style",
                    SubtitlePostProcessKind.LineFit => "line-fit",
                    SubtitlePostProcessKind.QualityGate => "quality-gate",
                    _ => "post-process"
                });
            }
        }

        if (_plugins.ProvidesExtract(plugin.Manifest.Provider))
        {
            capabilities.Add("extract");
        }

        if (_plugins.ProvidesSource(plugin.Manifest.Provider))
        {
            capabilities.Add("source");
        }

        if (_plugins.ProvidesInbox(plugin.Manifest.Provider))
        {
            capabilities.Add("webhook");
        }

        if (_plugins.ProvidesNotifier(plugin.Manifest.Provider))
        {
            capabilities.Add("notice");
        }

        if (_plugins.ProvidesServer(plugin.Manifest.Provider))
        {
            capabilities.Add("server");
        }

        if (_plugins.ProvidesTask(plugin.Manifest.Provider))
        {
            capabilities.Add("task");
        }

        if (_plugins.ProvidesMediaAction(plugin.Manifest.Provider))
        {
            capabilities.Add("media");
        }

        Add("filter", _plugins.ProvidesFilter(plugin.Manifest.Provider));
        Add("glossary", _plugins.ProvidesGlossary(plugin.Manifest.Provider));
        Add("file", _plugins.ProvidesFileTool(plugin.Manifest.Provider));
        Add("log", _plugins.ProvidesLogSink(plugin.Manifest.Provider));
        Add("health", _plugins.ProvidesHealth(plugin.Manifest.Provider));
        Add("widget", _plugins.ProvidesWidget(plugin.Manifest.Provider));
        Add("event", _plugins.ProvidesEvent(plugin.Manifest.Provider));
        Add("map", _plugins.ProvidesMapper(plugin.Manifest.Provider));
        Add("retry", _plugins.ProvidesRetry(plugin.Manifest.Provider));
        Add("badge", _plugins.ProvidesBadge(plugin.Manifest.Provider));
        Add("prompt", _plugins.ProvidesPrompt(plugin.Manifest.Provider));
        Add("export", _plugins.ProvidesExporter(plugin.Manifest.Provider));
        Add("merge", _plugins.ProvidesMerge(plugin.Manifest.Provider));
        Add("codec", _plugins.ProvidesCodec(plugin.Manifest.Provider));
        Add("caption", _plugins.ProvidesCaption(plugin.Manifest.Provider));
        Add("library", _plugins.ProvidesAgent(plugin.Manifest.Provider));

        return capabilities;

        void Add(string name, bool present)
        {
            if (present)
            {
                capabilities.Add(name);
            }
        }
    }

    private static string? Value(IReadOnlyDictionary<string, string> stored, string key) =>
        stored.TryGetValue(key, out var value) ? value : null;
}

public sealed class PluginSettingsWrite
{
    public Dictionary<string, string?>? Values { get; set; }
}

public sealed class MediaActionBody
{
    public string? Title { get; set; }
    public string? Path { get; set; }
    public string? FileName { get; set; }
    public int? Year { get; set; }
}

public sealed class PluginHostWrite
{
    public bool Enabled { get; set; } = true;
    public int Order { get; set; } = 100;
    public string? FailurePolicy { get; set; }
}
