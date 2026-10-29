using System.Diagnostics;

namespace PPPaint;

public partial class App
{
    private static void WriteStartupFailure(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Options.SettingsDirectory);
            File.WriteAllText(Path.Combine(Options.SettingsDirectory, "startup-error.log"),
                DateTimeOffset.UtcNow.ToString("O") + Environment.NewLine + exception + Environment.NewLine + RestrictedDetails(exception));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static string RestrictedDetails(Exception exception)
    {
        var details = new System.Text.StringBuilder();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            foreach (System.Collections.DictionaryEntry value in current.Data)
            {
                var key = value.Key?.ToString() ?? "";
                if (key.StartsWith("Restricted", StringComparison.Ordinal))
                    details.AppendLine(key + ": " + (value.Value is string text ? text : value.Value?.GetType().FullName));
            }
        }
        return details.ToString();
    }

    private void InitializeDiagnostics()
    {
        UnhandledException += (_, args) =>
        {
            try
            {
                var directory = Options?.SettingsDirectory;
                if (directory is null) return;
                Directory.CreateDirectory(directory);
                // 例外本文には外部サービスや入力内容が含まれ得るため、型と呼出箇所だけを記録する。
                File.WriteAllText(Path.Combine(directory, "crash-diagnostic.txt"),
                    DateTimeOffset.UtcNow.ToString("O") + Environment.NewLine + args.Exception.GetType().FullName + Environment.NewLine +
                    new StackTrace(args.Exception, false));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        };
    }
}
