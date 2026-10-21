using System;
using System.Threading.Tasks;

namespace LaunchPad.Ignite.App.Services;

public interface IClipboardService
{
    DateTimeOffset? ExpirationTime { get; }

    TimeSpan ExpirationDuration { get; }

    Task CopyWithExpiryAsync(string value, TimeSpan expiry);

    bool ExtendExpiry(TimeSpan extension);
}
