using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;

namespace Workleap.OpenApi.MSBuild.Spectral;

internal sealed class SpectralRunner
{
    // If the line below changes, make sure to update the corresponding regex on the renovate.json file
    private const string SpectralVersion = "v6.15.0";

    // Matches logs with the format of: 0 problems (0 errors, 0 warnings, 0 infos, 0 hints)
    private static readonly Regex SpectralLogWarningPattern = new(@"[0-9]+ problems? \((?<errors>[0-9]+) errors?, (?<warnings>[0-9]+) warnings?, [0-9]+ infos?, [0-9]+ hints?\)");

    private readonly ILoggerWrapper _loggerWrapper;
    private readonly IProcessWrapper _processWrapper;
    private readonly DiffCalculator _diffCalculator;
    private readonly string _spectralDirectory;
    private readonly string _openApiReportsDirectoryPath;

    public SpectralRunner(
        ILoggerWrapper loggerWrapper,
        IProcessWrapper processWrapper,
        DiffCalculator diffCalculator,
        string openApiToolsDirectoryPath,
        string openApiReportsDirectoryPath)
    {
        this._loggerWrapper = loggerWrapper;
        this._openApiReportsDirectoryPath = openApiReportsDirectoryPath;
        this._spectralDirectory = Path.Combine(openApiToolsDirectoryPath, "spectral", SpectralVersion);
        this._processWrapper = processWrapper;
        this._diffCalculator = diffCalculator;
    }

    public async Task RunSpectralAsync(IReadOnlyCollection<string> openApiDocumentPaths, string spectralExecutablePath, string spectralRulesetPath, CancellationToken cancellationToken)
    {
        var isGitHubActions = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        if (isGitHubActions)
        {
            Console.WriteLine("::group::🔍 Spectral: Validating OpenAPI Documents");
        }
        else
        {
            this._loggerWrapper.LogMessage("\n🔍 Spectral: Validating OpenAPI Documents", MessageImportance.High);
        }

        var shouldRunSpectral = await this.ShouldRunSpectral(spectralRulesetPath, openApiDocumentPaths);
        if (!shouldRunSpectral)
        {
            this._loggerWrapper.LogMessage("⏭️ Spectral validation skipped (no changes detected)", MessageImportance.High);
            foreach (var documentPath in openApiDocumentPaths)
            {
                this._loggerWrapper.LogMessage("📄 Previous report: {0}", MessageImportance.High, messageArgs: this.GetReportPath(documentPath));
                this.DisplayPreviousSpectralReport(documentPath);
            }

            if (isGitHubActions)
            {
                Console.WriteLine("::endgroup::");
            }

            return;
        }

        var spectralExecutePath = Path.Combine(this._spectralDirectory, spectralExecutablePath);
        foreach (var documentPath in openApiDocumentPaths)
        {
            var documentName = Path.GetFileNameWithoutExtension(documentPath);
            var spectralReportPath = this.GetReportPath(documentPath);

            if (isGitHubActions)
            {
                Console.WriteLine($"::group::📋 Validating {documentName}");
            }
            else
            {
                this._loggerWrapper.LogMessage("\n📋 Validating {0}", MessageImportance.High, documentName);
            }

            this._loggerWrapper.LogMessage("  📂 Document: {0}", MessageImportance.High, documentPath);
            this._loggerWrapper.LogMessage("  📏 Ruleset: {0}", MessageImportance.High, spectralRulesetPath);

            if (File.Exists(spectralReportPath))
            {
                File.Delete(spectralReportPath);
            }

            await this.GenerateSpectralReport(spectralExecutePath, documentPath, spectralRulesetPath, spectralReportPath, cancellationToken);
            CiReportRenderer.AttachReportToBuild(spectralReportPath);

            if (isGitHubActions)
            {
                Console.WriteLine("::endgroup::");
            }
        }

        this._diffCalculator.SaveCurrentExecutionChecksum(spectralRulesetPath, openApiDocumentPaths);

        if (isGitHubActions)
        {
            Console.WriteLine("::endgroup::");
        }
    }

