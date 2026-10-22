using System.Text;

namespace LaunchPad.Ignite.App.Services.Platform.ShellCommands;

internal static class ShellCommandArgumentQuoter
{
    public static string Quote(string argument)
    {
        var builder = new StringBuilder("\"");
        var backslashCount = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashCount++;
            }
            else if (character == '\"')
            {
                builder.Append('\\', backslashCount * 2 + 1);
                builder.Append(character);
                backslashCount = 0;
            }
            else
            {
                builder.Append('\\', backslashCount);
                builder.Append(character);
                backslashCount = 0;
            }
        }

        builder.Append('\\', backslashCount * 2);
        builder.Append('\"');
        return builder.ToString();
    }
}
