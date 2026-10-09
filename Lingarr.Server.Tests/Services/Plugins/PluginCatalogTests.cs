using System;
using System.Collections.Generic;
using System.Linq;
using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;
using Lingarr.Server.Services.Plugins;
using Xunit;

namespace Lingarr.Server.Tests.Services.Plugins;

public class PluginCatalogTests
{
    [Fact]
    public void OwnsKey_IncludesPanelFieldsAndRejectsForeignKeys()
    {
        var manifest = new SampleManifest();

        Assert.True(PluginCatalog.OwnsKey(manifest, "style_sample_enabled"));
        Assert.False(PluginCatalog.OwnsKey(manifest, "plex_token"));
        Assert.Contains("style_sample_enabled", PluginCatalog.Fields(manifest).Select(field => field.Key));
    }

    [Fact]
    public void HostKeys_AreStableAndPoliciesAreLimited()
    {
        Assert.Equal("plugin_host_style-sample_enabled", PluginCatalog.EnabledKey("style-sample"));
        Assert.True(PluginCatalog.IsEnabled(null));
        Assert.False(PluginCatalog.IsEnabled("false"));
        Assert.Equal(100, PluginCatalog.ParseOrder("nope"));
        Assert.True(PluginCatalog.TryNormalizePolicy("Stop", out var policy));
        Assert.Equal(PluginCatalog.PolicyStop, policy);
        Assert.False(PluginCatalog.TryNormalizePolicy("explode", out _));
    }

    [Fact]
    public void Capabilities_NameTranslationUiAndAction()
    {
        var manifest = new SampleManifest();
        Assert.Equal(["ui", "action"], PluginCatalog.Capabilities(manifest, translates: false, hasAction: true));
        Assert.Equal(["translation"], PluginCatalog.Capabilities(manifest, translates: true, hasAction: false).Take(1));
    }

    private sealed class SampleManifest : IPluginManifest
    {
        public string Provider => "style-sample";
        public string DisplayName => "Style sample";
        public string? Description => null;
        public IReadOnlyList<PluginSettingField> Settings { get; } = [];
        public IReadOnlyList<PluginPanelContribution> Panels { get; } =
        [
            new()
            {
                Id = "style",
                Section = "translation",
                TabId = "style-sample",
                TabLabel = "Style sample",
                Title = "Style sample",
                Fields =
                [
                    new()
                    {
                        Key = "style_sample_enabled",
                        Label = "Improve style",
                        Type = PluginSettingType.Toggle,
                        Default = "false"
                    }
                ],
                Actions = [new() { Id = "test", Label = "Test" }]
            }
        ];
    }
}
