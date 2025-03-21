using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Workleap.OpenApi.MSBuild.Spectral;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Workleap.OpenApi.MSBuild.Tests.Spectral;

public class SpectralRulesetManagerTests
{
    private const string OverridingRuleset = "./Spectral/ruleset/overriding-ruleset.yaml";
    private const string EjectingRuleset = "./Spectral/ruleset/ejecting-ruleset.yaml";

    [Theory]
    [InlineData("backend")]
    [InlineData("frontend")]
    public async Task Given_Using_Profile_Ruleset_When_GetLocalSpectralRulesetFile_Then_Get_Profile_Ruleset(string profile)
    {
        // Given
        var rulesetManager = new SpectralRulesetManager(new FakeLogger(), new HttpClientWrapper(), profile, null);

        // When
        var rulesetPath = await rulesetManager.GetLocalSpectralRulesetFile(CancellationToken.None);

        // Assert
        var spectralFile = GetSpectralValue(rulesetPath);

        Assert.NotNull(spectralFile.Extends);
        Assert.NotEmpty(spectralFile.Extends);

        Assert.NotNull(spectralFile.Rules);
        Assert.NotEmpty(spectralFile.Rules);
    }

    [Fact]
    public async Task Given_Custom_Hosted_Ruleset_When_GetLocalSpectralRulesetFile_Then_This_Ruleset()
    {
        // Given
        var ruleset = "https://raw.githubusercontent.com/workleap/wl-api-guidelines/0.1.0/.spectral.yaml";
        var expectedExtends = "spectral:oas";
        var expectedNumberOfRules = 10;

        var rulesetManager = new SpectralRulesetManager(new FakeLogger(), new HttpClientWrapper(), "random", ruleset);

        // When
        var rulesetPath = await rulesetManager.GetLocalSpectralRulesetFile(CancellationToken.None);

        // Assert
        var spectralFile = GetSpectralValue(rulesetPath);

        Assert.NotNull(spectralFile.Extends);

        var firstExtendsPair = spectralFile.Extends[0] as List<object>;
        var extendsUrl = firstExtendsPair?[0];
        Assert.Equal(expectedExtends, extendsUrl);

        Assert.NotNull(spectralFile.Rules);
        Assert.Equal(expectedNumberOfRules, spectralFile.Rules.Count);
    }

    [Theory]
    [InlineData("backend")]
    [InlineData("frontend")]
    public async Task Given_Overriding_Ruleset_With_No_Extends_When_GetLocalSpectralRulesetFile_Then_Extends_With_Profile_Ruleset(string profile)
    {
        // Given
        var expectedExtendsPatterns = new Regex($"https://raw\\.githubusercontent\\.com/workleap/wl-api-guidelines/(\\d+\\.\\d+\\.\\d+)/\\.spectral\\.{profile}\\.yaml");

        var rulesetManager = new SpectralRulesetManager(new FakeLogger(), new HttpClientWrapper(), profile, OverridingRuleset);

        // When
        var rulesetPath = await rulesetManager.GetLocalSpectralRulesetFile(CancellationToken.None);

        // Assert
        var spectralFile = GetSpectralValue(rulesetPath);

        Assert.NotNull(spectralFile.Extends);
        Assert.Matches(expectedExtendsPatterns, spectralFile.Extends[0] as string);

        Assert.NotNull(spectralFile.Rules);
        Assert.NotEmpty(spectralFile.Rules);
    }

    [Fact]
    public async Task Given_Ruleset_Fully_Ejected_When_GetLocalSpectralRulesetFile_Then_Do_Not_Extends_With_Profile_Ruleset()
    {
        // Given
        var rulesetManager = new SpectralRulesetManager(new FakeLogger(), new HttpClientWrapper(), "random", EjectingRuleset);
        var expectedNumberOfRules = 1;

        // When
        var rulesetPath = await rulesetManager.GetLocalSpectralRulesetFile(CancellationToken.None);

        // Assert
        var spectralFile = GetSpectralValue(rulesetPath);

        Assert.NotNull(spectralFile.Extends);
        Assert.Empty(spectralFile.Extends);

        Assert.NotNull(spectralFile.Rules);
        Assert.Equal(expectedNumberOfRules, spectralFile.Rules.Count);
    }

    private static SpectralFile GetSpectralValue(string spectralFilePath)
    {
        using var reader = new StreamReader(spectralFilePath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(LowerCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var spectralFile = deserializer.Deserialize<SpectralFile>(reader);
        return spectralFile;
    }

    private sealed class SpectralFile
    {
        public object[]? Extends { get; set; }

        public Dictionary<string, object>? Rules { get; set; }
    }

    private sealed class FakeLogger : ILoggerWrapper
    {
        public void LogWarning(string message, params object[] messageArgs)
        {
        }

        public void LogMessage(string message, MessageImportance importance = MessageImportance.Low, params object[] messageArgs)
        {
        }
    }
}