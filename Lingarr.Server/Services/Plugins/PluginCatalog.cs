using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;

namespace Lingarr.Server.Services.Plugins;

public static class PluginCatalog
{
    public const string PolicySkip = "skip";
    public const string PolicyStop = "stop";
    public const string PolicyFail = "fail";

    public static readonly string[] Sections = ["connections", "translation", "automation", "system", "plugins"];

    public static string EnabledKey(string provider) => $"plugin_host_{provider}_enabled";

    public static string OrderKey(string provider) => $"plugin_host_{provider}_order";

    public static string PolicyKey(string provider) => $"plugin_host_{provider}_failure_policy";

    public static IReadOnlyList<string> HostKeys(string provider) =>
        [EnabledKey(provider), OrderKey(provider), PolicyKey(provider)];

    public static bool IsEnabled(string? value) =>
        !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    public static int ParseOrder(string? value) =>
        int.TryParse(value, out var order) ? Math.Clamp(order, 0, 9999) : 100;

    public static bool TryNormalizePolicy(string? value, out string policy)
    {
        policy = (value ?? PolicySkip).Trim().ToLowerInvariant();
        if (policy is PolicySkip or PolicyStop or PolicyFail)
        {
            return true;
        }

        policy = PolicySkip;
        return false;
    }

    public static IEnumerable<PluginSettingField> Fields(IPluginManifest manifest)
    {
        foreach (var field in manifest.Settings)
        {
            yield return field;
        }

        foreach (var panel in manifest.Panels)
        {
            foreach (var field in panel.Fields)
            {
                yield return field;
            }
        }
    }

    public static bool OwnsKey(IPluginManifest manifest, string key) =>
        Fields(manifest).Any(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> Capabilities(IPluginManifest manifest, bool translates, bool hasAction)
    {
        var capabilities = new List<string>();
        if (translates)
        {
            capabilities.Add("translation");
        }

        if (manifest.Panels.Count > 0)
        {
            capabilities.Add("ui");
        }

        if (hasAction)
        {
            capabilities.Add("action");
        }

        return capabilities;
    }

    public static bool PanelIsValid(PluginPanelContribution panel) =>
        Sections.Contains(panel.Section, StringComparer.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(panel.TabId)
        && !string.IsNullOrWhiteSpace(panel.TabLabel)
        && !string.IsNullOrWhiteSpace(panel.Title);
}
