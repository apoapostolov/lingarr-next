using System.Reflection;
using System.Runtime.Loader;
using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;
using Lingarr.Contracts.Translation;

namespace Lingarr.Server.Services.Plugins;

/// <summary>
/// Loads plugin assemblies from the directory configured through PLUGINS_PATH and registers
/// the translation providers and manifests they expose at startup.
/// </summary>
public sealed class PluginLoader
{
    private const int HostMajorVersion = 1;
    public const string PluginsPathEnvironmentVariable = "PLUGINS_PATH";

    private readonly ILogger<PluginLoader> _logger;
    private readonly List<RegisteredPlugin> _loadedPlugins = new();
    private readonly HashSet<string> _translators = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _actions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _postProcessors = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _extractTools = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inboxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _notifiers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _tasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _mediaActions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _filters = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _glossaries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _fileTools = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _logSinks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _healthChecks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _widgets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _events = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _mappers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _retries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _badges = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _prompts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _exporters = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _merges = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _codecs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _captions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _agents = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<RegisteredPlugin> LoadedPlugins => _loadedPlugins;
    public bool LoadingEnabled { get; private set; }

    public bool ProvidesTranslation(string provider) => _translators.Contains(provider);

    public bool ProvidesAction(string provider) => _actions.Contains(provider);

    public bool ProvidesPostProcess(string provider) => _postProcessors.Contains(provider);

    public bool ProvidesExtract(string provider) => _extractTools.Contains(provider);

    public bool ProvidesSource(string provider) => _sources.Contains(provider);

    public bool ProvidesInbox(string provider) => _inboxes.Contains(provider);

    public bool ProvidesNotifier(string provider) => _notifiers.Contains(provider);

    public bool ProvidesServer(string provider) => _servers.Contains(provider);

    public bool ProvidesTask(string provider) => _tasks.Contains(provider);

    public bool ProvidesMediaAction(string provider) => _mediaActions.Contains(provider);

    public bool ProvidesFilter(string provider) => _filters.Contains(provider);

    public bool ProvidesGlossary(string provider) => _glossaries.Contains(provider);

    public bool ProvidesFileTool(string provider) => _fileTools.Contains(provider);

    public bool ProvidesLogSink(string provider) => _logSinks.Contains(provider);

    public bool ProvidesHealth(string provider) => _healthChecks.Contains(provider);

    public bool ProvidesWidget(string provider) => _widgets.Contains(provider);

    public bool ProvidesEvent(string provider) => _events.Contains(provider);

    public bool ProvidesMapper(string provider) => _mappers.Contains(provider);

    public bool ProvidesRetry(string provider) => _retries.Contains(provider);

    public bool ProvidesBadge(string provider) => _badges.Contains(provider);

    public bool ProvidesPrompt(string provider) => _prompts.Contains(provider);

    public bool ProvidesExporter(string provider) => _exporters.Contains(provider);

    public bool ProvidesMerge(string provider) => _merges.Contains(provider);

    public bool ProvidesCodec(string provider) => _codecs.Contains(provider);

    public bool ProvidesCaption(string provider) => _captions.Contains(provider);

    public bool ProvidesAgent(string provider) => _agents.Contains(provider);

    public bool StartsOff(string provider) =>
        ProvidesPostProcess(provider)
        || ProvidesExtract(provider)
        || ProvidesSource(provider)
        || _inboxes.Contains(provider)
        || _notifiers.Contains(provider)
        || _servers.Contains(provider)
        || _tasks.Contains(provider)
        || _mediaActions.Contains(provider)
        || _filters.Contains(provider)
        || _glossaries.Contains(provider)
        || _fileTools.Contains(provider)
        || _logSinks.Contains(provider)
        || _healthChecks.Contains(provider)
        || _widgets.Contains(provider)
        || _events.Contains(provider)
        || _mappers.Contains(provider)
        || _retries.Contains(provider)
        || _badges.Contains(provider)
        || _prompts.Contains(provider)
        || _exporters.Contains(provider)
        || _merges.Contains(provider)
        || _codecs.Contains(provider)
        || _captions.Contains(provider)
        || _agents.Contains(provider);

