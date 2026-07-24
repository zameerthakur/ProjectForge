using System.Net;
using System.Security.Cryptography;
using ProjectForge.Abstractions.Provisioning;
using ProjectForge.Abstractions.Provisioning.Manifests;
using ProjectForge.Infrastructure.Provisioning.Cache;
using ProjectForge.Infrastructure.Provisioning.Downloads;

namespace ProjectForge.Infrastructure.Tests.Provisioning.Downloads;

public sealed class VerifiedArtifactDownloaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ProjectForge.VerifiedDownloads.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DownloadStreamsAndPromotesVerifiedArtifact()
    {
        var content = "trusted artifact"u8.ToArray();
        var (downloader, handler) = CreateDownloader(
            (_, _) => Response(HttpStatusCode.OK, content));

        var result = await downloader.DownloadAsync(Manifest(content), 1);

        Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(result, "artifact.download")));
        Assert.Single(handler.Requests);
        Assert.False(Directory.EnumerateDirectories(
            Path.GetDirectoryName(result)!,
            "*.partial-*").Any());
    }

    [Fact]
    public async Task SizeMismatchFailsAndRemovesOwnedStaging()
    {
        var content = "short"u8.ToArray();
        var (downloader, _) = CreateDownloader(
            (_, _) => Response(HttpStatusCode.OK, content));
        var manifest = Manifest("expected"u8.ToArray());

        var exception = await Assert.ThrowsAsync<ArtifactDownloadException>(
            () => downloader.DownloadAsync(manifest, 1));

        Assert.Contains("after 1 attempt", exception.Message);
        AssertNoPartialDirectories();
    }

    [Fact]
    public async Task DigestMismatchFailsAndRemovesOwnedStaging()
    {
        var content = "same-size-a"u8.ToArray();
        var expected = "same-size-b"u8.ToArray();
        var (downloader, _) = CreateDownloader(
            (_, _) => Response(HttpStatusCode.OK, content));

        await Assert.ThrowsAsync<ArtifactDownloadException>(
            () => downloader.DownloadAsync(Manifest(expected), 1));

        AssertNoPartialDirectories();
    }

    [Fact]
    public async Task HttpFailureUsesOnlyExplicitAttemptLimit()
    {
        var content = "artifact"u8.ToArray();
        var (downloader, handler) = CreateDownloader(
            (_, _) => Response(HttpStatusCode.ServiceUnavailable, []));

        await Assert.ThrowsAsync<ArtifactDownloadException>(
            () => downloader.DownloadAsync(Manifest(content), 2));

        Assert.Equal(2, handler.Requests.Count);
        AssertNoPartialDirectories();
    }

    [Fact]
    public async Task CancellationRemovesTheExactOwnedStagingDirectory()
    {
        var content = "artifact"u8.ToArray();
        using var cancellation = new CancellationTokenSource();
        var (downloader, _) = CreateDownloader(
            (_, token) =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => downloader.DownloadAsync(Manifest(content), 1, cancellation.Token));

        AssertNoPartialDirectories();
    }

    [Fact]
    public async Task OversizedUnknownLengthStreamStopsAtTrustedLimit()
    {
        var expected = "small"u8.ToArray();
        var oversized = "larger-than-small"u8.ToArray();
        var (downloader, _) = CreateDownloader((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(oversized))
            };
            response.Content.Headers.ContentLength = null;
            return response;
        });

        await Assert.ThrowsAsync<ArtifactDownloadException>(
            () => downloader.DownloadAsync(Manifest(expected), 1));

        AssertNoPartialDirectories();
    }

    [Theory]
    [InlineData("http://downloads.example/runtime.zip")]
    [InlineData("https://user:secret@downloads.example/runtime.zip")]
    public async Task RedirectPolicyRejectsUntrustedTargets(string target)
    {
        var content = "artifact"u8.ToArray();
        var (downloader, handler) = CreateDownloader((_, _) =>
        {
            var response = Response(HttpStatusCode.Redirect, []);
            response.Headers.Location = new Uri(target);
            return response;
        });

        await Assert.ThrowsAsync<ArtifactDownloadException>(
            () => downloader.DownloadAsync(Manifest(content), 1));

        Assert.Single(handler.Requests);
        AssertNoPartialDirectories();
    }

    [Fact]
    public async Task HttpsRedirectIsFollowedAndVerified()
    {
        var content = "artifact"u8.ToArray();
        var (downloader, handler) = CreateDownloader((request, _) =>
        {
            if (request.RequestUri!.Host == "downloads.example")
            {
                var redirect = Response(HttpStatusCode.Redirect, []);
                redirect.Headers.Location = new Uri("https://cdn.example/runtime.zip");
                return redirect;
            }

            return Response(HttpStatusCode.OK, content);
        });

        var result = await downloader.DownloadAsync(Manifest(content), 1);

        Assert.Equal(2, handler.Requests.Count);
        Assert.True(File.Exists(Path.Combine(result, "artifact.download")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private (VerifiedArtifactDownloader Downloader, FakeHandler Handler) CreateDownloader(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory)
    {
        var handler = new FakeHandler(responseFactory);
        var client = new HttpClient(handler);
        var cache = new ManagedCache(new ManagedCachePaths(_root));
        return (new VerifiedArtifactDownloader(client, cache), handler);
    }

    private static TrustedArtifactManifest Manifest(byte[] expected) =>
        new()
        {
            Requirement = new ProvisioningRequirement
            {
                Id = "runtime",
                DisplayName = "Test runtime",
                Version = "1.0.0",
                Kind = ProvisioningArtifactKind.Executable
            },
            Platform = new ArtifactPlatform
            {
                OperatingSystem = "windows",
                Architecture = "x64"
            },
            ArtifactUri = new Uri("https://downloads.example/runtime.zip"),
            Sha256 = Convert.ToHexString(SHA256.HashData(expected)),
            ArchiveKind = ArtifactArchiveKind.Zip,
            EntryPoint = "runtime.exe",
            SizeInBytes = expected.LongLength,
            LicenseIdentity = "test-license",
            ConsentPolicy = ArtifactConsentPolicy.NotRequired
        };

    private static HttpResponseMessage Response(HttpStatusCode status, byte[] content) =>
        new(status) { Content = new ByteArrayContent(content) };

    private void AssertNoPartialDirectories()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        Assert.Empty(Directory.EnumerateDirectories(_root, "*.partial-*", SearchOption.AllDirectories));
    }

    private sealed class FakeHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(responseFactory(request, cancellationToken));
        }
    }
}
