using System.IO.Compression;
using System.Text.Json;
using FlowCLI.Models;
using FlowCLI.Services;
using FluentAssertions;

namespace FlowCLI.Tests;

/// <summary>
/// BuildModuleManager 서비스의 단위 테스트.
/// 로컬 파일 시스템을 사용하여 모듈 설치/로드 기능을 검증한다.
/// </summary>
public class BuildModuleManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TestPathResolver _paths;
    private readonly BuildModuleManager _manager;

    public BuildModuleManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // .flow 디렉토리 생성
        Directory.CreateDirectory(Path.Combine(_tempDir, ".flow"));

        _paths = new TestPathResolver(_tempDir);
        _manager = new BuildModuleManager(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    #region IsInstalled Tests

    [Fact]
    public void IsInstalled_NoModule_ReturnsFalse()
    {
        _manager.IsInstalled("unity").Should().BeFalse();
    }

    [Fact]
    public void IsInstalled_WithManifest_ReturnsTrue()
    {
        InstallTestModule("unity");
        _manager.IsInstalled("unity").Should().BeTrue();
    }

    #endregion

    #region GetModulePath Tests

    [Fact]
    public void GetModulePath_ReturnsCorrectPath()
    {
        var path = _manager.GetModulePath("unity");
        path.Should().EndWith(Path.Combine(".flow", "build", "unity"));
    }

    #endregion

    #region LoadManifest Tests

    [Fact]
    public void LoadManifest_NoModule_ReturnsNull()
    {
        _manager.LoadManifest("unity").Should().BeNull();
    }

    [Fact]
    public void LoadManifest_ValidManifest_ReturnsObject()
    {
        InstallTestModule("unity");
        var manifest = _manager.LoadManifest("unity");

        manifest.Should().NotBeNull();
        manifest!.Name.Should().Be("unity");
        manifest.Version.Should().Be("1.0.0");
        manifest.Scripts.Should().NotBeNull();
        manifest.Scripts!.Build.Should().Be("scripts/build.ps1");
        manifest.Scripts.Test.Should().Be("scripts/test.ps1");
        manifest.Detect.Should().NotBeNull();
        manifest.Detect!.Files.Should().Contain("Assets/");
    }

    [Fact]
    public void LoadManifest_CorruptedJson_ReturnsNull()
    {
        var modulePath = _paths.GetBuildModulePath("broken");
        Directory.CreateDirectory(modulePath);
        File.WriteAllText(Path.Combine(modulePath, "manifest.json"), "not valid json {{{");

        _manager.LoadManifest("broken").Should().BeNull();
    }

    #endregion

    #region GetScriptPath Tests

    [Fact]
    public void GetScriptPath_ExistingAction_ReturnsFullPath()
    {
        InstallTestModule("unity");
        var path = _manager.GetScriptPath("unity", "build");

        path.Should().NotBeNull();
        path.Should().Contain("scripts");
        path.Should().EndWith("build.ps1");
    }

    [Fact]
    public void GetScriptPath_UnknownAction_ReturnsNull()
    {
        InstallTestModule("unity");
        _manager.GetScriptPath("unity", "deploy").Should().BeNull();
    }

    [Fact]
    public void GetScriptPath_NoModule_ReturnsNull()
    {
        _manager.GetScriptPath("python", "build").Should().BeNull();
    }

    #endregion

    #region InstallFromZip Tests

    [Fact]
    public void InstallFromZip_ValidZip_InstallsSuccessfully()
    {
        var zipPath = CreateTestZip("node");

        var error = _manager.InstallFromZip("node", zipPath);

        error.Should().BeNull();
        _manager.IsInstalled("node").Should().BeTrue();
        var manifest = _manager.LoadManifest("node");
        manifest.Should().NotBeNull();
        manifest!.Name.Should().Be("node");
    }

    [Fact]
    public void InstallFromZip_NonexistentZip_ReturnsError()
    {
        var error = _manager.InstallFromZip("python", "/nonexistent.zip");
        error.Should().Contain("ZIP 파일을 찾을 수 없습니다");
    }

    [Fact]
    public void InstallFromZip_ZipWithoutManifest_ReturnsError()
    {
        // 빈 ZIP 파일 생성
        var zipPath = Path.Combine(_tempDir, "empty.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntry("readme.txt");
        }

        var error = _manager.InstallFromZip("empty", zipPath);
        error.Should().Contain("manifest.json");
    }

    [Fact]
    public void InstallFromZip_OverwritesExisting()
    {
        InstallTestModule("unity"); // v1.0.0
        _manager.LoadManifest("unity")!.Version.Should().Be("1.0.0");

        // v2.0.0 ZIP 생성 후 설치
        var zipPath = CreateTestZip("unity", "2.0.0");
        _manager.InstallFromZip("unity", zipPath);

        _manager.LoadManifest("unity")!.Version.Should().Be("2.0.0");
    }

    #endregion

    #region Helpers

    private void InstallTestModule(string platform)
    {
        var modulePath = _paths.GetBuildModulePath(platform);
        var scriptsPath = Path.Combine(modulePath, "scripts");
        Directory.CreateDirectory(scriptsPath);

        var manifest = new BuildManifest
        {
            Name = platform,
            Version = "1.0.0",
            Description = $"{platform} build module for flow",
            Detect = new BuildDetectRule
            {
                Files = platform == "unity"
                    ? new List<string> { "Assets/", "ProjectSettings/ProjectVersion.txt" }
                    : new List<string> { "package.json" },
                Description = $"{platform} 프로젝트 감지"
            },
            Scripts = new BuildScripts
            {
                Lint = "scripts/lint.ps1",
                Build = "scripts/build.ps1",
                Test = "scripts/test.ps1",
                Run = "scripts/run.ps1"
            }
        };

        File.WriteAllText(
            Path.Combine(modulePath, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        // 빈 스크립트 파일 생성
        foreach (var script in new[] { "lint.ps1", "build.ps1", "test.ps1", "run.ps1" })
        {
            File.WriteAllText(Path.Combine(scriptsPath, script), "# placeholder");
        }
    }

    private string CreateTestZip(string platform, string version = "1.0.0")
    {
        var tempModuleDir = Path.Combine(_tempDir, $"zip-source-{Guid.NewGuid():N}");
        var scriptsDir = Path.Combine(tempModuleDir, "scripts");
        Directory.CreateDirectory(scriptsDir);

        var manifest = new BuildManifest
        {
            Name = platform,
            Version = version,
            Description = $"{platform} build module",
            Scripts = new BuildScripts { Build = "scripts/build.ps1" }
        };

        File.WriteAllText(
            Path.Combine(tempModuleDir, "manifest.json"),
            JsonSerializer.Serialize(manifest));

        File.WriteAllText(Path.Combine(scriptsDir, "build.ps1"), "# build script");

        var zipPath = Path.Combine(_tempDir, $"build-module-{platform}-{Guid.NewGuid():N}.zip");
        ZipFile.CreateFromDirectory(tempModuleDir, zipPath);
        Directory.Delete(tempModuleDir, true);

        return zipPath;
    }

    /// <summary>
    /// 테스트용 PathResolver. 임시 디렉토리를 프로젝트 루트로 사용.
    /// </summary>
    private class TestPathResolver : PathResolver
    {
        public TestPathResolver(string tempRoot) : base(tempRoot) { }
    }

    #endregion
}
