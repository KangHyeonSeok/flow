using System.Diagnostics;
using FlowCLI.Services.Runner;
using FluentAssertions;

namespace FlowCLI.Tests;

public class GitWorktreeServiceTests : IDisposable
{
    private readonly string _tempDir;

    public GitWorktreeServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-worktree-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            DeleteDirectoryForce(_tempDir);
        }
    }

    [Fact]
    public async Task CreateWorktreeAsync_WhenStalePrunableEntryExists_PrunesAndRecreatesWorktree()
    {
        var repoDir = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoDir);
        Directory.CreateDirectory(Path.Combine(repoDir, ".flow"));

        await RunGitAsync("init --initial-branch=main", repoDir);
        await RunGitAsync("config user.email \"test@test.com\"", repoDir);
        await RunGitAsync("config user.name \"Test\"", repoDir);

        File.WriteAllText(Path.Combine(repoDir, "README.md"), "hello");
        await RunGitAsync("add .", repoDir);
        await RunGitAsync("commit -m \"init\"", repoDir);

        var flowRoot = Path.Combine(repoDir, ".flow");
        var staleWorktreePath = Path.Combine(flowRoot, "worktrees", "F-002");
        await RunGitAsync($"worktree add -b runner/F-002 \"{staleWorktreePath}\"", repoDir);

        Directory.Exists(staleWorktreePath).Should().BeTrue();
        DeleteDirectoryForce(staleWorktreePath);

        var log = new RunnerLogService(flowRoot, "logs", "test-runner", echoToConsole: false);
        var service = new GitWorktreeService(repoDir, flowRoot, "worktrees", "main", log);

        var (success, worktreePath, branchName) = await service.CreateWorktreeAsync("F-002");

        success.Should().BeTrue();
        worktreePath.Should().Be(staleWorktreePath);
        branchName.Should().Be("runner/F-002");
        Directory.Exists(staleWorktreePath).Should().BeTrue();

        var branchList = await RunGitWithResultAsync("branch --list \"runner/F-002\"", repoDir);
        branchList.ExitCode.Should().Be(0);
        branchList.StdOut.Should().Contain("runner/F-002");

        var worktreeList = await RunGitWithResultAsync("worktree list", repoDir);
        worktreeList.ExitCode.Should().Be(0);
        worktreeList.StdOut.Should().Contain(".flow/worktrees/F-002");
        worktreeList.StdOut.Should().NotContain("prunable");
    }

    private static void DeleteDirectoryForce(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }

    private static async Task RunGitAsync(string arguments, string workingDir)
    {
        var result = await RunGitWithResultAsync(arguments, workingDir);
        result.ExitCode.Should().Be(0, result.StdErr);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunGitWithResultAsync(string arguments, string workingDir)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdout.Trim(), stderr.Trim());
    }
}