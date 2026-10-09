namespace LaunchPad.Ignite.App.Services;

public interface ISecretAccessGate
{
    bool HasMasterPassword { get; }

    bool IsUnlocked { get; }

    bool TrySetMasterPassword(string password);

    bool TryUnlock(string password);

    void Lock();
}
