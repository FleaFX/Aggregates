namespace Aggregates.Policies;

/// <summary>
/// Configures the behaviour of a policy.
/// </summary>
/// <param name="name">The name of the policy.</param>
/// <param name="version">The version of the policy. You should only ever have one version for every <paramref name="name"/> in your codebase.</param>
/// <param name="namespace">Optional. A namespace to prepend to the name of your policy.</param>
/// <param name="continueFrom">Optional. Specifies the name of a previous version of the policy to continue projecting from.</param>
/// <param name="errorHandlingMode">Optional. Defines how the policy should behave when command handling errors occur. Defaults to <see cref="PolicyErrorHandlingMode.FailFast"/>.</param>
/// <param name="maxErrors">Optional. Defines the maximum number of failures to tolerate before failing an event, in case the <paramref name="errorHandlingMode"/> is <see cref="PolicyErrorHandlingMode.ContinueUntilMaxErrors"/>. Defaults to 1.</param>
/// <param name="maxErrorRate">Optional. Defines the maximum percentage of failures to tolerate before failing an event, in case the <paramref name="errorHandlingMode"/> is <see cref="PolicyErrorHandlingMode.ContinueUntilMaxFailureRate"/>. Defaults to 0.1 (10%).</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public class PolicyContractAttribute(
    string name, 
    int version = 1, 
    string? @namespace = null, 
    string? continueFrom = null, 
    PolicyErrorHandlingMode errorHandlingMode = PolicyErrorHandlingMode.FailFast,
    int? maxErrors = 1,
    double? maxErrorRate = .1
) : Attribute {
    /// <summary>
    /// Returns the fully qualified name of the preceding policy contract, if any.
    /// </summary>
    public string? ContinueFrom => continueFrom ?? (version > 1
        ? new PolicyContractAttribute(name, version - 1, @namespace).ToString()
        : null);

    /// <summary>
    /// Defines how the policy should behave when command handling errors occur.
    /// </summary>
    public PolicyErrorHandlingMode ErrorHandlingMode => errorHandlingMode;

    /// <summary>
    /// Defines the maximum number of failures to tolerate before failing an event, in case the <see cref="ErrorHandlingMode"/> is <see cref="PolicyErrorHandlingMode.ContinueUntilMaxErrors"/>.
    /// </summary>
    public int MaxErrors => maxErrors ?? 1;

    /// <summary>
    /// Defines the maximum percentage of failures to tolerate before failing an event, in case the <see cref="ErrorHandlingMode"/> is <see cref="PolicyErrorHandlingMode.ContinueUntilMaxFailureRate"/>.
    /// </summary>
    public double MaxErrorRate => maxErrorRate ?? .1;

    /// <summary>
    /// Returns the policy contract name.
    /// </summary>
    /// <returns>A <see cref="string"/>.</returns>
    public override string ToString() => $"{(!string.IsNullOrWhiteSpace(@namespace) ? $"{@namespace}." : string.Empty)}{name}@v{version}";
}

/// <summary>
/// Defines how a policy should behave when command handling errors occur.
/// </summary>
public enum PolicyErrorHandlingMode {
    /// <summary>
    /// Stops processing any further commands when one command fails.
    /// </summary>
    /// <remarks>
    /// This behaviour only applies to handling multiple commands in response to a single event. Failure does NOT affect the handling of commands for subsequent events.
    /// </remarks>
    FailFast,

    /// <summary>
    /// Continues processing remaining commands even if one fails.
    /// </summary>
    /// <remarks>
    /// This mode causes the policy to ignore command handling errors and proceed with the next command. As a result, handling of a single event will never fail due to command errors. 
    /// To introduce fault tolerance with thresholds, use <see cref="ContinueUntilMaxErrors"/> or <see cref="ContinueUntilMaxFailureRate"/> instead, and configure the threshold in the policy contract.
    /// </remarks>
    ContinueOnError,

    /// <summary>
    /// Continues processing further commands when failures occur, until a fixed maximum number of errors is reached.
    /// </summary>
    /// <remarks>
    /// The number of command failures is counted per event. Once the configured maximum number of failures is reached, handling of the event itself fails.
    /// </remarks>
    ContinueUntilMaxErrors,

    /// <summary>
    /// Continues processing further commands when failures occur, until a maximum failure rate (percentage of the whole batch) is reached.
    /// </summary>
    /// <remarks>
    /// The number of failed commands is tracked relative to the total number of commands for an event. Once the configured failure rate is exceeded, handling of the event itself fails.
    /// </remarks>
    ContinueUntilMaxFailureRate
}