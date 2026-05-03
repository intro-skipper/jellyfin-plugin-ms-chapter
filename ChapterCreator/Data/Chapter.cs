namespace ChapterCreator.Data;

/// <summary>
/// Media queued for analysis.
/// </summary>
public sealed record class Chapter
{
    /// <summary>
    /// Gets the start time of the chapter.
    /// </summary>
    public string StartTime { get; init; } = string.Empty;

    /// <summary>
    /// Gets the end time of the chapter.
    /// </summary>
    public string EndTime { get; init; } = string.Empty;

    /// <summary>
    /// Gets the title of the chapter.
    /// </summary>
    public string Title { get; init; } = string.Empty;
}
