using System.Threading.Tasks;

namespace LaunchPad.Ignite.App.Services;

public interface ISecretEntryDialogService
{
    Task<string?> ShowAsync(string title, bool requireConfirmation);

    Task ShowSecretAsync(string title, string value);
}
