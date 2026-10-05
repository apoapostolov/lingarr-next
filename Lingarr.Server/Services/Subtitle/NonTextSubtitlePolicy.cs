using Lingarr.Core.Configuration;
using Lingarr.Core.Entities;
using Lingarr.Core.Interfaces;

namespace Lingarr.Server.Services.Subtitle;

/// <summary>
/// Which picture and caption formats Lingarr may turn into a text subtitle.
/// A missing setting stays on.
/// </summary>
public sealed record NonTextSubtitlePolicy
{
    public static readonly string[] Keys =
    [
        SettingKeys.Subtitle.PictureOcrEnabled,
        SettingKeys.Subtitle.CaptionExtractEnabled,
        SettingKeys.Subtitle.PgsEnabled,
        SettingKeys.Subtitle.VobSubEnabled,
        SettingKeys.Subtitle.DvbEnabled,
        SettingKeys.Subtitle.XsubEnabled,
        SettingKeys.Subtitle.Eia608Enabled,
        SettingKeys.Subtitle.Eia708Enabled,
        SettingKeys.Subtitle.TeletextEnabled,
        SettingKeys.Subtitle.ConvertLastResort,
        SettingKeys.Subtitle.ConvertWaitHours
    ];

    public bool PictureOcr { get; init; } = true;
    public bool CaptionExtract { get; init; } = true;
    public bool Pgs { get; init; } = true;
    public bool VobSub { get; init; } = true;
    public bool Dvb { get; init; } = true;
    public bool Xsub { get; init; } = true;
    public bool Eia608 { get; init; } = true;
    public bool Eia708 { get; init; } = true;
    public bool Teletext { get; init; } = true;
    public bool LastResort { get; init; } = true;
    public int WaitHours { get; init; } = 72;

    public static NonTextSubtitlePolicy All { get; } = new();

    public NonTextSubtitlePolicy TextOnly() => this with
    {
        PictureOcr = false,
        CaptionExtract = false
    };

    public static DateTime? FoundAt(IMedia media)
    {
        if (media.DateAdded.HasValue)
        {
            return media.DateAdded;
        }

        return media is BaseEntity entity && entity.CreatedAt != default
            ? entity.CreatedAt
            : null;
    }

    public bool ConvertImagesNow(DateTime? foundAt, DateTime utcNow)
    {
        if (!AnyFormatEnabled)
        {
            return false;
        }

        if (!LastResort)
        {
            return true;
        }

        if (foundAt == null)
        {
            return false;
        }

        return utcNow >= AsUtc(foundAt.Value).AddHours(WaitHours);
    }

    public TimeSpan? DelayUntilImages(DateTime? foundAt, DateTime utcNow)
    {
        if (!LastResort || !AnyFormatEnabled || foundAt == null)
        {
            return null;
        }

        var delay = AsUtc(foundAt.Value).AddHours(WaitHours) - utcNow;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    public bool AnyFormatEnabled =>
        (PictureOcr && (Pgs || VobSub || Dvb || Xsub))
        || (CaptionExtract && (Eia608 || Eia708 || Teletext));

    public static NonTextSubtitlePolicy From(IReadOnlyDictionary<string, string> settings) => new()
    {
        PictureOcr = On(settings, SettingKeys.Subtitle.PictureOcrEnabled),
        CaptionExtract = On(settings, SettingKeys.Subtitle.CaptionExtractEnabled),
        Pgs = On(settings, SettingKeys.Subtitle.PgsEnabled),
        VobSub = On(settings, SettingKeys.Subtitle.VobSubEnabled),
        Dvb = On(settings, SettingKeys.Subtitle.DvbEnabled),
        Xsub = On(settings, SettingKeys.Subtitle.XsubEnabled),
        Eia608 = On(settings, SettingKeys.Subtitle.Eia608Enabled),
        Eia708 = On(settings, SettingKeys.Subtitle.Eia708Enabled),
        Teletext = On(settings, SettingKeys.Subtitle.TeletextEnabled),
        LastResort = On(settings, SettingKeys.Subtitle.ConvertLastResort),
        WaitHours = ParseHours(settings)
    };

    public bool Allows(string codec)
    {
        return codec.Trim().ToLowerInvariant() switch
        {
            "hdmv_pgs_subtitle" or "pgssub" => PictureOcr && Pgs,
            "dvd_subtitle" or "dvdsub" => PictureOcr && VobSub,
            "dvb_subtitle" => PictureOcr && Dvb,
            "xsub" => PictureOcr && Xsub,
            "eia_608" => CaptionExtract && Eia608,
            "eia_708" => CaptionExtract && Eia708,
            "dvb_teletext" => CaptionExtract && Teletext,
            _ => false
        };
    }

    private static bool On(IReadOnlyDictionary<string, string> settings, string key) =>
        !settings.TryGetValue(key, out var value)
        || !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    private static int ParseHours(IReadOnlyDictionary<string, string> settings)
    {
        if (!settings.TryGetValue(SettingKeys.Subtitle.ConvertWaitHours, out var value)
            || !int.TryParse(value, out var hours))
        {
            return 72;
        }

        return Math.Clamp(hours, 0, 24 * 365);
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
