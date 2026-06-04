using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace SoloBB.SmokeTests;

public sealed class SmokeHarnessTests
{
    private readonly ITestOutputHelper _output;

    public SmokeHarnessTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ExecutableSmokeHarnessPasses()
    {
        var root = FindRepositoryRoot();
        var harnessProject = Path.Combine(root, "tests", "SoloBB.Tests", "SoloBB.Tests.csproj");
        var startInfo = new ProcessStartInfo("dotnet", $"run --project \"{harnessProject}\"")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start smoke harness process.");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        WriteOutput(stdout);
        WriteOutput(stderr);

        Assert.True(process.ExitCode == 0, $"Smoke harness failed with exit code {process.ExitCode}.");
    }

    private void WriteOutput(string text)
    {
        foreach (var line in text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            _output.WriteLine(line);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "project.godot")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
