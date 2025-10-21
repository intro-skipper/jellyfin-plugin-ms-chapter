namespace ChapterCreator.Data;

/// <summary>
/// Media queued for analysis.
/// </summary>
public class Chapter
{
    /// <summary>
    /// Gets or sets the start time of the chapter.
    /// </summary>
    public string StartTime { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the end time of the chapter.
    /// </summary>
    public string EndTime { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title of the chapter.
    /// </summary>
    public string Title { get; set; } = string.Empty;
}
