using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;

namespace Workleap.OpenApi.MSBuild;

internal sealed class SwaggerManager : ISwaggerManager
{
    private const string DefaultSwaggerVersion = "9.0.4";
    private const string DoNotEditComment = "# DO NOT EDIT. This is a generated file\n";
    private const int MaxRetryCount = 3;
    private readonly IProcessWrapper _processWrapper;
    private readonly ILoggerWrapper _loggerWrapper;
    private readonly string _openApiWebApiAssemblyPath;
    private readonly string _swaggerDirectory;
    private readonly string _openApiToolsDirectoryPath;
    private readonly string _swaggerExecutablePath;
    private readonly string _swaggerVersion;

    public SwaggerManager(ILoggerWrapper loggerWrapper, IProcessWrapper processWrapper, string openApiToolsDirectoryPath, string openApiWebApiAssemblyPath)
    {
        this._processWrapper = processWrapper;
        this._loggerWrapper = loggerWrapper;
        this._openApiWebApiAssemblyPath = openApiWebApiAssemblyPath;
        this._openApiToolsDirectoryPath = openApiToolsDirectoryPath;

        // Detect Swashbuckle version from the application's assemblies
        var assemblyDirectory = Path.GetDirectoryName(openApiWebApiAssemblyPath);
        if (string.IsNullOrEmpty(assemblyDirectory))
        {
            this._loggerWrapper.LogMessage("Could not determine assembly directory from path. Using default Swashbuckle CLI version.", MessageImportance.Low);
            this._swaggerVersion = DefaultSwaggerVersion;
        }
        else
        {
            this._swaggerVersion = this.DetectSwashbuckleVersion(assemblyDirectory) ?? DefaultSwaggerVersion;
        }

        this._swaggerDirectory = Path.Combine(openApiToolsDirectoryPath, "swagger", this._swaggerVersion);
        this._swaggerExecutablePath = Path.Combine(this._swaggerDirectory, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "swagger.exe" : "swagger");
    }

    public async Task<IEnumerable<string>> RunSwaggerAsync(string[] openApiSwaggerDocumentNames, CancellationToken cancellationToken)
    {
        var taskList = new List<Task<string>>();

        foreach (var documentName in openApiSwaggerDocumentNames)
        {
            var outputOpenApiSpecName = $"openapi-{documentName.ToLowerInvariant()}.yaml";

            var outputOpenApiSpecPath = Path.Combine(this._openApiToolsDirectoryPath, outputOpenApiSpecName);

            taskList.Add(this.GenerateOpenApiSpecAsync(this._swaggerExecutablePath, outputOpenApiSpecPath, documentName, cancellationToken));
        }

        return await Task.WhenAll(taskList);
    }

