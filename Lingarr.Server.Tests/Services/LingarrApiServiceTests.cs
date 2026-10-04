using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Server.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class LingarrApiServiceTests
{
    [Fact]
    public async Task GetLatestVersion_UsesForkRelease()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(
                "https://api.github.com/repos/apoapostolov/lingarr-next/releases/latest",
                request.RequestUri?.ToString());
            Assert.Contains("application/vnd.github+json", request.Headers.Accept.Select(value => value.MediaType));
            Assert.Equal("2022-11-28", request.Headers.GetValues("X-GitHub-Api-Version").Single());

            return Json(HttpStatusCode.OK, """{"tag_name":"v2.4.1"}""");
        });

        var service = CreateService(handler);

        Assert.Equal("2.4.1", await service.GetLatestVersion());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetLatestVersion_FallsBackToHighestSemanticForkTag()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/releases/latest") == true)
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }

            Assert.Equal(
                "https://api.github.com/repos/apoapostolov/lingarr-next/tags?per_page=100",
                request.RequestUri?.ToString());
            return Json(
                HttpStatusCode.OK,
                """[{"name":"nightly"},{"name":"1.9.0"},{"name":"v2.0.1"},{"name":"2.0.0"}]""");
        });

        var service = CreateService(handler);

        Assert.Equal("2.0.1", await service.GetLatestVersion());
        Assert.Equal(2, handler.Requests.Count);
    }

    private static LingarrApiService CreateService(HttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, false));

        return new LingarrApiService(
            factory.Object,
            NullLogger<LingarrApiService>.Instance,
            new MemoryCache(new MemoryCacheOptions()));
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string content)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responseFactory(request));
        }
    }
}
