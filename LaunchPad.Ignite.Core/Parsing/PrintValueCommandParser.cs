namespace LaunchPad.Ignite.Core.Parsing;

public static class PrintValueCommandParser
{
    public static bool TryParse(string commandText, out string expression)
    {
        expression = string.Empty;
        if (commandText.Length == 0 || commandText[0] != '?'
            || (commandText.Length > 1 && !char.IsWhiteSpace(commandText[1])))
        {
            return false;
        }

        expression = commandText[1..].TrimStart();
        return true;
    }
}
