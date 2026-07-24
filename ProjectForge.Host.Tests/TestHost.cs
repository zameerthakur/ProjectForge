using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ProjectForge.Host.Tests;

internal sealed class TestHost : IDisposable
{
    private readonly string _temporaryDirectory;
    private readonly bool _ownsTemporaryDirectory;
    private readonly WebApplicationFactory<Program> _application;
    private bool _disposed;

    public TestHost(string? databasePath = null)
    {
        _temporaryDirectory = databasePath is null
            ? Path.Combine(
                Path.GetTempPath(),
                "ProjectForge.Host.Tests",
                Guid.NewGuid().ToString("N"))
            : Path.GetDirectoryName(Path.GetFullPath(databasePath)) ??
                throw new ArgumentException(
                    "The database path must include a directory.",
                    nameof(databasePath));
        Directory.CreateDirectory(_temporaryDirectory);

        var configuredDatabasePath = databasePath ??
            Path.Combine(_temporaryDirectory, "workflows.db");
        _ownsTemporaryDirectory = databasePath is null;
        _application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(
                builder =>
                {
                    builder.UseEnvironment("Testing");
                    builder.UseSetting(
                        "ProjectForge:DatabasePath",
                        configuredDatabasePath);
                    builder.UseSetting(
                        "ProjectForge:ArtifactRoot",
                        Path.Combine(_temporaryDirectory, "artifacts"));
                    builder.UseSetting(
                        "ProjectForge:ExecutionTimeoutSeconds",
                        "30");
                });
        Client = _application.CreateClient();
    }

    public HttpClient Client { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Client.Dispose();
        _application.Dispose();
        if (_ownsTemporaryDirectory)
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }

        _disposed = true;
    }
}
