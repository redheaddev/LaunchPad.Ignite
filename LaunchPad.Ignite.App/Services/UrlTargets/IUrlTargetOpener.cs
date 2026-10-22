using System.Collections.Generic;
using LaunchPad.Ignite.Core.Execution;

namespace LaunchPad.Ignite.App.Services.UrlTargets;

public interface IUrlTargetOpener
{
    void Open(IEnumerable<UrlTarget> targets, bool newWindow = false);
}
