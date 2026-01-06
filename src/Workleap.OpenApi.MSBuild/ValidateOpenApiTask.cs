using Microsoft.Build.Framework;
using Workleap.OpenApi.MSBuild.Spectral;

namespace Workleap.OpenApi.MSBuild;

public sealed class ValidateOpenApiTask : CancelableAsyncTask
{
    private const string GenerateContract = "GenerateContract";
    private const string CodeFirst = "CodeFirst"; // For backward compatibility
    private const string ValidateContract = "ValidateContract";
    private const string ContractFirst = "ContractFirst"; // For backward compatibility
    private const string Backend = "backend";
    private const string Frontend = "frontend";

    /// <summary>
    ///     2 supported modes:
    ///     - GenerateContract: Generate the OpenAPI specification files from the code
    ///     - ValidateContract: Will use the OpenAPI specification files provided
    /// </summary>
    [Required]
    public string OpenApiDevelopmentMode { get; set; } = string.Empty;

    /// <summary>
    ///     2 supported profiles:
    ///     - backend (default): Uses the backend ruleset to validate the API spec
    ///     - frontend: Uses the frontend ruleset to validate the API spec
    /// </summary>
    [Required]
    public string OpenApiServiceProfile { get; set; } = string.Empty;

    /// <summary>When Development mode is ValidateContract, will validate if the specification match the code.</summary>
    [Required]
    public bool OpenApiCompareCodeAgainstSpecFile { get; set; } = false;

    /// <summary>The path of the ASP.NET Core project startup assembly directory.</summary>
    [Required]
    public string StartupAssemblyPath { get; set; } = string.Empty;

    /// <summary>The path of the ASP.NET Core project being built.</summary>
    [Required]
    public string OpenApiWebApiAssemblyPath { get; set; } = string.Empty;

    /// <summary>The base directory path where the OpenAPI tools will be downloaded.</summary>
    [Required]
    public string OpenApiToolsDirectoryPath { get; set; } = string.Empty;

    /// <summary>The URL of the OpenAPI Spectral ruleset to validate against.</summary>
    public string? OpenApiSpectralRulesetUrl { get; set; }

    /// <summary>The names of the Swagger documents to generate OpenAPI specifications for.</summary>
    [Required]
    public string[] OpenApiSwaggerDocumentNames { get; set; } = Array.Empty<string>();

    /// <summary>The paths of the OpenAPI specification files to validate against.</summary>
    [Required]
    public string[] OpenApiSpecificationFiles { get; set; } = Array.Empty<string>();

    /// <summary>If warnings should be logged as errors instead.</summary>
    public bool OpenApiTreatWarningsAsErrors { get; set; }

    /// <summary>The version of Swashbuckle.AspNetCore.Cli to use for generating OpenAPI specs.</summary>
    [Required]
    public string OpenApiSwaggerVersion { get; set; } = string.Empty;

    protected override async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        var loggerWrapper = new LoggerWrapper(this.Log, this.OpenApiTreatWarningsAsErrors);

        loggerWrapper.LogMessage("\n******** Starting {0} ********\n", MessageImportance.Normal, nameof(ValidateOpenApiTask));

        loggerWrapper.LogMessage("{0} = '{1}'", MessageImportance.Normal, nameof(this.OpenApiDevelopmentMode), this.OpenApiDevelopmentMode);
        loggerWrapper.LogMessage("{0} = '{1}'", MessageImportance.Normal, nameof(this.OpenApiServiceProfile), this.OpenApiServiceProfile);
        loggerWrapper.LogMessage("{0} = '{1}'", MessageImportance.Normal, nameof(this.OpenApiCompareCodeAgainstSpecFile), this.OpenApiCompareCodeAgainstSpecFile);
        loggerWrapper.LogMessage("{0} = '{1}'", MessageImportance.Low, nameof(this.OpenApiTreatWarningsAsErrors), this.OpenApiTreatWarningsAsErrors);
        loggerWrapper.LogMessage("{0} = '{1}'", MessageImportance.Low, nameof(this.OpenApiWebApiAssemblyPath), this.OpenApiWebApiAssemblyPath);
        loggerWrapper.LogMessage("{0} = '{1}'", MessageImportance.Low, nameof(this.OpenApiToolsDirectoryPath), this.OpenApiToolsDirectoryPath);
        loggerWrapper.LogMessage("{0} = '{1}'", MessageImportance.Low, nameof(this.OpenApiSpectralRulesetUrl), this.OpenApiSpectralRulesetUrl ?? "No custom ruleset provided");
        loggerWrapper.LogMessage("{0} = '{1}'", MessageImportance.Low, nameof(this.OpenApiSwaggerDocumentNames), string.Join(", ", this.OpenApiSwaggerDocumentNames));
        loggerWrapper.LogMessage("{0} = '{1}'", MessageImportance.Low, nameof(this.OpenApiSpecificationFiles), string.Join(", ", this.OpenApiSpecificationFiles));
        loggerWrapper.LogMessage("{0} = '{1}'", MessageImportance.Normal, nameof(this.OpenApiSwaggerVersion), this.OpenApiSwaggerVersion);

