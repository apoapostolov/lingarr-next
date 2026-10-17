using System.IO;
using System.IO.Compression;
using System.Text;
using Lingarr.Server.Services.SubtitleDownload;
using Xunit;

namespace Lingarr.Server.Tests.Services.SubtitleDownload;

public class SubtitleArchiveTests
{
    [Fact]
    public void Unpack_PrefersEntryMatchingRequestedLanguage()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "de.srt", "1\n00:00:01,000 --> 00:00:02,000\nGuten Tag.\n");
            WriteEntry(zip, "en.srt", "1\n00:00:01,000 --> 00:00:02,000\nHello there.\n");
        }

        var unpacked = SubtitleArchive.Unpack(buffer.ToArray(), "subs.zip", out var extension, "en");
        Assert.Equal(".srt", extension);
        Assert.Contains("Hello there", Encoding.UTF8.GetString(unpacked));
        Assert.DoesNotContain("Guten Tag", Encoding.UTF8.GetString(unpacked));
    }

    private static void WriteEntry(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }
}
