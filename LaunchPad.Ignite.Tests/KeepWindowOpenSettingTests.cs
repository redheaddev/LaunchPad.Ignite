using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Services.Profiles;

namespace LaunchPad.Ignite.Tests;

public sealed class KeepWindowOpenSettingTests
{
    [Fact]
    public void KeepWindowOpenIsAvailableAsAnOffByDefaultGeneralSetting()
    {
        var setting = new ApplicationSettingsLoader().Load()
            .Single(candidate => candidate.QualifiedName == "app:keepWindowOpen");

        Assert.Equal("Keep window open", setting.Label);
        Assert.Equal("This will prevent the window from closing on execution.", setting.Description);
        Assert.Equal("Off", setting.DefaultValue);
        Assert.Equal("select", setting.Type);
        Assert.Equal(["Off", "On"], setting.Options);
    }
}