    public async Task InstallSwaggerCliAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(this._swaggerExecutablePath))
        {
            return;
        }

        this._loggerWrapper.LogMessage($"🔧 Installing Swashbuckle.AspNetCore.Cli {this._swaggerVersion}...");

        for (var retryCount = 0; retryCount < MaxRetryCount; retryCount++)
        {
            var result = await this._processWrapper.RunProcessAsync(
                "dotnet",
                ["tool", "update", "Swashbuckle.AspNetCore.Cli", "--ignore-failed-sources", "--tool-path", this._swaggerDirectory, "--configfile", Path.Combine(this._openApiToolsDirectoryPath, "nuget.config"), "--version", this._swaggerVersion],
                cancellationToken);

            var isLastRetry = retryCount == MaxRetryCount - 1;
            if (result.ExitCode != 0)
            {
                if (isLastRetry)
                {
                    throw new OpenApiTaskFailedException("Swashbuckle CLI could not be installed.");
                }

                this._loggerWrapper.LogMessage(result.StandardOutput, MessageImportance.High);
                this._loggerWrapper.LogWarning(result.StandardError);
                this._loggerWrapper.LogWarning("Swashbuckle download failed. Retrying once more...");

                continue;
            }

            break;
        }

        this._loggerWrapper.LogMessage($"✅ Swashbuckle.AspNetCore.Cli {this._swaggerVersion} installed successfully.");
    }

    public async Task<string> GenerateOpenApiSpecAsync(string swaggerExePath, string outputOpenApiSpecPath, string documentName, CancellationToken cancellationToken)
    {
        var envVars = new Dictionary<string, string?>() { { "DOTNET_ROLL_FORWARD", "LatestMajor" } };
        var swaggerCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        swaggerCancellationToken.CancelAfter(TimeSpan.FromMinutes(1));

        for (var retryCount = 0; retryCount < MaxRetryCount; retryCount++)
        {
            var result = await this._processWrapper.RunProcessAsync(swaggerExePath, ["tofile", "--output", outputOpenApiSpecPath, "--yaml", this._openApiWebApiAssemblyPath, documentName], cancellationToken: swaggerCancellationToken.Token, envVars: envVars);

            var isLastRetry = retryCount == MaxRetryCount - 1;
            if (result.ExitCode != 0)
            {
                if (isLastRetry)
                {
                    throw new OpenApiTaskFailedException($"OpenApi file for {outputOpenApiSpecPath} could not be generated.");
                }

                this._loggerWrapper.LogMessage(result.StandardOutput, MessageImportance.High);
                this._loggerWrapper.LogWarning(result.StandardError);
                this._loggerWrapper.LogWarning($"OpenAPI spec generation failed for {outputOpenApiSpecPath}. Retrying again...");
                continue;
            }

            break;
        }

        this.PrependComment(outputOpenApiSpecPath, DoNotEditComment);

        return outputOpenApiSpecPath;
    }

    private void PrependComment(string outputOpenApiSpecPath, string comment)
    {
        try
        {
            // Add the comment at the top of the generated YAML file
            var yamlContent = File.ReadAllText(outputOpenApiSpecPath);
            File.WriteAllText(outputOpenApiSpecPath, comment + yamlContent);
        }
        catch (Exception)
        {
            this._loggerWrapper.LogWarning($"Failed to add comment to generated spec for {outputOpenApiSpecPath}.");
        }
    }

    private string? DetectSwashbuckleVersion(string? assemblyDirectory)
    {
        if (string.IsNullOrEmpty(assemblyDirectory) || !Directory.Exists(assemblyDirectory))
        {
            this._loggerWrapper.LogMessage("Could not detect Swashbuckle version: assembly directory not found. Using default version.", MessageImportance.Low);
            return null;
        }

        try
        {
            // Look for Swashbuckle.AspNetCore.SwaggerGen.dll in the assembly directory
            // (Swashbuckle.AspNetCore is a metapackage, so we check for one of its actual assemblies)
            var swashbuckleAssemblyPath = Path.Combine(assemblyDirectory, "Swashbuckle.AspNetCore.SwaggerGen.dll");

            if (!File.Exists(swashbuckleAssemblyPath))
            {
                this._loggerWrapper.LogMessage("Could not detect Swashbuckle version: Swashbuckle.AspNetCore.SwaggerGen.dll not found in assembly directory. Using default version.", MessageImportance.Low);
                return null;
            }

            // Get the assembly version using FileVersionInfo (doesn't load the assembly)
            var versionInfo = FileVersionInfo.GetVersionInfo(swashbuckleAssemblyPath);
            var productVersion = versionInfo.ProductVersion;

            if (!string.IsNullOrEmpty(productVersion))
            {
                // Product version might include metadata like "9.0.6+commit", extract just the version number
                var versionParts = productVersion.Split('+', '-');
                var versionString = versionParts[0];

                // Validate that the extracted version matches semantic versioning pattern (e.g., 9.0.4, 10.0.1)
                // Pattern: 1-4 digits, dot, 1-4 digits, dot, 1-4 digits, optionally followed by more version parts
                if (!string.IsNullOrWhiteSpace(versionString) &&
                    Regex.IsMatch(versionString, @"^\d{1,4}\.\d{1,4}(\.\d{1,4})?(\.\d{1,4})?$"))
                {
                    this._loggerWrapper.LogMessage($"✓ Detected Swashbuckle.AspNetCore version: {versionString}", MessageImportance.Normal);
                    return versionString;
                }

                this._loggerWrapper.LogMessage($"Detected version '{versionString}' does not match expected format. Using default version.", MessageImportance.Low);
                return null;
            }

            this._loggerWrapper.LogMessage("Could not detect Swashbuckle version from assembly metadata. Using default version.", MessageImportance.Low);
            return null;
        }
        catch (Exception ex)
        {
            this._loggerWrapper.LogMessage($"Error detecting Swashbuckle version: {ex.Message}. Using default version.", MessageImportance.Low);
            return null;
        }
    }
}