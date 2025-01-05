using MediaBrowser.Model.Plugins;

namespace ChapterCreator.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the maximum gap between chapters in seconds.
    /// </summary>
    public int MaxGap { get; set; } = 3;

    /// <summary>
    /// Gets or sets an Unknown Action option.
    /// </summary>
    public string Unknown { get; set; } = "Unknown";

    /// <summary>
    /// Gets or sets an Intro Action option.
    /// </summary>
    public string Intro { get; set; } = "Opening";

    /// <summary>
    /// Gets or sets an Outro Action option.
    /// </summary>
    public string Outro { get; set; } = "Ending";

    /// <summary>
    /// Gets or sets an Preview Action option.
    /// </summary>
    public string Preview { get; set; } = "Preview";

    /// <summary>
    /// Gets or sets an Recap Action option.
    /// </summary>
    public string Recap { get; set; } = "Recap";

    /// <summary>
    /// Gets or sets an Commercial Action option.
    /// </summary>
    public string Commercial { get; set; } = "Commercial";

    /// <summary>
    /// Gets or sets a value indicating whether to overwrite existing chapter files. Which keeps the file in sync with media segment edits.
    /// </summary>
    public bool OverwriteFiles { get; set; } = true;

    /// <summary>
    /// Gets or sets the max degree of parallelism used when creating chapter files.
    /// </summary>
    public int MaxParallelism { get; set; } = 2;

    /// <summary>
    /// Gets or sets the comma separated list of library names to analyze. If empty, all libraries will be analyzed.
    /// </summary>
    public string SelectedLibraries { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the comma separated list of tv shows and seasons to skip the analyze. Format: "My Show;S01;S02, Another Show".
    /// </summary>
    public string SkippedTvShows { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the comma separated list of movies to skip the analyze.".
    /// </summary>
    public string SkippedMovies { get; set; } = string.Empty;
}
