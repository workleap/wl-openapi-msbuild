using System.Runtime.InteropServices;
using Microsoft.Build.Framework;

namespace Workleap.OpenApi.MSBuild;

internal sealed class OasdiffManager : IOasdiffManager
{
    // If the line below changes, make sure to update the corresponding regex on the renovate.json file
    // Do not upgrade to v2.x as it is an older version with breaking changes
    private const string OasdiffVersion = "1.11.7";
    private const string OasdiffDownloadUrlFormat = "https://github.com/Tufin/oasdiff/releases/download/v{0}/{1}";

    private readonly ILoggerWrapper _loggerWrapper;
    private readonly IHttpClientWrapper _httpClientWrapper;
    private readonly IProcessWrapper _processWrapper;
    private readonly string _oasdiffDirectory;

    public OasdiffManager(ILoggerWrapper loggerWrapper, IProcessWrapper processWrapper, string openApiToolsDirectoryPath, IHttpClientWrapper httpClientWrapper)
    {
        this._loggerWrapper = loggerWrapper;
        this._httpClientWrapper = httpClientWrapper;
        this._processWrapper = processWrapper;
        this._oasdiffDirectory = Path.Combine(openApiToolsDirectoryPath, "oasdiff", OasdiffVersion);
    }

    public async Task InstallOasdiffAsync(CancellationToken cancellationToken)
    {
        this._loggerWrapper.LogMessage($"🔧 Installing OasDiff {OasdiffVersion}...");

        Directory.CreateDirectory(this._oasdiffDirectory);

        var oasdiffFileName = GetOasdiffFileName();
        var url = string.Format(OasdiffDownloadUrlFormat, OasdiffVersion, oasdiffFileName);

        await this._httpClientWrapper.DownloadFileToDestinationAsync(url, Path.Combine(this._oasdiffDirectory, oasdiffFileName), cancellationToken);
        await this.DecompressDownloadedFileAsync(oasdiffFileName, cancellationToken);

        this._loggerWrapper.LogMessage($"✅ OasDiff {OasdiffVersion} installed successfully.");
    }

    public async Task RunOasdiffAsync(IReadOnlyCollection<string> openApiSpecFiles, IReadOnlyCollection<string> generatedOpenApiSpecFiles, CancellationToken cancellationToken)
    {
        var generatedOpenApiSpecFilesList = generatedOpenApiSpecFiles.ToList();
        var oasdiffExecutePath = Path.Combine(this._oasdiffDirectory, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "oasdiff.exe" : "oasdiff");
        var isGitHubActions = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));

        var filesPath = generatedOpenApiSpecFiles.ToDictionary(Path.GetFileName, x => x);
        var hasAnyChanges = false;

        foreach (var baseSpecFile in openApiSpecFiles)
        {
            var fileName = Path.GetFileName(baseSpecFile);

            this._loggerWrapper.LogMessage($"🔍 OasDiff: Comparing specifications for {fileName}...", MessageImportance.High);

            var isFileFound = filesPath.TryGetValue(fileName, out var generatedSpecFilePath);
            if (!isFileFound || string.IsNullOrEmpty(generatedSpecFilePath))
            {
                this._loggerWrapper.LogWarning($"⚠️ Could not find a generated spec file for {fileName}.");
                continue;
            }

            if (isGitHubActions)
            {
                Console.WriteLine($"::group::📋 OasDiff Comparison Details for {fileName}");
            }

            this._loggerWrapper.LogMessage($"📄 Specification file: {baseSpecFile}", MessageImportance.High);
            this._loggerWrapper.LogMessage($"🔧 Generated from code: {generatedSpecFilePath}", MessageImportance.High);

            var result = await this._processWrapper.RunProcessAsync(oasdiffExecutePath, new[] { "diff", baseSpecFile, generatedSpecFilePath, "--exclude-elements", "description,examples,title,summary", "--fail-on-diff", "--format", "yaml" }, cancellationToken);

            if (!string.IsNullOrEmpty(result.StandardError))
            {
                this._loggerWrapper.LogWarning($"❌ OasDiff error: {result.StandardError}");
                if (isGitHubActions)
                {
                    Console.WriteLine("::endgroup::");
                }

                continue;
            }

            var isChangesDetected = result.ExitCode != 0;
            if (isChangesDetected)
            {
                hasAnyChanges = true;
                this._loggerWrapper.LogWarning($"⚠️ Breaking changes detected in {fileName}. Your web API does not respect the provided OpenAPI specification.");
                this._loggerWrapper.LogMessage(result.StandardOutput, MessageImportance.High);
            }
            else
            {
                this._loggerWrapper.LogMessage($"✅ No breaking changes detected in {fileName}.", MessageImportance.High);
            }

            if (isGitHubActions)
            {
                Console.WriteLine("::endgroup::");
            }
        }

        if (!hasAnyChanges && openApiSpecFiles.Any())
        {
            this._loggerWrapper.LogMessage("🎉 All OpenAPI specifications are in sync with your code!", MessageImportance.High);
        }
    }

    private async Task DecompressDownloadedFileAsync(string oasdiffFileName, CancellationToken cancellationToken)
    {
        var alreadyDecompressed = Path.Combine(this._oasdiffDirectory, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "oasdiff.exe" : "oasdiff");
        var pathToCompressedFile = Path.Combine(this._oasdiffDirectory, oasdiffFileName);
        if (File.Exists(alreadyDecompressed))
        {
            return;
        }

        var result = await this._processWrapper.RunProcessAsync("tar", new[] { "-xzf", $"{pathToCompressedFile}", "-C", $"{this._oasdiffDirectory}" }, cancellationToken);

        if (result.ExitCode != 0)
        {
            this._loggerWrapper.LogMessage(result.StandardOutput, MessageImportance.High);
            this._loggerWrapper.LogWarning(result.StandardError);
            throw new OpenApiTaskFailedException("Failed to decompress oasdiff.");
        }
    }

    private static string GetOasdiffFileName()
    {
        var osType = RuntimeInformationHelper.GetOperatingSystem();
        var architecture = RuntimeInformationHelper.GetArchitecture("amd");

        var fileName = $"oasdiff_{OasdiffVersion}_{osType}_{architecture}.tar.gz";

        if (osType == "macos")
        {
            fileName = $"oasdiff_{OasdiffVersion}_darwin_all.tar.gz";
        }

        return fileName;
    }
}