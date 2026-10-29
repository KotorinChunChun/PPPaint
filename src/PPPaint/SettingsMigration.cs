namespace PPPaint;

internal static class SettingsMigration
{
    internal static void TryCopyLegacy(string destination, string? legacyDirectory = null)
    {
        var pending = destination + ".legacy-migration-pending";
        if (Directory.Exists(destination) && !File.Exists(pending)) return;
        var legacy = legacyDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChunPaint");
        if (!Directory.Exists(legacy)) return;
        var staging = destination + ".migration-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(pending, "設定と復元データのコピーが未完了です。");
            Directory.CreateDirectory(staging);
            var preferences = Path.Combine(legacy, "preferences.json");
            if (File.Exists(preferences)) File.Copy(preferences, Path.Combine(staging, "preferences.json"));
            var recovery = Path.Combine(legacy, "Recovery");
            if (Directory.Exists(recovery)) CopyDirectory(recovery, Path.Combine(staging, "Recovery"));
            if (!Directory.Exists(destination)) Directory.Move(staging, destination);
            else MergeDirectory(staging, destination);
            File.Delete(pending);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // 旧設定を変更せず、未完了のコピーは次回起動で再試行する。
        }
        finally
        {
            if (Directory.Exists(staging)) try { Directory.Delete(staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void MergeDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var destination = Path.Combine(target, Path.GetFileName(file));
            if (!File.Exists(destination)) File.Move(file, destination);
        }
        foreach (var directory in Directory.EnumerateDirectories(source)) MergeDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    private static void CopyDirectory(string source, string target)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("移行元は通常のフォルダーである必要があります。");
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("移行元は通常のファイルである必要があります。");
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
        foreach (var directory in Directory.EnumerateDirectories(source)) CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
}