        if (this.OpenApiSpecificationFiles.Length != this.OpenApiSwaggerDocumentNames.Length)
        {
            loggerWrapper.LogWarning("You must provide the same amount of OpenAPI documents file names and swagger document file names.");
            return false;
        }

        if (!this.OpenApiServiceProfile.Equals(Backend, StringComparison.Ordinal) && !this.OpenApiServiceProfile.Equals(Frontend, StringComparison.Ordinal))
        {
            loggerWrapper.LogWarning("Invalid value of '{0}' for {1}. Allowed values are {2} or {3}", this.OpenApiServiceProfile, nameof(this.OpenApiServiceProfile), Backend, Frontend);
            return false;
        }

        var reportsPath = Path.Combine(this.OpenApiToolsDirectoryPath, "reports");
        var processWrapper = new ProcessWrapper(this.StartupAssemblyPath);
        var swaggerManager = new SwaggerManager(loggerWrapper, processWrapper, this.OpenApiToolsDirectoryPath, this.OpenApiWebApiAssemblyPath, this.OpenApiSwaggerVersion);
        var diffCalculator = new DiffCalculator(Path.Combine(this.OpenApiToolsDirectoryPath, "spectral-state"));

        var httpClientWrapper = new HttpClientWrapper();

        var spectralRulesetManager = new SpectralRulesetManager(loggerWrapper, httpClientWrapper, this.OpenApiServiceProfile, this.OpenApiSpectralRulesetUrl);
        var spectralInstaller = new SpectralInstaller(loggerWrapper, this.OpenApiToolsDirectoryPath, httpClientWrapper);

        var spectralManager = new SpectralRunner(loggerWrapper, processWrapper, diffCalculator, this.OpenApiToolsDirectoryPath, reportsPath);
        var oasdiffManager = new OasdiffManager(loggerWrapper, processWrapper, this.OpenApiToolsDirectoryPath, httpClientWrapper);
        var specGeneratorManager = new SpecGeneratorManager(loggerWrapper);

        var generateContractProcess = new GenerateContractProcess(loggerWrapper, spectralInstaller, spectralRulesetManager, spectralManager, swaggerManager, specGeneratorManager, oasdiffManager);
        var validateContractProcess = new ValidateContractProcess(loggerWrapper, spectralInstaller, spectralRulesetManager, spectralManager, swaggerManager, oasdiffManager);

        try
        {
            await this.GeneratePublicNugetSource();
            Directory.CreateDirectory(reportsPath);

            switch (this.OpenApiDevelopmentMode)
            {
                case GenerateContract:
                case CodeFirst: // For backward compatibility
                    loggerWrapper.LogMessage("\nStarting generate contract process...", MessageImportance.Normal);
                    await generateContractProcess.Execute(
                        this.OpenApiSpecificationFiles,
                        this.OpenApiSwaggerDocumentNames,
                        this.OpenApiCompareCodeAgainstSpecFile ? GenerateContractProcess.GenerateContractMode.SpecComparison : GenerateContractProcess.GenerateContractMode.SpecGeneration,
                        cancellationToken);
                    break;

                case ValidateContract:
                case ContractFirst: // For backward compatibility
                    loggerWrapper.LogMessage("\nStarting validate contract process...", MessageImportance.Normal);
                    var isSuccess = await validateContractProcess.Execute(
                        this.OpenApiSpecificationFiles,
                        this.OpenApiToolsDirectoryPath,
                        this.OpenApiSwaggerDocumentNames,
                        this.OpenApiCompareCodeAgainstSpecFile ? ValidateContractProcess.CompareCodeAgainstSpecFile.Enabled : ValidateContractProcess.CompareCodeAgainstSpecFile.Disabled,
                        cancellationToken);

                    if (!isSuccess)
                    {
                        return false;
                    }

                    break;

                default:
                    loggerWrapper.LogError("Invalid value of '{0}' for {1}. Allowed values are '{2}' or '{3}'", this.OpenApiDevelopmentMode, nameof(this.OpenApiDevelopmentMode), ValidateContract, GenerateContract);
                    return false;
            }
        }
        catch (OpenApiTaskFailedException e)
        {
            loggerWrapper.LogWarning("An error occurred while validating the OpenAPI specification: {0}", e.Message);
        }

        return true;
    }

    // To avoid depending on the client feeds, which can can be private, we will exclusively use the public nuget feed.
    private async Task GeneratePublicNugetSource()
    {
        Directory.CreateDirectory(this.OpenApiToolsDirectoryPath);

        if (!File.Exists(Path.Combine(this.OpenApiToolsDirectoryPath, "nuget.config")))
        {
            var path = Path.Combine(this.OpenApiToolsDirectoryPath, "nuget.config");
            File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<configuration>\n  <packageSources>\n    <clear />\n    <add key=\"nuget\" value=\"https://api.nuget.org/v3/index.json\" />\n  </packageSources>\n</configuration>");
        }
    }
}