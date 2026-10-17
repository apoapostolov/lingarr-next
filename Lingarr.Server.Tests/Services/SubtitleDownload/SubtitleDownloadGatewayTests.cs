using System;
using System.IO;
using System.Text;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.SubtitleDownload;
using Xunit;

namespace Lingarr.Server.Tests.Services.SubtitleDownload;

public class SubtitleDownloadGatewayTests
{
    [Fact]
    public void DecodeText_ReadsUtf8()
    {
        var bytes = Encoding.UTF8.GetBytes("<a href=\"/download/en.srt\">English</a>");
        Assert.Contains("download/en.srt", SubtitleDownloadGateway.DecodeText(bytes, "utf-8"));
    }

    [Fact]
    public void DecodeText_FallsBackWhenIso885915IsUnknown()
    {
        var bytes = Encoding.Latin1.GetBytes("<a href=\"/download/en.srt\">caf\u00e9</a>");
        var html = SubtitleDownloadGateway.DecodeText(bytes, "iso-8859-15");
        Assert.Contains("download/en.srt", html);
    }

    [Fact]
    public void DecodeText_UsesUtf8WhenCharsetIsMissing()
    {
        var bytes = Encoding.UTF8.GetBytes("hello");
        Assert.Equal("hello", SubtitleDownloadGateway.DecodeText(bytes, null));
    }

