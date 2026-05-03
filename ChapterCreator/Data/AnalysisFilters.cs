using System.Collections.Generic;

namespace ChapterCreator.Data;

/// <summary>
/// Holds the parsed analysis filter settings for a single GetMediaItems invocation.
/// </summary>
public sealed record class AnalysisFilters
(
    IReadOnlyList<string> SelectedLibraries,
    IReadOnlyList<string> SkippedMovies,
    IReadOnlyDictionary<string, IReadOnlyList<int>> SkippedTvShows);
