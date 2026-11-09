using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace LaunchPad.Ignite.App.Services.Secrets;

public sealed class EncryptedSecretFileService
{
    private const int FormatVersion = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int KeyDerivationIterations = 600_000;
    private const int MaximumFileSize = 10 * 1024 * 1024;
    private const int MaximumSecretCount = 10_000;
    private static readonly byte[] AssociatedData = "LaunchPad.Ignite.Secrets.v1"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public void WriteFile(string path, IReadOnlyDictionary<string, string> secrets, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(secrets);
        ValidatePassword(password);

        if (secrets.Count > MaximumSecretCount)
        {
            throw new InvalidDataException($"A secrets file cannot contain more than {MaximumSecretCount} secrets.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = secrets.Select(pair =>
        {
            ValidateEntry(pair.Key, pair.Value);
            if (!names.Add(pair.Key))
            {
                throw new InvalidDataException($"The secrets collection contains duplicate name '{pair.Key}'.");
            }

            return new SecretEntry(pair.Key, pair.Value);
        }).ToArray();
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new SecretFilePayload(FormatVersion, entries));
        if (plaintext.Length > MaximumFileSize)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new InvalidDataException("The secrets collection is too large to export.");
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, KeyDerivationIterations, HashAlgorithmName.SHA256, KeySize);
        try
        {
            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);
            }

            var envelope = new EncryptedSecretFile(FormatVersion, Convert.ToBase64String(salt), Convert.ToBase64String(nonce), Convert.ToBase64String(ciphertext), Convert.ToBase64String(tag));
            var fileContents = JsonSerializer.SerializeToUtf8Bytes(envelope);
            if (fileContents.Length > MaximumFileSize)
            {
                throw new InvalidDataException("The encrypted secrets file is too large to export.");
            }

            WriteAtomically(path, fileContents);
            CryptographicOperations.ZeroMemory(fileContents);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public IReadOnlyDictionary<string, string> ReadFile(string path, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ValidatePassword(password);

        var fileContents = File.ReadAllBytes(path);
        if (fileContents.Length == 0 || fileContents.Length > MaximumFileSize)
        {
            CryptographicOperations.ZeroMemory(fileContents);
            throw new InvalidDataException("The secrets file is empty or too large.");
        }

        byte[]? plaintext = null;
        byte[]? key = null;
        try
        {
            var envelope = JsonSerializer.Deserialize<EncryptedSecretFile>(fileContents, JsonOptions)
                ?? throw new InvalidDataException("The secrets file is invalid.");
            if (envelope.Version != FormatVersion)
            {
                throw new InvalidDataException("The secrets file uses an unsupported format version.");
            }

            var salt = DecodeBase64(envelope.Salt, SaltSize, "salt");
            var nonce = DecodeBase64(envelope.Nonce, NonceSize, "nonce");
            var ciphertext = DecodeBase64(envelope.Ciphertext, null, "ciphertext");
            var tag = DecodeBase64(envelope.Tag, TagSize, "authentication tag");
            key = Rfc2898DeriveBytes.Pbkdf2(password, salt, KeyDerivationIterations, HashAlgorithmName.SHA256, KeySize);
            plaintext = new byte[ciphertext.Length];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData);
            }
            catch (CryptographicException exception)
            {
                throw new InvalidDataException("The password is incorrect or the secrets file has been modified.", exception);
            }

            var payload = JsonSerializer.Deserialize<SecretFilePayload>(plaintext, JsonOptions)
                ?? throw new InvalidDataException("The decrypted secrets payload is invalid.");
            if (payload.Version != FormatVersion || payload.Secrets is null || payload.Secrets.Length > MaximumSecretCount)
            {
                throw new InvalidDataException("The decrypted secrets payload is invalid or unsupported.");
            }

            var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in payload.Secrets)
            {
                if (entry is null)
                {
                    throw new InvalidDataException("The decrypted secrets payload contains an invalid entry.");
                }

                ValidateEntry(entry.Name, entry.Value);
                if (!secrets.TryAdd(entry.Name, entry.Value))
                {
                    throw new InvalidDataException($"The decrypted secrets payload contains duplicate name '{entry.Name}'.");
                }
            }

            return secrets;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The secrets file is invalid.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fileContents);
            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }

            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    private static byte[] DecodeBase64(string? value, int? expectedLength, string fieldName)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(value ?? string.Empty);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"The secrets file has an invalid {fieldName}.", exception);
        }

        if ((expectedLength.HasValue && bytes.Length != expectedLength.Value)
            || (fieldName == "ciphertext" && bytes.Length > MaximumFileSize))
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new InvalidDataException($"The secrets file has an invalid {fieldName}.");
        }

        return bytes;
    }

    private static void ValidateEntry(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\0') || value is null)
        {
            throw new InvalidDataException("A secret entry has an invalid name or value.");
        }
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
        {
            throw new ArgumentException("A password with at least 12 characters is required.", nameof(password));
        }
    }

    private static void WriteAtomically(string path, byte[] contents)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, contents);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record EncryptedSecretFile(int Version, string Salt, string Nonce, string Ciphertext, string Tag);
    private sealed record SecretFilePayload(int Version, SecretEntry[] Secrets);
    private sealed record SecretEntry(string Name, string Value);
}
