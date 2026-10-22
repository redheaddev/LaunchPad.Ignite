using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LaunchPad.Ignite.App.Services.Secrets;

public sealed class SecretAccessGate : ISecretAccessGate
{
    private const int SaltSize = 16;
    private const int VerifierSize = 32;
    private const int Iterations = 600_000;
    private static readonly TimeSpan UnlockDuration = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _statePath;
    private readonly TimeProvider _timeProvider;
    private MasterPasswordState? _state;
    private DateTimeOffset? _unlockedUntil;

    public SecretAccessGate()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LaunchPad.Ignite",
            "secret-master.json"))
    {
    }

    public SecretAccessGate(string statePath, TimeProvider? timeProvider = null)
    {
        _statePath = statePath;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _state = LoadState(statePath);
    }

    public bool HasMasterPassword => _state is not null;

    public bool IsUnlocked
    {
        get
        {
            if (_state is null)
            {
                return true;
            }

            if (_unlockedUntil is null || _timeProvider.GetUtcNow() >= _unlockedUntil.Value)
            {
                _unlockedUntil = null;
                return false;
            }

            return true;
        }
    }

    public bool TrySetMasterPassword(string password)
    {
        if (string.IsNullOrEmpty(password) || (_state is not null && !IsUnlocked))
        {
            return false;
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var verifier = DeriveVerifier(password, salt, Iterations);
        try
        {
            var state = new MasterPasswordState(
                Convert.ToBase64String(salt),
                Convert.ToBase64String(verifier),
                Iterations);
            SaveState(_statePath, state);
            _state = state;
            _unlockedUntil = _timeProvider.GetUtcNow().Add(UnlockDuration);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(verifier);
        }
    }

    public bool TryUnlock(string password)
    {
        if (_state is null || string.IsNullOrEmpty(password))
        {
            return false;
        }

        var salt = Convert.FromBase64String(_state.Salt);
        var expectedVerifier = Convert.FromBase64String(_state.Verifier);
        var actualVerifier = DeriveVerifier(password, salt, _state.Iterations);
        try
        {
            var unlocked = CryptographicOperations.FixedTimeEquals(expectedVerifier, actualVerifier);
            _unlockedUntil = unlocked ? _timeProvider.GetUtcNow().Add(UnlockDuration) : null;
            return unlocked;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(expectedVerifier);
            CryptographicOperations.ZeroMemory(actualVerifier);
        }
    }

    public void Lock()
    {
        _unlockedUntil = null;
    }

    private static MasterPasswordState? LoadState(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var state = JsonSerializer.Deserialize<MasterPasswordState>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The master-password state file is invalid.");
        if (state.Iterations is < 100_000 or > 2_000_000
            || Convert.FromBase64String(state.Salt).Length != SaltSize
            || Convert.FromBase64String(state.Verifier).Length != VerifierSize)
        {
            throw new InvalidDataException("The master-password state file is invalid.");
        }

        return state;
    }

    private static void SaveState(string path, MasterPasswordState state)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static byte[] DeriveVerifier(string password, byte[] salt, int iterations)
        => Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            VerifierSize);

    private sealed record MasterPasswordState(string Salt, string Verifier, int Iterations);
}
