using System.Collections.Generic;
using System.Diagnostics;
using LaunchPad.Ignite.Core.Execution;

namespace LaunchPad.Ignite.App.Services;

public sealed class SystemUrlTargetOpener : IUrlTargetOpener
{
    public void Open(IEnumerable<UrlTarget> targets)
    {
        foreach (var target in targets)
        {
            Process.Start(new ProcessStartInfo(target.Value)
            {
                UseShellExecute = true
            });
        }
    }
}
