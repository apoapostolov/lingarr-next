using System;
using System.IO;
using Lingarr.Server.Services.Translation;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class TranslationFactoryPathTests
{
    [Fact]
    public void LanguageCatalogPath_UsesAppDirectory()
    {
        var path = TranslationFactory.LanguageCatalogPath("google_languages.json");

        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "Statics", "google_languages.json"),
            path);
    }
}
