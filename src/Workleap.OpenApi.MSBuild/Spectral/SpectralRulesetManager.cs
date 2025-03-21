using Microsoft.Build.Framework;
using YamlDotNet.RepresentationModel;

namespace Workleap.OpenApi.MSBuild.Spectral;

internal sealed class SpectralRulesetManager
{
    private const string WorkleapRulesetVersion = "0.10.1";
    private const string WorkleapRulesetDownloadUrlFormat = "https://raw.githubusercontent.com/workleap/wl-api-guidelines/{0}/.spectral.{1}.yaml";

    private readonly ILoggerWrapper _loggerWrapper;
    private readonly IHttpClientWrapper _httpClientWrapper;
    private readonly string _spectralProfile;
    private readonly string _spectralRulesetPath;

    public SpectralRulesetManager(
        ILoggerWrapper loggerWrapper,
        IHttpClientWrapper httpClientWrapper,
        string spectralProfile,
        string? customSpectralFilePath)
    {
        this._loggerWrapper = loggerWrapper;
        this._httpClientWrapper = httpClientWrapper;

        this._spectralProfile = spectralProfile;

        this._spectralRulesetPath = customSpectralFilePath ?? GetProfileRulesetUrl(this._spectralProfile);
    }

    private static string GetProfileRulesetUrl(string spectralProfile)
    {
        return string.Format(WorkleapRulesetDownloadUrlFormat, WorkleapRulesetVersion, spectralProfile);
    }

    public async Task<string> GetLocalSpectralRulesetFile(CancellationToken cancellationToken)
    {
        var rulesetPath = this._spectralRulesetPath;

        // For remote ruleset we download the file for optimization and reduce spectral flakiness
        if (IsRemote(rulesetPath))
        {
            this._loggerWrapper.LogMessage("Downloading ruleset.");
            rulesetPath = await this.DownloadFileAsync(rulesetPath, cancellationToken);
        }

        // If they are not extending any rules we extend them with Workleap rules
        if (!IsRulesetHaveExtendsProperty(rulesetPath))
        {
            this._loggerWrapper.LogMessage("Extending ruleset with Workleap rules.");
            rulesetPath = await CopyAndExtendRuleset(rulesetPath, GetProfileRulesetUrl(this._spectralProfile), cancellationToken);
        }

        return rulesetPath;
    }

    private static bool IsRulesetHaveExtendsProperty(string customSpectralFilePath)
    {
        using var reader = new StreamReader(customSpectralFilePath);
        var yamlStream = new YamlStream();
        yamlStream.Load(reader);

        var hasExtendsProperty = yamlStream.Documents.FirstOrDefault()?.RootNode is YamlMappingNode root &&
               root.Children.ContainsKey(new YamlScalarNode("extends"));

        return hasExtendsProperty;
    }

    private static async Task<string> CopyAndExtendRuleset(string initialPath, string extendsUrl, CancellationToken cancellationToken)
    {
        var outputFilePath = Path.ChangeExtension(Path.GetTempFileName(), "yaml");

        using var sourceReader = new StreamReader(initialPath);
        using var destinationWriter = new StreamWriter(outputFilePath);

        await destinationWriter.WriteLineAsync($"extends: [{extendsUrl}]");

        while (await sourceReader.ReadLineAsync() is { } line)
        {
            await destinationWriter.WriteLineAsync(line);
        }

        return outputFilePath;
    }

    private static bool IsRemote(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return !uri.IsFile;
        }

        return false;
    }

    private async Task<string> DownloadFileAsync(string rulesetUrl, CancellationToken cancellationToken)
    {
        try
        {
            this._loggerWrapper.LogMessage("Downloading rule file {0}", MessageImportance.Normal, rulesetUrl);

            var outputFilePath = Path.ChangeExtension(Path.GetTempFileName(), "yaml");
            await this._httpClientWrapper.DownloadFileToDestinationAsync(rulesetUrl, outputFilePath, cancellationToken);

            this._loggerWrapper.LogMessage("Download completed", MessageImportance.Normal);

            return outputFilePath;
        }
        catch (Exception e)
        {
            this._loggerWrapper.LogWarning(e.Message);
            throw new OpenApiTaskFailedException($"Failed to download {rulesetUrl}.");
        }
    }
}