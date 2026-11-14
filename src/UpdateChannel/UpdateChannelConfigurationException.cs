namespace PolygonLab.UpdateChannel;

/// <summary>The update channel settings are missing or invalid.</summary>
public sealed class UpdateChannelConfigurationException : Exception
{
    /// <summary>Creates the exception with the list of problems found.</summary>
    public UpdateChannelConfigurationException(IReadOnlyList<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    /// <summary>Creates the exception with a single message.</summary>
    public UpdateChannelConfigurationException(string message)
        : this([message])
    {
    }

    /// <summary>Creates the exception with a single message and its cause.</summary>
    public UpdateChannelConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [message];
    }

    /// <summary>Creates the exception without details (required by the standard exception pattern).</summary>
    public UpdateChannelConfigurationException()
        : this("Invalid update channel configuration.")
    {
    }

    /// <summary>Every problem found, one sentence each.</summary>
    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors.Count == 0
            ? "Invalid update channel configuration."
            : "Invalid update channel configuration: " + string.Join(" ", errors);
    }
}
