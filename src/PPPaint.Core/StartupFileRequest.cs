namespace PPPaint.Core;

public static class StartupFileRequest
{
    public const string ProtocolScheme = "pppaint";

    public static string? FindSupportedPath(IEnumerable<string>? commandLineArguments)
    {
        if (commandLineArguments is null)
        {
            return null;
        }

        foreach (var argument in commandLineArguments.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            if (TryGetProtocolPath(argument, out var protocolPath))
            {
                return protocolPath;
            }

            if (argument.StartsWith('-'))
            {
                continue;
            }

            if (TryNormalizeSupportedPath(argument, requireFullyQualified: false, out var fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    public static bool TryGetProtocolPath(string? argument, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(argument) ||
            !Uri.TryCreate(argument, UriKind.Absolute, out var uri) ||
            !(uri.Scheme.Equals(ProtocolScheme, StringComparison.OrdinalIgnoreCase) ||
              uri.Scheme.Equals("chunpaint", StringComparison.OrdinalIgnoreCase)) ||
            !uri.Host.Equals("open", StringComparison.OrdinalIgnoreCase) ||
            (uri.AbsolutePath.Length > 0 && uri.AbsolutePath != "/") ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var query = uri.Query.TrimStart('?');
        var fields = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 1)
        {
            return false;
        }

        var separator = fields[0].IndexOf('=');
        if (separator <= 0 ||
            !fields[0][..separator].Equals("path", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string path;
        try
        {
            path = Uri.UnescapeDataString(fields[0][(separator + 1)..]);
        }
        catch (UriFormatException)
        {
            return false;
        }

        return TryNormalizeSupportedPath(path, requireFullyQualified: true, out fullPath);
    }

    private static bool TryNormalizeSupportedPath(
        string path,
        bool requireFullyQualified,
        out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) ||
            (requireFullyQualified && !Path.IsPathFullyQualified(path)))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            fullPath = string.Empty;
            return false;
        }

        if (!DocumentFileFormats.IsSupported(fullPath))
        {
            fullPath = string.Empty;
            return false;
        }

        return true;
    }
}
