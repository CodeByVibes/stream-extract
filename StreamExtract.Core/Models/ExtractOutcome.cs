namespace StreamExtract.Models;

/// <summary>
/// Result of an extraction. Failures are collected per mode/item so one bad item
/// (e.g. a single attachment with an invalid name) does not abort the rest of the
/// extraction — the caller can log each failure and still keep the successful parts.
/// </summary>
public sealed record ExtractOutcome(bool Succeeded, IReadOnlyList<string> Failures)
{
    public static ExtractOutcome Success { get; } = new(true, []);

    public static ExtractOutcome Failure(string message) => new(false, [message]);
}
