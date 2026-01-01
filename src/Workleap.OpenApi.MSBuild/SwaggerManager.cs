using System.Runtime.InteropServices;
using Microsoft.Build.Framework;

namespace Workleap.OpenApi.MSBuild;

internal sealed class SwaggerManager : ISwaggerManager
{
    private const string SwaggerVersion = "9.0.6";
    private const string DoNotEditComment = "# DO NOT EDIT. This is a generated file\n";
    private const int MaxRetryCount = 3;
    private readonly IProcessWrapper _processWrapper;
    private readonly ILoggerWrapper _loggerWrapper;
    private readonly string _openApiWebApiAssemblyPath;
    private readonly string _swaggerDirectory;
    private readonly string _openApiToolsDirectoryPath;
    private readonly string _swaggerExecutablePath;

    public SwaggerManager(ILoggerWrapper loggerWrapper, IProcessWrapper processWrapper, string openApiToolsDirectoryPath, string openApiWebApiAssemblyPath)
    {
        this._processWrapper = processWrapper;
        this._loggerWrapper = loggerWrapper;
        this._openApiWebApiAssemblyPath = openApiWebApiAssemblyPath;
        this._openApiToolsDirectoryPath = openApiToolsDirectoryPath;
        this._swaggerDirectory = Path.Combine(openApiToolsDirectoryPath, "swagger", SwaggerVersion);
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

        this._loggerWrapper.LogMessage($"🔧 Installing Swashbuckle.AspNetCore.Cli {SwaggerVersion}...");

        for (var retryCount = 0; retryCount < MaxRetryCount; retryCount++)
        {
            var result = await this._processWrapper.RunProcessAsync(
                "dotnet",
                ["tool", "update", "Swashbuckle.AspNetCore.Cli", "--ignore-failed-sources", "--tool-path", this._swaggerDirectory, "--configfile", Path.Combine(this._openApiToolsDirectoryPath, "nuget.config"), "--version", SwaggerVersion],
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

        this._loggerWrapper.LogMessage($"✅ Swashbuckle.AspNetCore.Cli {SwaggerVersion} installed successfully.");
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
}