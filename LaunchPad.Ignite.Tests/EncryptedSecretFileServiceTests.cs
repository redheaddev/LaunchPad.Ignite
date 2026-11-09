using System.IO;
using System.Text.Json.Nodes;
using LaunchPad.Ignite.App.Services.Secrets;
using Xunit;

namespace LaunchPad.Ignite.Tests;

public sealed class EncryptedSecretFileServiceTests
{
    [Fact]
    public void WritesEncryptedFileAndReadsSecretsBack()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "secrets.lpsecrets");
        var secrets = new Dictionary<string, string>
        {
            ["api-token"] = "sensitive-value",
            ["database-password"] = "another-sensitive-value"
        };
        var service = new EncryptedSecretFileService();

        service.WriteFile(path, secrets, "correct horse battery staple");
        var fileContents = File.ReadAllText(path);
        var imported = service.ReadFile(path, "correct horse battery staple");

        Assert.DoesNotContain("sensitive-value", fileContents, StringComparison.Ordinal);
        Assert.DoesNotContain("api-token", fileContents, StringComparison.Ordinal);
        Assert.Equal(secrets, imported);
    }

    [Fact]
    public void RejectsIncorrectPassword()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "secrets.lpsecrets");
        var service = new EncryptedSecretFileService();
        service.WriteFile(path, new Dictionary<string, string> { ["token"] = "value" }, "correct horse battery staple");

        Assert.Throws<InvalidDataException>(() => service.ReadFile(path, "incorrect password"));
    }

    [Fact]
    public void RejectsModifiedCiphertext()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "secrets.lpsecrets");
        var service = new EncryptedSecretFileService();
        service.WriteFile(path, new Dictionary<string, string> { ["token"] = "value" }, "correct horse battery staple");
        var envelope = JsonNode.Parse(File.ReadAllText(path))!;
        var ciphertext = envelope["Ciphertext"]!.GetValue<string>().ToCharArray();
        ciphertext[0] = ciphertext[0] == 'A' ? 'B' : 'A';
        envelope["Ciphertext"] = new string(ciphertext);
        File.WriteAllText(path, envelope.ToJsonString());

        Assert.Throws<InvalidDataException>(() => service.ReadFile(path, "correct horse battery staple"));
    }

    private sealed class TemporaryDirectory : System.IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
