using System.Diagnostics;

namespace LaunchPad.Ignite.App.Services;

public interface IProcessStarter
{
    void Start(ProcessStartInfo startInfo);
}
