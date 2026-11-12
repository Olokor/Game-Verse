namespace GameVerse.Api.Hosting;

/// <summary>
/// Source-generated log messages. (.NET note: [LoggerMessage] makes the compiler write the logging code,
/// avoiding string formatting and boxing when the level is disabled.)
/// </summary>
internal static partial class StartupLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Applying database migrations")]
    public static partial void MigrationsStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database migrations applied")]
    public static partial void MigrationsApplied(ILogger logger);
}
