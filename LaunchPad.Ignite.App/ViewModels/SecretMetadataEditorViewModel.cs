using System.Collections.Generic;
using System.Linq;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Services.Profiles;

namespace LaunchPad.Ignite.App.ViewModels;

public sealed class SecretMetadataEditorViewModel : ObjectMetadataEditorViewModel
{
    public SecretMetadataEditorViewModel(ProfileSettingsStore settingsStore, IReadOnlyList<string> secretNames)
        : base(settingsStore, MetadataObjectType.Secret, secretNames
            .Select(name => new ObjectMetadataTarget(MetadataObjectType.Secret, name, name))
            .ToArray())
    {
    }
}
