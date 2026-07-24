using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using ProjectForge.Abstractions.Provisioning.Manifests;
using ProjectForge.Infrastructure.Provisioning.Cache;

namespace ProjectForge.Infrastructure.Provisioning.Downloads;

public sealed class VerifiedArtifactDownloader
{
    private const int MaximumRedirects = 5;
    private readonly HttpClient _httpClient;
    private readonly ManagedCache _cache;

    public VerifiedArtifactDownloader(HttpClient httpClient, ManagedCache cache)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<string> DownloadAsync(
        TrustedArtifactManifest manifest,
        int maximumAttempts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateDownloadFields(manifest);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumAttempts);

        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var staging = _cache.CreateStaging(
                manifest.Requirement.Id,
                manifest.Requirement.Version,
                $"{manifest.Platform.OperatingSystem}-{manifest.Platform.Architecture}");
            var destination = Path.Combine(staging.StagingPath, "artifact.download");

            try
            {
                await DownloadAndVerifyAsync(manifest, destination, cancellationToken)
                    .ConfigureAwait(false);
                return _cache.PromoteVerified(staging);
            }
            catch (OperationCanceledException)
            {
                _cache.DiscardStaging(staging);
                throw;
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException or ArtifactDownloadException)
            {
                _cache.DiscardStaging(staging);
                lastFailure = exception;
            }
        }

        throw new ArtifactDownloadException(
            $"The artifact download failed after {maximumAttempts} attempt(s).",
            lastFailure!);
    }

    internal static void ValidateTrustedUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException(
                "Artifact download targets must be absolute HTTPS URIs without embedded credentials.");
        }
    }

    private static void ValidateDownloadFields(TrustedArtifactManifest manifest)
    {
        if (manifest.Requirement is null || manifest.Platform is null)
        {
            throw new ArgumentException(
                "The artifact requirement and platform are required.",
                nameof(manifest));
        }

        ValidateTrustedUri(manifest.ArtifactUri);
        if (!string.IsNullOrEmpty(manifest.ArtifactUri.Query) ||
            !string.IsNullOrEmpty(manifest.ArtifactUri.Fragment))
        {
            throw new ArgumentException(
                "Artifact download targets cannot contain a query or fragment.",
                nameof(manifest));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(manifest.SizeInBytes);
        if (manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "The trusted SHA-256 digest must contain 64 hexadecimal characters.",
                nameof(manifest));
        }
    }

    private async Task DownloadAndVerifyAsync(
        TrustedArtifactManifest manifest,
        string destination,
        CancellationToken cancellationToken)
    {
        using var response = await SendWithSafeRedirectsAsync(
            manifest.ArtifactUri,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new ArtifactDownloadException(
                $"The artifact server returned HTTP {(int)response.StatusCode}.");
        }

        if (response.Content.Headers.ContentLength is long declaredLength &&
            declaredLength != manifest.SizeInBytes)
        {
            throw new ArtifactDownloadException("The artifact size does not match the trusted manifest.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var target = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        long total = 0;

        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > manifest.SizeInBytes)
                {
                    throw new ArtifactDownloadException(
                        "The artifact stream exceeded the trusted size limit.");
                }

                digest.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (total != manifest.SizeInBytes)
        {
            throw new ArtifactDownloadException("The artifact size does not match the trusted manifest.");
        }

        var actualDigest = Convert.ToHexString(digest.GetHashAndReset());
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualDigest),
                Convert.FromHexString(manifest.Sha256)))
        {
            throw new ArtifactDownloadException("The artifact digest does not match the trusted manifest.");
        }
    }

    private async Task<HttpResponseMessage> SendWithSafeRedirectsAsync(
        Uri initialUri,
        CancellationToken cancellationToken)
    {
        var currentUri = initialUri;
        for (var redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            ValidateTrustedUri(currentUri);
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (!IsRedirect(response.StatusCode))
            {
                return response;
            }

            if (redirect == MaximumRedirects || response.Headers.Location is null)
            {
                response.Dispose();
                throw new ArtifactDownloadException("The artifact redirect policy was not satisfied.");
            }

            var location = response.Headers.Location;
            currentUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
            response.Dispose();
            try
            {
                ValidateTrustedUri(currentUri);
            }
            catch (ArgumentException exception)
            {
                throw new ArtifactDownloadException(
                    "The artifact redirect target is not trusted.",
                    exception);
            }
        }

        throw new ArtifactDownloadException("The artifact redirect limit was exceeded.");
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently or
            HttpStatusCode.Redirect or
            HttpStatusCode.RedirectMethod or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

}
