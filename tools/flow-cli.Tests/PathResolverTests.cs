using System.IO;
using FlowCLI.Services;
using Xunit;

namespace FlowCLI.Tests;

public class PathResolverTests
{
    [Fact]
    public void GetSharedProjectFlowRoot_UsesProjectFolderName_ForRegularProject()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "flow");

        var sharedRoot = PathResolver.GetSharedProjectFlowRoot(projectRoot);

        Assert.EndsWith(Path.Combine(".flow", "flow"), sharedRoot);
    }

    [Fact]
    public void GetSharedProjectFlowRoot_UsesMainProjectFolderName_ForWorktreeProject()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "flow", ".flow", "worktrees", "F-003");

        var sharedRoot = PathResolver.GetSharedProjectFlowRoot(projectRoot);

        Assert.EndsWith(Path.Combine(".flow", "flow"), sharedRoot);
    }
}