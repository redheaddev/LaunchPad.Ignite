using System.Collections.Generic;
using LaunchPad.Ignite.Core.Execution;

namespace LaunchPad.Ignite.App.Services;

public interface IUrlTargetOpener
{
    void Open(IEnumerable<UrlTarget> targets);
}
