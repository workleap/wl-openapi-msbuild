namespace Workleap.OpenApi.MSBuild.Spectral;

internal class SpectralInstaller
{
    // If the line below changes, make sure to update the corresponding regex on the renovate.json file
    private const string SpectralVersion = "v6.15.0";
    private const string SpectralDownloadUrlFormat = "https://github.com/stoplightio/spectral/releases/download/{0}/{1}";

    private readonly ILoggerWrapper _loggerWrapper;
    private readonly IHttpClientWrapper _httpClientWrapper;
    private readonly string _spectralDirectory;

    public SpectralInstaller(
        ILoggerWrapper loggerWrapper,
        string openApiToolsDirectoryPath,
        IHttpClientWrapper httpClientWrapper)
    {
        this._loggerWrapper = loggerWrapper;
        this._httpClientWrapper = httpClientWrapper;
        this._spectralDirectory = Path.Combine(openApiToolsDirectoryPath, "spectral", SpectralVersion);
    }

    /// <summary>
    /// Install spectral tool
    /// </summary>
    /// <returns>Return executable path</returns>
    public async Task<string> InstallSpectralAsync(CancellationToken cancellationToken)
    {
        this._loggerWrapper.LogMessage("Starting Spectral installation.");

        Directory.CreateDirectory(this._spectralDirectory);

        var executablePath = GetSpectralFileName();
        var url = string.Format(SpectralDownloadUrlFormat, SpectralVersion, executablePath);
        var destination = Path.Combine(this._spectralDirectory, executablePath);

        try
        {
            await this._httpClientWrapper.DownloadFileToDestinationAsync(url, destination, cancellationToken);
        }
        catch (Exception)
        {
            this._loggerWrapper.LogWarning("Failed to download Spectral from {0} to {1}.", url, destination);
            throw;
        }

        this._loggerWrapper.LogMessage("Spectral installation completed.");

        return executablePath;
    }

    private static string GetSpectralFileName()
    {
        var osType = RuntimeInformationHelper.GetOperatingSystem();
        var architecture = RuntimeInformationHelper.GetArchitecture();

        if (osType == "linux")
        {
            var distro = File.Exists("/etc/os-release") ? File.ReadAllText("/etc/os-release") : string.Empty;
            if (distro.Contains("Alpine Linux"))
            {
                osType = "alpine";
            }
        }

        var fileName = $"spectral-{osType}-{architecture}";

        if (osType == "windows")
        {
            fileName = "spectral.exe";
        }

        return fileName;
    }
}