    [Fact]
    public void IsHtml_DetectsDoctypePage()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "<!DOCTYPE html>\n<html lang=\"bg\"><head><title>Субтитри BG</title></head></html>");
        Assert.True(SubtitleBytes.IsHtml(bytes));
    }

    [Fact]
    public void IsHtml_AllowsSrt()
    {
        var bytes = Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\nHello\n");
        Assert.False(SubtitleBytes.IsHtml(bytes));
    }

    [Fact]
    public void MismatchesLanguage_RejectsFrenchAsEnglish()
    {
        var bytes = Encoding.UTF8.GetBytes(
            """
            1
            00:00:10,760 --> 00:00:11,988
            Tranquille.

            2
            00:00:19,800 --> 00:00:24,157
            L'infirmière de nuit

            3
            00:00:28,480 --> 00:00:32,314
            C'est rien, madame,
            j'ai juste troué votre veste.

            4
            00:00:34,320 --> 00:00:35,275
            Reposez-vous.

            5
            00:00:35,560 --> 00:00:37,949
            Je vais voir ma nouvelle cliente.

            6
            00:03:45,100 --> 00:03:46,935
            - On veut quoi ?
            - Une mutuelle !

            7
            00:03:47,102 --> 00:03:48,854
            - On la veut quand ?
            - Maintenant !
            """);
        Assert.True(SubtitleBytes.MismatchesLanguage(bytes, "en"));
        Assert.True(SubtitleBytes.MismatchesLanguage(bytes, "bg"));
        Assert.False(SubtitleBytes.MismatchesLanguage(bytes, "fr"));
    }

    [Fact]
    public void MismatchesLanguage_RejectsArabicAsEnglish()
    {
        var bytes = Encoding.UTF8.GetBytes(
            """
            1
            00:00:11,500 --> 00:00:16,500
            ترجمات من قبل المتفجرات
            www.OpenSubtitles.org

            2
            00:00:54,987 --> 00:00:58,455
            نيك، كان هذا مأساة، ولماذا

            3
            00:00:58,539 --> 00:01:01,208
            فعلت هذا يا رجل؟

            4
            00:01:01,292 --> 00:01:04,378
            كان يجب أن تخبرني
            """);
        Assert.True(SubtitleBytes.MismatchesLanguage(bytes, "en"));
        Assert.True(SubtitleBytes.MismatchesLanguage(bytes, "bg"));
    }

    [Fact]
    public void QueryTitle_IncludesYearFromFileName()
    {
        var query = new SubtitleDownloadQuery
        {
            MediaType = Lingarr.Core.Enum.MediaType.Movie,
            MediaId = 1,
            Title = "Infirmary",
            FileName = "Infirmary (2026) {tmdb-1610703} [x265]-YTS",
            Language = "en"
        };
        Assert.Equal(2026, SubtitleDownloadGateway.YearOf(query));
        Assert.Equal("Infirmary 2026", SubtitleDownloadGateway.QueryTitle(query));
    }

    [Fact]
    public void QueryTitle_StripsColonFromMovieTitle()
    {
        var query = new SubtitleDownloadQuery
        {
            MediaType = Lingarr.Core.Enum.MediaType.Movie,
            MediaId = 70,
            Title = "Avatar: Fire and Ash",
            Year = 2025,
            FileName = "Avatar Fire and Ash (2025) {tmdb-83533} [Bluray-1080p][x265]-QxR",
            Language = "en"
        };
        Assert.Equal("Avatar Fire and Ash 2025", SubtitleDownloadGateway.QueryTitle(query));
    }

    [Fact]
    public void ScoreCandidate_MatchesTitleWhenHrefUsesDotsInsteadOfColon()
    {
        var query = new SubtitleDownloadQuery
        {
            MediaType = Lingarr.Core.Enum.MediaType.Movie,
            MediaId = 70,
            Title = "Avatar: Fire and Ash",
            Year = 2025,
            FileName = "Avatar Fire and Ash (2025) {tmdb-83533} [Bluray-1080p][x265]-QxR",
            Language = "en"
        };
        var score = SubtitleDownloadGateway.ScoreCandidate(
            "subs/1707/avatar.fire.and.ash.2025.1080p.web-dl.html",
            "avatar fire and ash 2025 1080p web-dl",
            query);
        Assert.True(score >= 3);
    }

    [Fact]
    public void RankLinks_KeepsColonTitleResultPages()
    {
        var html =
            """<a href="subs/1707/Avatar.Fire.And.Ash.2025.1080p.WEB-DL.html">Avatar Fire And Ash 2025</a>""";
        var page = new Uri("https://www.subtitlecat.com/index.php?search=Avatar%3A%20Fire%20and%20Ash%202025");
        var query = new SubtitleDownloadQuery
        {
            MediaType = Lingarr.Core.Enum.MediaType.Movie,
            MediaId = 70,
            Title = "Avatar: Fire and Ash",
            Year = 2025,
            FileName = "Avatar Fire and Ash (2025) {tmdb-83533} [Bluray-1080p][x265]-QxR",
            Language = "en"
        };
        var ranked = SubtitleDownloadGateway.RankLinks(html, page, query);
        Assert.Contains(
            ranked,
            uri => uri.AbsolutePath.Contains("/subs/1707/", StringComparison.OrdinalIgnoreCase)
                   && uri.AbsolutePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void QueryTitle_TurnsAmpersandAndApostropheIntoSearchWords()
    {
        var query = new SubtitleDownloadQuery
        {
            MediaType = Lingarr.Core.Enum.MediaType.Movie,
            MediaId = 122,
            Title = "Bill & Ted's Excellent Adventure",
            Year = 1989,
            Language = "en"
        };
        Assert.Equal("Bill and Teds Excellent Adventure 1989", SubtitleDownloadGateway.QueryTitle(query));
    }

    [Fact]
    public void ScoreCandidate_MatchesAccentedTitleWithoutAccents()
    {
        var query = new SubtitleDownloadQuery
        {
            MediaType = Lingarr.Core.Enum.MediaType.Movie,
            MediaId = 426,
            Title = "Joker: Folie à Deux",
            Year = 2024,
            Language = "en"
        };
        var score = SubtitleDownloadGateway.ScoreCandidate(
            "subs/1/joker.folie.a.deux.2024.webdl.html",
            "joker folie a deux 2024",
            query);
        Assert.True(score >= 3);
    }

    [Fact]
    public void EncodeHref_EscapesSpacesAndBrackets()
    {
        var encoded = SubtitleDownloadGateway.EncodeHref(
            "/subs/1710/Infirmary.2026.WEBRip.x264.AAC5.1-[YTS.GG - YTS.BZ]-en.srt");
        Assert.Equal(
            "/subs/1710/Infirmary.2026.WEBRip.x264.AAC5.1-%5BYTS.GG%20-%20YTS.BZ%5D-en.srt",
            encoded);
        Assert.True(Uri.TryCreate(new Uri("https://www.subtitlecat.com/index.php"), encoded, out var uri));
        Assert.Contains("-en.srt", uri!.AbsolutePath);
        Assert.DoesNotContain(" ", uri.AbsoluteUri);
        Assert.Contains("%5B", uri.AbsoluteUri);
    }

    [Fact]
    public void RankLinks_KeepsRelativeSubsResultPages()
    {
        var html =
            """<a href="subs/1710/Infirmary.2026.1080p.WEBRip.x264.AAC5.1-%5BYTS.GG%20-%20YTS.BZ%5D.html">Infirmary</a>""";
        var page = new Uri("https://www.subtitlecat.com/index.php?search=Infirmary%202026");
        var query = new SubtitleDownloadQuery
        {
            MediaType = Lingarr.Core.Enum.MediaType.Movie,
            MediaId = 1,
            Title = "Infirmary",
            Year = 2026,
            FileName = "Infirmary (2026) {tmdb-1610703} [WEBRip-1080p][x265]-YTS",
            Language = "en"
        };
        var ranked = SubtitleDownloadGateway.RankLinks(html, page, query);
        Assert.Contains(
            ranked,
            uri => uri.AbsolutePath.Contains("/subs/1710/", StringComparison.OrdinalIgnoreCase)
                   && uri.AbsolutePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                   && !uri.AbsoluteUri.Contains(' '));
    }

    [Fact]
    public void HrefMatchesLanguage_ReadsSubtitleCatFile()
    {
        Assert.True(SubtitleDownloadGateway.HrefMatchesLanguage(
            "/subs/1710/Infirmary.2026.WEBRip-[YTS.GG]-en.srt", "en"));
        Assert.True(SubtitleDownloadGateway.HrefMatchesLanguage(
            "/subs/1712/Infirmary.2026.WEBRip-[YTS.GG]-bg.srt", "bg"));
        Assert.False(SubtitleDownloadGateway.HrefMatchesLanguage(
            "/subs/1711/Infirmary.2026.WEBRip-[YTS.GG]-ar.srt", "en"));
    }

    [Fact]
    public void ScoreCandidate_DoesNotMatchBlackBoxWhenTitleIsBox()
    {
        var query = new SubtitleDownloadQuery
        {
            MediaType = Lingarr.Core.Enum.MediaType.Movie,
            MediaId = 149,
            Title = "Box",
            Year = 2021,
            FileName = "Box (2021) {tmdb-680847} [WEBDL-1080p]",
            Language = "en"
        };
        var blackBox = SubtitleDownloadGateway.ScoreCandidate(
            "subs/1/black.box.2021.1080p.html",
            "black box 2021</a><a href=\"subs/2/box.2021.1080p.html\">box 2021",
            query);
        var box = SubtitleDownloadGateway.ScoreCandidate(
            "subs/2/box.2021.1080p.html",
            "box 2021",
            query);
        Assert.True(blackBox < 3, $"Black Box scored {blackBox}");
        Assert.True(box >= 3, $"Box scored {box}");
    }

    [Fact]
    public void ScoreCandidate_DoesNotMatchHerInsideThere()
    {
        var query = new SubtitleDownloadQuery
        {
            MediaType = Lingarr.Core.Enum.MediaType.Movie,
            MediaId = 1163,
            Title = "Her",
            Year = 2013,
            Language = "en"
        };
        var nested = SubtitleDownloadGateway.ScoreCandidate(
            "subs/1/theres.something.about.mary.1998.html",
            "theres something about mary 1998",
            query);
        var exact = SubtitleDownloadGateway.ScoreCandidate(
            "subs/2/her.2013.1080p.html",
            "her 2013",
            query);
        Assert.True(nested < 3, $"nested Her scored {nested}");
        Assert.True(exact >= 3, $"Her scored {exact}");
    }

    [Fact]
    public void ScoreCandidate_DoesNotMatchDistrict9WhenTitleIsNine()
    {
        var query = new SubtitleDownloadQuery
        {
            MediaType = Lingarr.Core.Enum.MediaType.Movie,
            MediaId = 9,
            Title = "9",
            Year = 2009,
            Language = "en"
        };
        var district = SubtitleDownloadGateway.ScoreCandidate(
            "subs/1/district.9.2009.html",
            "district 9 2009",
            query);
        var nine = SubtitleDownloadGateway.ScoreCandidate(
            "subs/2/9.2009.1080p.html",
            "9 2009",
            query);
        Assert.True(district < 3, $"District 9 scored {district}");
        Assert.True(nine >= 3, $"9 scored {nine}");
    }

    [Fact]
    public void RankLinks_DropsBlackBoxWhenSearchingBox()
    {
        var html =
            """
            <a href="subs/1/Black.Box.2021.1080p.html">Black Box 2021</a>
            <a href="subs/2/Box.2021.1080p.html">Box 2021</a>
            """;
        var page = new Uri("https://www.subtitlecat.com/index.php?search=Box%202021");
        var query = new SubtitleDownloadQuery
        {
            MediaType = Lingarr.Core.Enum.MediaType.Movie,
            MediaId = 149,
            Title = "Box",
            Year = 2021,
            Language = "en"
        };
        var ranked = SubtitleDownloadGateway.RankLinks(html, page, query);
        Assert.DoesNotContain(
            ranked,
            uri => uri.AbsolutePath.Contains("Black.Box", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            ranked,
            uri => uri.AbsolutePath.Contains("/subs/2/Box.2021", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void QueryTitle_KeepsNumericTitleAndReleaseYear()
    {
        var query = new SubtitleDownloadQuery
        {
            MediaType = Lingarr.Core.Enum.MediaType.Movie,
            MediaId = 3,
            Title = "2073",
            Year = 2024,
            FileName = "2073 (2024) {tmdb-1064489} [WEBDL-1080p]",
            Language = "en"
        };
        Assert.Equal(2024, SubtitleDownloadGateway.YearOf(query));
        Assert.Equal("2073 2024", SubtitleDownloadGateway.QueryTitle(query));
    }

    [Fact]
    public void SearchTitle_SplitsMiddleDot()
    {
        Assert.Equal("BURN E", SubtitleDownloadGateway.SearchTitle("BURN·E"));
        Assert.Equal("WALL E", SubtitleDownloadGateway.SearchTitle("WALL·E"));
    }

    [Fact]
    public void ScoreCandidate_PrefersYtsRelease()
    {
        var query = new SubtitleDownloadQuery
        {
            MediaType = Lingarr.Core.Enum.MediaType.Movie,
            MediaId = 1,
            Title = "Infirmary",
            FileName = "Infirmary (2026) {tmdb-1610703} [WEBRip-1080p][x265]-YTS",
            Language = "en"
        };
        var yts = SubtitleDownloadGateway.ScoreCandidate(
            "subs/1710/infirmary.2026.1080p.webrip.x264-[yts.gg].html",
            "infirmary 2026 yts webrip",
            query);
        var other = SubtitleDownloadGateway.ScoreCandidate(
            "subs/1199/school-infirmary.html",
            "school infirmary",
            query);
        Assert.True(yts > other);
        Assert.True(yts >= 8);
    }

    [Fact]
    public void BuildQuery_ReadsYearFromFileName()
    {
        var movie = new Lingarr.Core.Entities.Movie
        {
            Title = "Infirmary",
            FileName = "Infirmary (2026) {tmdb-1610703} [x265]-YTS",
            Path = "/media/movies/Infirmary (2026) {tmdb-1610703}",
            RadarrId = 1,
            DateAdded = DateTime.UtcNow
        };
        var query = SubtitleDownloadService.BuildQuery(movie, Lingarr.Core.Enum.MediaType.Movie, "en");
        Assert.Equal(2026, query.Year);
        Assert.Equal("Infirmary", query.Title);
    }

    [Fact]
    public void MismatchesLanguage_AllowsEnglishAndBulgarian()
    {
        var english = Encoding.UTF8.GetBytes(
            "1\n00:00:01,000 --> 00:00:02,000\nStay quiet.\n\n2\n00:00:03,000 --> 00:00:04,000\nThe night nurse is here.\n\n3\n00:00:05,000 --> 00:00:06,000\nYou don't have to get up.\n\n4\n00:00:07,000 --> 00:00:08,000\nI have to see my new client.\n");
        var bulgarian = Encoding.UTF8.GetBytes(
            "1\n00:00:01,000 --> 00:00:02,000\nСпокойно.\n\n2\n00:00:03,000 --> 00:00:04,000\nНощната сестра е тук.\n\n3\n00:00:05,000 --> 00:00:06,000\nНе ставайте.\n\n4\n00:00:07,000 --> 00:00:08,000\nЩе видя новата си пациентка.\n");
        Assert.False(SubtitleBytes.MismatchesLanguage(english, "en"));
        Assert.False(SubtitleBytes.MismatchesLanguage(bulgarian, "bg"));
        Assert.True(SubtitleBytes.MismatchesLanguage(bulgarian, "en"));
    }

    [Fact]
    public void IsUsableSidecar_RejectsEmptyAndHtml()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var empty = Path.Combine(folder.FullName, "empty.en.srt");
            File.WriteAllBytes(empty, []);
            var html = Path.Combine(folder.FullName, "page.en.srt");
            File.WriteAllText(html, "<!DOCTYPE html>\n<html lang=\"bg\"><head><title>Субтитри BG</title></head></html>");
            var real = Path.Combine(folder.FullName, "real.en.srt");
            File.WriteAllText(real, "1\n00:00:01,000 --> 00:00:02,000\nStay quiet.\n\n2\n00:00:03,000 --> 00:00:04,000\nThe night nurse is here.\n\n3\n00:00:05,000 --> 00:00:06,000\nYou don't have to get up.\n\n4\n00:00:07,000 --> 00:00:08,000\nI have to see my new client.\n");

            Assert.False(SubtitleBytes.IsUsableSidecar(empty, "en"));
            Assert.False(SubtitleBytes.IsUsableSidecar(html, "en"));
            Assert.True(SubtitleBytes.IsUsableSidecar(real, "en"));
            Assert.True(SubtitleBytes.IsUsableSidecar(null, "en"));
            Assert.False(SubtitleDownloadService.HasLanguage(
                [new Subtitles { Path = html, Language = "en", FileName = "page", Format = ".srt" }],
                "en"));
            Assert.True(SubtitleDownloadService.HasLanguage(
                [new Subtitles { Path = real, Language = "en", FileName = "real", Format = ".srt" }],
                "en"));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
