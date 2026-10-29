using System.Security.Cryptography;
using System.Text;

namespace PPPaint.Core;

public sealed record LaunchOptions(string SettingsDirectory, bool IsolatedSettings, string LaunchOrigin, string? FilePath)
{
    public string ToolId => "pppaint" + (IsolatedSettings ? "-" + SettingsKey(SettingsDirectory) : "");

    public static string SettingsKey(string path) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Path.GetFullPath(path).Replace('/', '\\').ToLowerInvariant()))).ToLowerInvariant()[..16];

    public static LaunchOptions Parse(IEnumerable<string> arguments, string? dataDirectory = null)
    {
        var values = arguments.ToArray();
        string? settings = null;
        var origin = "standalone";
        var fileArguments = new List<string> { values.FirstOrDefault() ?? "PPPaint.exe" };
        for (var index = 1; index < values.Length; index++)
        {
            var argument = values[index];
            var separator = argument.IndexOf('=');
            var option = separator >= 0 ? argument[..separator] : argument;
            if (option is "--startup" or "--hidden") throw new ArgumentException("PPPaintは無表示常駐起動に対応していません。");
            if (option is "--settings-dir" or "--launch-origin")
            {
                var value = separator >= 0 ? argument[(separator + 1)..] : ++index < values.Length ? values[index] : "";
                if (string.IsNullOrWhiteSpace(value) || value.StartsWith("--")) throw new ArgumentException("起動引数の値がありません。");
                if (option == "--settings-dir") settings = value;
                else origin = value switch { "kotorintoys" => "host_manual", "host_manual" or "via_kotorintoys" or "direct_task" => value, _ => "standalone" };
            }
            else fileArguments.Add(argument);
        }
        settings ??= string.IsNullOrWhiteSpace(dataDirectory) ? null : dataDirectory;
        if (settings is not null && !Path.IsPathFullyQualified(settings)) throw new ArgumentException("設定フォルダーは絶対パスで指定してください。");
        return new LaunchOptions(Path.GetFullPath(settings ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PPPaint")),
            settings is not null, origin, StartupFileRequest.FindSupportedPath(fileArguments));
    }
}
