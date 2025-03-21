using CliWrap.Buffered;

namespace Workleap.OpenApi.MSBuild;

internal interface IProcessWrapper
{
    Task<BufferedCommandResult> RunProcessAsync(string filename, string[] arguments, CancellationToken cancellationToken, Dictionary<string, string?>? envVars = null);
}