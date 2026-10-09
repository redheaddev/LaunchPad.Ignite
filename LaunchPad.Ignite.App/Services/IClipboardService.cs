using System;
using System.Threading.Tasks;

namespace LaunchPad.Ignite.App.Services;

public interface IClipboardService
{
    Task CopyWithExpiryAsync(string value, TimeSpan expiry);
}
