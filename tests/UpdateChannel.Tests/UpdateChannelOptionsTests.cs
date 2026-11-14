using Microsoft.Extensions.Configuration;

namespace PolygonLab.UpdateChannel.Tests;

public sealed class UpdateChannelOptionsTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>("UpdateChannel:" + v.Key, v.Value)))
            .Build();

    [Fact]
    public void Create_WithDefaults_UsesStableChannelAndChecksOnStartup()
    {
        var options = UpdateChannelOptions.Create("https://updates.example.com/sampleapp/");

        Assert.Equal(new Uri("https://updates.example.com/sampleapp/"), options.FeedUrl);
        Assert.Equal("stable", options.Channel);
        Assert.True(options.CheckOnStartup);
        Assert.False(options.AllowDowngrade);
    }

    [Theory]
    [InlineData("https://updates.example.com/sampleapp", "https://updates.example.com/sampleapp/")]
    [InlineData("https://updates.example.com/sampleapp/", "https://updates.example.com/sampleapp/")]
    [InlineData("  https://updates.example.com/a/b  ", "https://updates.example.com/a/b/")]
    [InlineData("https://updates.example.com", "https://updates.example.com/")]
    public void Create_AddsTheTrailingSlashOfAFolder(string input, string expected)
        => Assert.Equal(new Uri(expected), UpdateChannelOptions.Create(input).FeedUrl);

    [Theory]
    [InlineData("http://localhost:8080/releases/")]
    [InlineData("http://127.0.0.1:8080/releases/")]
    [InlineData("http://[::1]:8080/releases/")]
    public void Create_AcceptsPlainHttpOnLocalhost(string url)
        => Assert.Equal("http", UpdateChannelOptions.Create(url).FeedUrl.Scheme);

    [Theory]
    [InlineData(null, "FeedUrl is required")]
    [InlineData("", "FeedUrl is required")]
    [InlineData("   ", "FeedUrl is required")]
    [InlineData("updates/sampleapp", "is not an absolute address")]
    [InlineData("http://updates.example.com/sampleapp/", "must use HTTPS")]
    [InlineData("ftp://updates.example.com/sampleapp/", "must use HTTPS")]
    [InlineData("file:///C:/releases/", "must use HTTPS")]
    [InlineData("https://updates.example.com/sampleapp/?token=1", "plain folder address")]
    [InlineData("https://updates.example.com/sampleapp/#top", "plain folder address")]
    public void Create_RejectsAnInvalidFeedUrl(string? url, string expected)
    {
        var exception = Assert.Throws<UpdateChannelConfigurationException>(() => UpdateChannelOptions.Create(url));

        Assert.Contains(expected, Assert.Single(exception.Errors), StringComparison.Ordinal);
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RejectsAFeedUrlWithCredentials()
    {
        var url = new UriBuilder(Uri.UriSchemeHttps, "updates.example.com") { UserName = "reader", Password = "not-a-real-one", Path = "sampleapp/" }.Uri;

        var exception = Assert.Throws<UpdateChannelConfigurationException>(() => UpdateChannelOptions.Create(url.ToString()));

        Assert.Contains("without query string, fragment or credentials", Assert.Single(exception.Errors), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("stable")]
    [InlineData("beta")]
    [InlineData("release-candidate-2")]
    [InlineData(" beta ")]
    public void Create_AcceptsAValidChannel(string channel)
        => Assert.Equal(channel.Trim(), UpdateChannelOptions.Create("https://updates.example.com/", channel).Channel);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Beta")]
    [InlineData("2beta")]
    [InlineData("beta channel")]
    [InlineData("../stable")]
    [InlineData("a234567890123456789012345678901234")]
    public void Create_RejectsAnInvalidChannel(string? channel)
    {
        var exception = Assert.Throws<UpdateChannelConfigurationException>(() => UpdateChannelOptions.Create("https://updates.example.com/", channel));

        Assert.Contains("Channel", Assert.Single(exception.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ListsEveryProblemAtOnce()
    {
        var exception = Assert.Throws<UpdateChannelConfigurationException>(() => UpdateChannelOptions.Create("http://updates.example.com/", "Beta"));

        Assert.Equal(2, exception.Errors.Count);
    }

    [Fact]
    public void ReleaseIndexUrl_PointsToTheIndexOfTheChannel()
    {
        var options = UpdateChannelOptions.Create("https://updates.example.com/sampleapp", "beta");

        Assert.Equal(new Uri("https://updates.example.com/sampleapp/releases.beta.json"), options.ReleaseIndexUrl);
    }

    [Fact]
    public void FromConfiguration_ReadsEveryValue()
    {
        var options = UpdateChannelOptions.FromConfiguration(Configuration(
            ("FeedUrl", "https://updates.example.com/sampleapp/"),
            ("Channel", "beta"),
            ("CheckOnStartup", "false"),
            ("AllowDowngrade", "True")));

        Assert.Equal("beta", options.Channel);
        Assert.False(options.CheckOnStartup);
        Assert.True(options.AllowDowngrade);
    }

    [Fact]
    public void FromConfiguration_AppliesDefaultsForMissingKeys()
    {
        var options = UpdateChannelOptions.FromConfiguration(Configuration(("FeedUrl", "https://updates.example.com/sampleapp/"), ("CheckOnStartup", " ")));

        Assert.Equal(UpdateChannelOptions.DefaultChannel, options.Channel);
        Assert.True(options.CheckOnStartup);
        Assert.False(options.AllowDowngrade);
    }

    [Fact]
    public void FromConfiguration_WithoutSection_ExplainsThatFeedUrlIsRequired()
    {
        var exception = Assert.Throws<UpdateChannelConfigurationException>(() => UpdateChannelOptions.FromConfiguration(new ConfigurationBuilder().Build()));

        Assert.Contains("UpdateChannel:FeedUrl is required", Assert.Single(exception.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void FromConfiguration_RejectsInvalidValuesTogether()
    {
        var exception = Assert.Throws<UpdateChannelConfigurationException>(() => UpdateChannelOptions.FromConfiguration(Configuration(
            ("FeedUrl", "http://updates.example.com/"),
            ("Channel", "Stable"),
            ("CheckOnStartup", "yes"),
            ("AllowDowngrade", "1"))));

        Assert.Equal(4, exception.Errors.Count);
        Assert.Contains(exception.Errors, e => e.Contains("CheckOnStartup must be true or false", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, e => e.Contains("AllowDowngrade must be true or false", StringComparison.Ordinal));
    }

    [Fact]
    public void FromConfiguration_RejectsANullConfiguration()
        => Assert.Throws<ArgumentNullException>(() => UpdateChannelOptions.FromConfiguration(null!));

    [Fact]
    public void ConfigurationException_StandardConstructors()
    {
        Assert.Equal("Invalid update channel configuration.", new UpdateChannelConfigurationException().Errors.Single());
        Assert.Equal("Invalid update channel configuration.", new UpdateChannelConfigurationException([]).Message);
        Assert.Equal("Invalid update channel configuration: one", new UpdateChannelConfigurationException("one").Message);

        var inner = new InvalidOperationException("cause");
        var exception = new UpdateChannelConfigurationException("two", inner);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(["two"], exception.Errors);
    }
}
