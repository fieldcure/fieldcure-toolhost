namespace FieldCure.ToolHost;

/// <summary>
/// Policy controlling how <see cref="DnxLiteRunner"/> resolves the version of a tool to run.
/// </summary>
public enum ToolVersionPolicy
{
    /// <summary>
    /// Always query NuGet sources for the latest version. Slow but freshest.
    /// Default for CLI invocation, matching <c>dnx</c> semantics.
    /// </summary>
    AlwaysLatest,

    /// <summary>
    /// Use the cached pinned version if the last NuGet check was within the TTL;
    /// otherwise query. Default for library embedding scenarios with cold-start sensitivity.
    /// </summary>
    CachedWithRefresh,

    /// <summary>
    /// Use the cached pinned version unconditionally. Used when offline or for fast restarts.
    /// Throws if no cached version exists.
    /// </summary>
    CachedOnly,
}