    public PluginLoader(ILogger<PluginLoader> logger)
    {
        _logger = logger;
    }

    public void LoadPlugins(IServiceCollection services)
    {
        var pluginsPath = Environment.GetEnvironmentVariable(PluginsPathEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(pluginsPath))
        {
            _logger.LogInformation(
                "Plugin loading disabled. Set {EnvironmentVariable} to enable.",
                PluginsPathEnvironmentVariable);
            return;
        }

        if (!Directory.Exists(pluginsPath))
        {
            _logger.LogWarning(
                "{EnvironmentVariable} is set to {PluginsPath} but the directory does not exist.",
                PluginsPathEnvironmentVariable,
                pluginsPath);
            return;
        }

        LoadingEnabled = true;
        var pluginFiles = Directory.EnumerateFiles(pluginsPath, "*.dll");

        foreach (var pluginFile in pluginFiles)
        {
            try
            {
                LoadPluginAssembly(services, pluginFile);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to load plugin {PluginFile}, skipping.",
                    pluginFile);
            }
        }
    }

    private void LoadPluginAssembly(IServiceCollection services, string pluginFile)
    {
        var assemblyName = AssemblyName.GetAssemblyName(pluginFile);
        if (IsHostAssembly(assemblyName))
        {
            return;
        }

        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(pluginFile);

        var versionAttribute = assembly.GetCustomAttribute<LingarrPluginApiVersionAttribute>();
        if (versionAttribute is null)
        {
            _logger.LogDebug(
                "Assembly {PluginFile} has no LingarrPluginApiVersion attribute, skipping.",
                pluginFile);
            return;
        }

        if (versionAttribute.Major != HostMajorVersion)
        {
            _logger.LogWarning(
                "Plugin {PluginFile} targets plugin API version {Major}.{Minor} but the host requires major version {HostMajorVersion}, skipping.",
                pluginFile,
                versionAttribute.Major,
                versionAttribute.Minor,
                HostMajorVersion);
            return;
        }

        var exportedTypes = assembly.GetExportedTypes();
        var manifestsFromThisAssembly = new List<IPluginManifest>();
        var sourceFileName = Path.GetFileName(pluginFile);

        foreach (var type in exportedTypes)
        {
            if (type.IsAbstract || type.IsInterface)
            {
                continue;
            }

            var providerAttribute = type.GetCustomAttribute<PluginProviderAttribute>();
            if (providerAttribute is not null)
            {
                RegisterProvider(services, type, providerAttribute);
            }

            if (typeof(IPluginActionHandler).IsAssignableFrom(type) && providerAttribute is not null)
            {
                _actions.Add(providerAttribute.Provider);
                services.AddScoped(typeof(IPluginActionHandler), type);
            }

            if (typeof(ISubtitlePostProcessor).IsAssignableFrom(type) && providerAttribute is not null)
            {
                _postProcessors.Add(providerAttribute.Provider);
                services.AddScoped(typeof(ISubtitlePostProcessor), type);
            }

            if (typeof(IExtractTool).IsAssignableFrom(type) && providerAttribute is not null)
            {
                _extractTools.Add(providerAttribute.Provider);
                services.AddScoped(typeof(IExtractTool), type);
            }

            if (typeof(ISubtitleSource).IsAssignableFrom(type) && providerAttribute is not null)
            {
                _sources.Add(providerAttribute.Provider);
                services.AddScoped(typeof(ISubtitleSource), type);
            }

            Register(type, providerAttribute, typeof(IWebhookInbox), _inboxes, services);
            Register(type, providerAttribute, typeof(IPluginNotifier), _notifiers, services);
            Register(type, providerAttribute, typeof(IMediaServerPlugin), _servers, services);
            Register(type, providerAttribute, typeof(IPluginTask), _tasks, services);
            Register(type, providerAttribute, typeof(IMediaAction), _mediaActions, services);
            Register(type, providerAttribute, typeof(IContentFilter), _filters, services);
            Register(type, providerAttribute, typeof(IGlossary), _glossaries, services);
            Register(type, providerAttribute, typeof(IFileTool), _fileTools, services);
            Register(type, providerAttribute, typeof(ILogSink), _logSinks, services);
            Register(type, providerAttribute, typeof(IPluginHealthCheck), _healthChecks, services);
            Register(type, providerAttribute, typeof(IDashboardWidget), _widgets, services);
            Register(type, providerAttribute, typeof(IMediaEventSink), _events, services);
            Register(type, providerAttribute, typeof(IPathMapper), _mappers, services);
            Register(type, providerAttribute, typeof(IRetryPolicy), _retries, services);
            Register(type, providerAttribute, typeof(IListBadge), _badges, services);
            Register(type, providerAttribute, typeof(IPromptContributor), _prompts, services);
            Register(type, providerAttribute, typeof(IStatisticsExporter), _exporters, services);
            Register(type, providerAttribute, typeof(ISubtitleMerge), _merges, services);
            Register(type, providerAttribute, typeof(ISidecarCodec), _codecs, services);
            Register(type, providerAttribute, typeof(ICaptionPolicy), _captions, services);
            Register(type, providerAttribute, typeof(ILibraryAgent), _agents, services);

            if (typeof(IPluginManifest).IsAssignableFrom(type))
            {
                var manifestInstance = TryCreateManifest(type, pluginFile);
                if (manifestInstance is not null)
                {
                    services.AddSingleton(typeof(IPluginManifest), manifestInstance);
                    manifestsFromThisAssembly.Add(manifestInstance);
                }
            }
        }

        foreach (var manifest in manifestsFromThisAssembly)
        {
            _loadedPlugins.Add(new RegisteredPlugin
            {
                Manifest = manifest,
                IsBuiltIn = false,
                SourceFile = sourceFileName
            });
        }

        _logger.LogInformation(
            "Loaded plugin {PluginFile} ({ManifestCount} manifest(s)).",
            pluginFile,
            manifestsFromThisAssembly.Count);
    }

    private static void Register(
        Type type,
        PluginProviderAttribute? providerAttribute,
        Type serviceType,
        HashSet<string> ids,
        IServiceCollection services)
    {
        if (providerAttribute is null || !serviceType.IsAssignableFrom(type))
        {
            return;
        }

        ids.Add(providerAttribute.Provider);
        services.AddScoped(serviceType, type);
    }

    private void RegisterProvider(
        IServiceCollection services,
        Type implementationType,
        PluginProviderAttribute providerAttribute)
    {
        if (typeof(ITranslationService).IsAssignableFrom(implementationType))
        {
            _translators.Add(providerAttribute.Provider);
            services.AddKeyedScoped(
                typeof(ITranslationService),
                providerAttribute.Provider.ToLowerInvariant(),
                implementationType);
        }
    }

    private IPluginManifest? TryCreateManifest(Type manifestType, string pluginFile)
    {
        var parameterlessConstructor = manifestType.GetConstructor(Type.EmptyTypes);
        if (parameterlessConstructor is null)
        {
            _logger.LogWarning(
                "Plugin manifest {ManifestType} from {PluginFile} has no parameterless constructor, skipping.",
                manifestType.FullName,
                pluginFile);
            return null;
        }

        try
        {
            return (IPluginManifest?)parameterlessConstructor.Invoke(null);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to instantiate plugin manifest {ManifestType} from {PluginFile}.",
                manifestType.FullName,
                pluginFile);
            return null;
        }
    }

    private static bool IsHostAssembly(AssemblyName assemblyName)
    {
        foreach (var loadedAssembly in AssemblyLoadContext.Default.Assemblies)
        {
            if (string.Equals(
                    loadedAssembly.GetName().Name,
                    assemblyName.Name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
