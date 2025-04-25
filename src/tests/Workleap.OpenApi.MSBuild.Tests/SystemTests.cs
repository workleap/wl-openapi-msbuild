#pragma warning disable CA1861
using CliWrap;
using Meziantou.Framework;

namespace Workleap.OpenApi.MSBuild.Tests;

public sealed class SystemTests(ITestOutputHelper testOutputHelper, SystemTestFixture fixture) : IClassFixture<SystemTestFixture>
{
    private const string GenericSysTestDir = "src/tests/WebApi.MsBuild.SystemTest.GenericTest";
    private const string OasDiffErrorSysTestDir = "src/tests/WebApi.MsBuild.SystemTest.OasDiffError";
    private const string SpectralErrorSysTestDir = "src/tests/WebApi.MsBuild.SystemTest.SpectralError";

    [Theory]

    // Testing Generate Contract Mode
    [InlineData(GenericSysTestDir, false, new string[] { "/p:OpenApiDevelopmentMode=GenerateContract" })] // Then Should Successfully Build
    [InlineData(GenericSysTestDir, false, new string[] { "/p:OpenApiDevelopmentMode=CodeFirst" })] // When using legacy name / Then Should Successfully Build
    [InlineData(GenericSysTestDir, false, new string[] { "/p:OpenApiDevelopmentMode=GenerateContract;OpenApiCompareCodeAgainstSpecFile=true" })] // When Comparing Spec and No Diff Error / Then Should Successfully Build
    [InlineData(OasDiffErrorSysTestDir, true, new string[] { "/p:OpenApiDevelopmentMode=GenerateContract;OpenApiCompareCodeAgainstSpecFile=true" })] // When Comparing Spec and Have Diff / Then Should Fail Build

    // Testing Compare Contract Mode
    [InlineData(GenericSysTestDir, false, new string[] { "/p:OpenApiDevelopmentMode=ValidateContract" })] // Given no diff / Then Should Successfully Build
    [InlineData(GenericSysTestDir, false, new string[] { "/p:OpenApiDevelopmentMode=ContractFirst" })] // Given no diff / When using legacy name / Then Should Successfully Build
    [InlineData(OasDiffErrorSysTestDir, true, new string[] { "/p:OpenApiDevelopmentMode=ValidateContract" })] // Given diff / Then Should Fail Build
    [InlineData(OasDiffErrorSysTestDir, false, new string[] { "/p:OpenApiTreatWarningsAsErrors=false" })] // Given diff / When OpenApiTreatWarningsAsErrors=false / Then Should Successfully Bui// ld

    // Testing Spectral Validation
    [InlineData(GenericSysTestDir, false, new string[] { "/p:OpenApiDevelopmentMode=GenerateContract;OpenApiServiceProfile=frontend" })] // Given no spectral violation / When using frontend profile / Then Should Successfully Build
    [InlineData(GenericSysTestDir, true, new string[] { "/p:OpenApiDevelopmentMode=GenerateContract;OpenApiServiceProfile=scrap" })] // Given no spectral violation / When using invalid profile / Then Should Fail Build
    [InlineData(SpectralErrorSysTestDir, true, new string[] { })] // Given spectral violations / When using default ruleset / Then Should Fail Build
    [InlineData(SpectralErrorSysTestDir, false, new string[] { "/p:OpenApiTreatWarningsAsErrors=false" })] // Given spectral violations / When using default ruleset And OpenApiTreatWarningsAsErrors=false / Then Should Successfully Build
    [InlineData(SpectralErrorSysTestDir, false, new string[] { "/p:OpenApiSpectralRulesetUrl=./eject.spectral.yaml" })] // Given workleap spectral violations / When ejecting ruleset / Then Should Successfully Build
    [InlineData(SpectralErrorSysTestDir, true, new string[] { "/p:OpenApiSpectralRulesetUrl=./override.spectral.yaml" })] // Given spectral violations / When overriding ruleset without disabling problematic ruleset / Then Should Fail Build
    [InlineData(SpectralErrorSysTestDir, false, new string[] { "/p:OpenApiSpectralRulesetUrl=./override.fixed.spectral.yaml" })] // Given spectral violations / When overriding ruleset while disabling problematic ruleset / Then Should Successfully Build
    public async Task Run(string projectPath, bool isFailureExpected, string[] extraBuildArgs)
    {
        var fullPathPath = PathHelpers.GetGitRoot() / projectPath;
        await using var projectDir = TemporaryDirectory.Create();
        foreach (var file in Directory.EnumerateFiles(fullPathPath, "*", SearchOption.AllDirectories))
        {
            var destFileName = projectDir.FullPath / Path.GetRelativePath(fullPathPath, file);
            destFileName.CreateParentDirectory();
            File.Copy(file, destFileName, overwrite: false);
        }

        // Ensure
        await projectDir.CreateTextFileAsync("NuGet.config", $"""
                <configuration>
                  <config>
                    <add key="globalPackagesFolder" value="{fixture.PackageDirectory}/packages" />
                  </config>
                  <packageSources>
                    <clear />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                    <add key="TestSource" value="{fixture.PackageDirectory}" />
                  </packageSources>
                  <packageSourceMapping>
                    <packageSource key="nuget.org">
                        <package pattern="*" />
                    </packageSource>
                    <packageSource key="TestSource">
                        <package pattern="Workleap.OpenApi.MSBuild" />
                    </packageSource>
                  </packageSourceMapping>
                </configuration>
                """);

        await Cli.Wrap("dotnet")
            .WithWorkingDirectory(projectDir.FullPath)
            .WithArguments(["add", "package", "Workleap.OpenApi.MSBuild", "--version", "*-*"])
            .WithStandardOutputPipe(PipeTarget.ToDelegate(testOutputHelper.WriteLine))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(testOutputHelper.WriteLine))
            .ExecuteAsync();

        var buildResult = await Cli.Wrap("dotnet")
            .WithWorkingDirectory(projectDir.FullPath)
            .WithArguments(["build", "--configuration", "Release", "/bl", .. extraBuildArgs])
            .WithStandardOutputPipe(PipeTarget.ToDelegate(testOutputHelper.WriteLine))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(testOutputHelper.WriteLine))
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync();

        if (isFailureExpected && buildResult.ExitCode == 0)
        {
            Assert.Fail($"The build for project {projectPath} was expected to fail, but it succeeded.");
        }
        else if (!isFailureExpected && buildResult.ExitCode != 0)
        {
            Assert.Fail($"The build for project {projectPath} was expected to succeed, but it failed.");
        }
    }
}
