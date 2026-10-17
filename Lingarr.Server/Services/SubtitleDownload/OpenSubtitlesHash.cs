namespace Lingarr.Server.Services.SubtitleDownload;

internal static class OpenSubtitlesHash
{
    public static string? FromFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        const int chunk = 65536;
        using var stream = File.OpenRead(path);
        var size = stream.Length;
        if (size < chunk)
        {
            return null;
        }

        ulong hash = (ulong)size;
        hash += Sum(stream, chunk);
        stream.Seek(Math.Max(0, size - chunk), SeekOrigin.Begin);
        hash += Sum(stream, chunk);
        return hash.ToString("x16");
    }

    private static ulong Sum(Stream stream, int bytes)
    {
        var buffer = new byte[8];
        ulong total = 0;
        var remaining = bytes;
        while (remaining >= 8)
        {
            var read = stream.Read(buffer, 0, 8);
            if (read < 8)
            {
                break;
            }

            total += BitConverter.ToUInt64(buffer, 0);
            remaining -= 8;
        }

        return total;
    }
}
