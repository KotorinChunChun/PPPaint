namespace PPPaint.Core.Tests;

[TestClass]
public sealed class QualityGateContractTests
{
    [TestMethod]
    public void CommonBuildConfiguration_ExcludesGeneratedOutputTrees()
    {
        var root = FindProjectRoot();
        var propsPath = Path.Combine(root, "Directory.Build.props");
        var props = File.ReadAllText(propsPath);

        StringAssert.Contains(props, "<DefaultItemExcludes>");
        StringAssert.Contains(props, "**/bin/**");
        StringAssert.Contains(props, "**/obj/**");
        StringAssert.Contains(props, "**/artifacts/**");
    }

    [TestMethod]
    public void QualityGateScript_CoversRequiredChecks()
    {
        var root = FindProjectRoot();
        var scriptPath = Path.Combine(root, "dev", "quality", "Invoke-QualityGate.ps1");
        var script = File.ReadAllText(scriptPath);

        StringAssert.Contains(script, ".tools\\dotnet\\dotnet.exe");
        StringAssert.Contains(script, "'test'");
        StringAssert.Contains(script, "PPPaint.csproj");
        StringAssert.Contains(script, "Platform=x64");
        StringAssert.Contains(script, "capture-index.jsonl");
        StringAssert.Contains(script, "Send-CaptureRequest.ps1");
        StringAssert.Contains(script, "CaptureProcessId");
        StringAssert.Contains(script, "diff");
        StringAssert.Contains(script, "--check");
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PPPaint.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the project root.");
    }
}