    // Display previous spectral report and log warning if there are any previous spectral errors or warnings
    private void DisplayPreviousSpectralReport(string documentPath)
    {
        var lines = File.ReadLines(this.GetReportPath(documentPath));
        foreach (var line in lines)
        {
            var match = SpectralLogWarningPattern.Match(line);
            if (match.Success)
            {
                var errors = int.Parse(match.Groups["errors"].Value, CultureInfo.InvariantCulture);
                var warnings = int.Parse(match.Groups["warnings"].Value, CultureInfo.InvariantCulture);
                if (errors > 0 || warnings > 0)
                {
                    this._loggerWrapper.LogWarning("Spectral errors from previous run: {0}", line);
                }
            }
            else
            {
                this._loggerWrapper.LogMessage(line, MessageImportance.High);
            }
        }
    }

    private async Task<bool> ShouldRunSpectral(string spectralRulesetPath, IReadOnlyCollection<string> openApiDocumentPaths)
    {
        if (this._diffCalculator.HasRulesetChangedSinceLastExecution(spectralRulesetPath))
        {
            return true;
        }

        if (this._diffCalculator.HasOpenApiDocumentChangedSinceLastExecution(openApiDocumentPaths))
        {
            return true;
        }

        // If we can't find a report for any of the documents, then run spectral
        foreach (var documentPath in openApiDocumentPaths)
        {
            var spectralReportPath = this.GetReportPath(documentPath);

            if (!File.Exists(spectralReportPath))
            {
                return true;
            }
        }

        return false;
    }

    private string GetReportPath(string documentPath)
    {
        var documentName = Path.GetFileNameWithoutExtension(documentPath);
        var outputSpectralReportName = $"spectral-{documentName}.txt";
        return Path.Combine(this._openApiReportsDirectoryPath, outputSpectralReportName);
    }

    private async Task GenerateSpectralReport(string spectralExecutePath, string swaggerDocumentPath, string rulesetPath, string spectralReportPath, CancellationToken cancellationToken)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await this.AssignExecutePermission(spectralExecutePath, cancellationToken);
        }

        this._loggerWrapper.LogMessage("  🔄 Running Spectral validation...", MessageImportance.Normal);
        var result = await this._processWrapper.RunProcessAsync(spectralExecutePath, new[] { "lint", swaggerDocumentPath, "--ruleset", rulesetPath, "--format", "pretty", "--format", "stylish", "--output.stylish", spectralReportPath, "--fail-severity=warn", "--verbose" }, cancellationToken);

        // Only show verbose output if there are errors
        if (result.ExitCode != 0 && !string.IsNullOrEmpty(result.StandardOutput))
        {
            this._loggerWrapper.LogMessage(result.StandardOutput, MessageImportance.High);
        }

        if (!string.IsNullOrEmpty(result.StandardError))
        {
            this._loggerWrapper.LogWarning(result.StandardError);
        }

        if (!File.Exists(spectralReportPath))
        {
            throw new OpenApiTaskFailedException($"Spectral report for {swaggerDocumentPath} could not be created. Please check the CONSOLE output above for more details.");
        }

        if (result.ExitCode != 0)
        {
            this._loggerWrapper.LogWarning($"⚠️ Spectral detected violations. Report: {spectralReportPath}");
        }
        else
        {
            this._loggerWrapper.LogMessage($"  ✅ Validation passed. Report: {spectralReportPath}", MessageImportance.Normal);
        }
    }

    private async Task AssignExecutePermission(string spectralExecutePath, CancellationToken cancellationToken)
    {
        var result = await this._processWrapper.RunProcessAsync("chmod", new[] { "+x", spectralExecutePath }, cancellationToken);
        if (result.ExitCode != 0)
        {
            this._loggerWrapper.LogMessage(result.StandardOutput, MessageImportance.High);
            this._loggerWrapper.LogWarning(result.StandardError);
            throw new OpenApiTaskFailedException($"Failed to provide execute permission to {spectralExecutePath}");
        }
    }
}