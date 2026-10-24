using Lingarr.Server.Services.Translation;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class StructuredOutputSchemaTests
{
    [Fact]
    public void TranslatedLineDescription_NamesTheTargetLanguage()
    {
        Assert.Equal(
            "Subtitle text translated into Bulgarian",
            StructuredOutputSchema.TranslatedLineDescription("Bulgarian"));
    }
}
