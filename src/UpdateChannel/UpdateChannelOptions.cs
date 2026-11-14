using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace PolygonLab.UpdateChannel;

/// <summary>
/// Where the application looks for updates and how. Immutable and always valid: build it with
/// <see cref="Create"/> or <see cref="FromConfiguration"/>, which reject an invalid value with a clear message.
/// </summary>
/// <remarks>
/// Configuration section (for example in <c>appsettings.json</c>):
/// <code>
/// "UpdateChannel": {
///   "FeedUrl": "https://updates.example.com/sampleapp/",
///   "Channel": "stable",
///   "CheckOnStartup": true,
///   "AllowDowngrade": false
/// }
/// </code>
/// </remarks>
public sealed partial class UpdateChannelOptions
{
    /// <summary>Name of the configuration section read by <see cref="FromConfiguration"/>.</summary>
    public const string SectionName = "UpdateChannel";

    /// <summary>Channel used when none is configured.</summary>
    public const string DefaultChannel = "stable";

    /// <summary>Longest accepted channel name.</summary>
    public const int MaxChannelLength = 32;

    private UpdateChannelOptions(Uri feedUrl, string channel, bool checkOnStartup, bool allowDowngrade)
    {
        FeedUrl = feedUrl;
        Channel = channel;
        CheckOnStartup = checkOnStartup;
        AllowDowngrade = allowDowngrade;
    }

    /// <summary>
    /// Address of the folder that holds the release files on the web server, always ending with a slash
    /// (for example <c>https://updates.example.com/sampleapp/</c>).
    /// </summary>
    public Uri FeedUrl { get; }

    /// <summary>Release channel, for example <c>stable</c> or <c>beta</c>.</summary>
    public string Channel { get; }

    /// <summary>When true, the application checks for updates in the background when it starts.</summary>
    public bool CheckOnStartup { get; }

    /// <summary>
    /// When true, a lower version is accepted. Needed to move a user from <c>beta</c> back to <c>stable</c>
    /// when the stable version is older than the installed beta.
    /// </summary>
    public bool AllowDowngrade { get; }

    /// <summary>Builds validated options.</summary>
    /// <exception cref="UpdateChannelConfigurationException">A value is invalid; every problem is listed.</exception>
    public static UpdateChannelOptions Create(string? feedUrl, string? channel = DefaultChannel, bool checkOnStartup = true, bool allowDowngrade = false)
    {
        var errors = new List<string>();
        var uri = ValidateFeedUrl(feedUrl, errors);
        var name = ValidateChannel(channel, errors);
        if (errors.Count > 0 || uri is null || name is null)
        {
            throw new UpdateChannelConfigurationException(errors);
        }

        return new UpdateChannelOptions(uri, name, checkOnStartup, allowDowngrade);
    }

    /// <summary>
    /// Reads the <c>UpdateChannel</c> section of <paramref name="configuration"/>. Missing keys take their default value
    /// (<c>Channel</c>: <c>stable</c>, <c>CheckOnStartup</c>: true, <c>AllowDowngrade</c>: false); <c>FeedUrl</c> is required.
    /// </summary>
    /// <exception cref="UpdateChannelConfigurationException">The section is missing or a value is invalid; every problem is listed.</exception>
    public static UpdateChannelOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);
        var errors = new List<string>();
        var uri = ValidateFeedUrl(section["FeedUrl"], errors);
        var name = ValidateChannel(section["Channel"] ?? DefaultChannel, errors);
        var checkOnStartup = ReadBoolean(section, "CheckOnStartup", defaultValue: true, errors);
        var allowDowngrade = ReadBoolean(section, "AllowDowngrade", defaultValue: false, errors);
        if (errors.Count > 0 || uri is null || name is null)
        {
            throw new UpdateChannelConfigurationException(errors);
        }

        return new UpdateChannelOptions(uri, name, checkOnStartup, allowDowngrade);
    }

    /// <summary>Address of the release index of this channel (the file Velopack downloads first).</summary>
    public Uri ReleaseIndexUrl => new(FeedUrl, "releases." + Channel + ".json");

    private static Uri? ValidateFeedUrl(string? value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(SectionName + ":FeedUrl is required (the HTTPS address of the folder that holds the release files).");
            return null;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            errors.Add(SectionName + ":FeedUrl \"" + value + "\" is not an absolute address.");
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        {
            errors.Add(SectionName + ":FeedUrl must use HTTPS (plain HTTP is only accepted for localhost, for tests): \"" + value + "\".");
            return null;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            errors.Add(SectionName + ":FeedUrl must be a plain folder address, without query string, fragment or credentials.");
            return null;
        }

        // A folder address ends with a slash, so that relative file names resolve inside it.
        return uri.AbsolutePath.EndsWith('/') ? uri : new Uri(uri.GetLeftPart(UriPartial.Path) + "/");
    }

    private static string? ValidateChannel(string? value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(SectionName + ":Channel must not be empty (for example \"stable\" or \"beta\").");
            return null;
        }

        var channel = value.Trim();
        if (channel.Length > MaxChannelLength || !ChannelPattern().IsMatch(channel))
        {
            errors.Add(SectionName + ":Channel \"" + value + "\" is invalid: use 1 to " + MaxChannelLength.ToString(CultureInfo.InvariantCulture)
                + " lowercase letters, digits or hyphens, starting with a letter (for example \"stable\" or \"beta\").");
            return null;
        }

        return channel;
    }

    private static bool ReadBoolean(IConfigurationSection section, string key, bool defaultValue, List<string> errors)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        if (bool.TryParse(raw.Trim(), out var value))
        {
            return value;
        }

        errors.Add(SectionName + ":" + key + " must be true or false, not \"" + raw + "\".");
        return defaultValue;
    }

    [GeneratedRegex("^[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ChannelPattern();
}
