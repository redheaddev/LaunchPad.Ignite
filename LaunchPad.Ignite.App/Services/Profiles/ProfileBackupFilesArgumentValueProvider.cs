using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LaunchPad.Ignite.Core.Lists;

namespace LaunchPad.Ignite.App.Services.Profiles;

public sealed class ProfileBackupFilesArgumentValueProvider : IArgumentValueProvider
{
    public const string Reference = "list:profileBackups";

    private readonly ProfileBackupService _backupService;

    public ProfileBackupFilesArgumentValueProvider(ProfileBackupService backupService)
    {
        _backupService = backupService;
    }

    public IReadOnlyList<string> GetValues(string reference)
        => string.Equals(reference, Reference, StringComparison.OrdinalIgnoreCase)
            ? _backupService.GetBackupFiles().Select(Path.GetFileName).Where(name => name is not null).Cast<string>().ToArray()
            : Array.Empty<string>();
}
