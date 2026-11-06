using LaunchPad.Ignite.Extension.Windows;

namespace LaunchPad.Ignite.Tests;

public class WindowsAppCatalogTests
{
    [Fact]
    public void AppCanBeResolvedByFriendlyNameOrLegacyId()
    {
        using var directory = new TemporaryDirectory();
        var appDirectory = Path.Combine(directory.Path, "Programs", "Startup");
        Directory.CreateDirectory(appDirectory);
        File.WriteAllBytes(Path.Combine(appDirectory, "Accountable2You.lnk"), []);
        var catalog = new StartMenuAppCatalog([("User", directory.Path)]);
        var app = Assert.Single(catalog.GetApps());

        Assert.Equal("Accountable2You", app.Name);
        Assert.NotEqual(app.Name, app.Id);
        Assert.Equal(app, catalog.FindApp(app.Name));
        Assert.Equal(app, catalog.FindApp(app.Id));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}