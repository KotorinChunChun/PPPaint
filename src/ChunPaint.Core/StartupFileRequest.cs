namespace ChunPaint.Core;

public static class StartupFileRequest
{
    public static string? FindSupportedPath(IEnumerable<string>? commandLineArguments)
    {
        if (commandLineArguments is null)
        {
            return null;
        }

        foreach (var argument in commandLineArguments.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(argument) || argument.StartsWith('-'))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(argument);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (DocumentFileFormats.IsSupported(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }
}
