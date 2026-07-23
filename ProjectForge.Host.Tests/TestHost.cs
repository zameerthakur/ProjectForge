using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ProjectForge.Host.Tests;

internal sealed class TestHost : IDisposable
{
    private readonly string _temporaryDirectory;
    private readonly WebApplicationFactory<Program> _application;
    private bool _disposed;

    public TestHost()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "ProjectForge.Host.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temporaryDirectory);

        var databasePath = Path.Combine(
            _temporaryDirectory,
            "workflows.db");
        _application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(
                builder =>
                {
                    builder.UseEnvironment("Testing");
                    builder.UseSetting(
                        "ProjectForge:DatabasePath",
                        databasePath);
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
        Directory.Delete(_temporaryDirectory, recursive: true);
        _disposed = true;
    }
}
