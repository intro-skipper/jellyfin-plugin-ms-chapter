using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChapterCreator.Configuration;
using ChapterCreator.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace ChapterCreator.Managers;

/// <summary>
/// Manages enqueuing library items for analysis.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="QueueManager"/> class.
/// </remarks>
/// <param name="logger">Logger.</param>
/// <param name="libraryManager">Library manager.</param>
/// <param name="mediaSourceManager">Media source manager.</param>
/// <param name="configurationAccessor">Plugin configuration accessor.</param>
public partial class QueueManager(
    ILogger<QueueManager> logger,
    ILibraryManager libraryManager,
    IMediaSourceManager mediaSourceManager,
    IPluginConfigurationAccessor configurationAccessor) : IQueueManager
{
    private readonly ILibraryManager _libraryManager = libraryManager;
    private readonly ILogger<QueueManager> _logger = logger;
    private readonly IMediaSourceManager _mediaSourceManager = mediaSourceManager;
    private readonly IPluginConfigurationAccessor _configurationAccessor = configurationAccessor;

    /// <summary>
    /// Gets the ordered media items selected for analysis.
    /// </summary>
    /// <returns>An ordered read-only list of queued media items.</returns>
    public IReadOnlyList<QueuedMedia> GetMediaItems()
    {
        var queuedMedia = new List<QueuedMedia>();
        var filters = LoadAnalysisSettings();

        // For all selected libraries, enqueue all contained media items.
        foreach (var folder in _libraryManager.GetVirtualFolders())
        {
            // If libraries have been selected for analysis, ensure this library was selected.
            if (filters.SelectedLibraries.Count > 0 && !filters.SelectedLibraries.Contains(folder.Name))
            {
                LogLibraryNotSelected(_logger, folder.Name);
                continue;
            }

            LogEnqueueLibrary(_logger, folder.Name, folder.ItemId);

            try
            {
                QueueLibraryContents(folder.ItemId, queuedMedia, filters);
            }
            catch (Exception ex)
            {
                LogEnqueueFailure(_logger, folder.Name, ex);
            }
        }

        return queuedMedia;
    }

    /// <summary>
    /// Loads the list of libraries which have been selected to use or skipped.
    /// </summary>
    private AnalysisFilters LoadAnalysisSettings()
    {
        var config = _configurationAccessor.GetConfiguration();

        // Get the list of library names which have been selected for analysis, ignoring whitespace and empty entries.
        var selectedLibraries = config.SelectedLibraries
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // Get the list movie names which should be skipped.
        var skippedMovies = config.SkippedMovies
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // Get the list of tvshow names and seasons which should be skipped for analysis.
        var skippedTvShows = new Dictionary<string, List<int>>();
        var show = config.SkippedTvShows
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        foreach (var s in show)
        {
            if (s.Contains(';', StringComparison.InvariantCulture))
            {
                var rseasons = s.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var seasons = rseasons.Skip(1).ToArray();
                var name = rseasons.ElementAt(0);
                var seasonNumbers = new List<int>();

                foreach (var season in seasons)
                {
                    var nr = season[1..];

                    try
                    {
                        seasonNumbers.Add(int.Parse(nr, CultureInfo.InvariantCulture));
                    }
                    catch (FormatException)
                    {
                        LogSeasonParseFailure(_logger, nr, name);
                    }
                }

                skippedTvShows.Add(name, seasonNumbers);
            }
            else
            {
                skippedTvShows.Add(s, []);
            }
        }

        // If any libraries have been selected for analysis, log their names.
        if (selectedLibraries.Count > 0)
        {
            LogSelectedLibraries(_logger, string.Join(", ", selectedLibraries));
        }
        else
        {
            LogNoLibraryFilter(_logger);
        }

        return new AnalysisFilters(selectedLibraries, skippedMovies, skippedTvShows);
    }

    private void QueueLibraryContents(string rawId, List<QueuedMedia> queuedMedia, AnalysisFilters filters)
    {
        LogConstructingQuery(_logger);

        var includes = new BaseItemKind[] { BaseItemKind.Episode, BaseItemKind.Movie };

        var query = new InternalItemsQuery()
        {
            // Order by series name, season, and then episode number so that status updates are logged in order
            ParentId = Guid.Parse(rawId),
            OrderBy =
            [
                (ItemSortBy.SeriesSortName, SortOrder.Ascending),
                (ItemSortBy.ParentIndexNumber, SortOrder.Ascending),
                (ItemSortBy.IndexNumber, SortOrder.Ascending),
            ],
            IncludeItemTypes = includes,
            Recursive = true,
            IsVirtualItem = false
        };

        LogGettingItems(_logger);

        var items = _libraryManager.GetItemList(query, false);

        if (items is null)
        {
            LogLibraryQueryNull(_logger);
            return;
        }

        // Queue all media on the server.
        LogIteratingItems(_logger);

        foreach (var item in items)
        {
            if (item is Episode episode)
            {
                if (ShouldSkipEpisode(episode, filters.SkippedTvShows))
                {
                    LogSkippingEpisode(_logger, episode.Name, episode.SeriesName, episode.AiredSeasonNumber);
                    continue;
                }

                QueueEpisode(episode, queuedMedia);
            }
            else if (item is Movie movie)
            {
                if (filters.SkippedMovies.Contains(movie.Name))
                {
                    LogSkippingMovie(_logger, movie.Name);
                    continue;
                }

                // Movie can have multiple MediaSources like 1080p and a 4k file, they have different ids
                foreach (var source in _mediaSourceManager.GetStaticMediaSources(movie, false, null))
                {
                    LogAddingMovie(_logger, movie.Name, source.Name);
                    QueueMovie(movie, source, queuedMedia);
                }
            }
            else
            {
                LogItemNotEpisodeOrMovie(_logger, item.Name);
                continue;
            }
        }

        LogQueuedItemCount(_logger, queuedMedia.Count);
    }

    private static bool ShouldSkipEpisode(Episode episode, Dictionary<string, List<int>> skippedTvShows)
    {
        if (skippedTvShows.TryGetValue(episode.SeriesName, out var seasons))
        {
            return seasons.Count == 0 || (episode.AiredSeasonNumber != null && seasons.Contains(episode.AiredSeasonNumber.GetValueOrDefault()));
        }

        return false;
    }

    private void QueueEpisode(Episode episode, List<QueuedMedia> queuedMedia)
    {
        if (string.IsNullOrEmpty(episode.Path))
        {
            LogEpisodeNoPath(_logger, episode.Name, episode.SeriesName, episode.Id);
            return;
        }

        if (episode.RunTimeTicks is null)
        {
            LogEpisodeNoDuration(_logger, episode.Name, episode.SeriesName, episode.Id);
            return;
        }

        queuedMedia.Add(new QueuedMedia()
        {
            SeriesName = episode.SeriesName,
            SeasonNumber = episode.AiredSeasonNumber ?? 0,
            ItemId = episode.Id,
            Name = episode.Name,
            Path = episode.Path,
        });
    }

    private void QueueMovie(Movie movie, MediaSourceInfo source, List<QueuedMedia> queuedMedia)
    {
        if (string.IsNullOrEmpty(source.Path))
        {
            LogMovieNoPath(_logger, movie.Name, source.Name, source.Id);
            return;
        }

        if (movie.RunTimeTicks is null)
        {
            LogMovieNoDuration(_logger, movie.Name, source.Name, source.Id);
            return;
        }

        queuedMedia.Add(new QueuedMedia()
        {
            SeriesName = movie.Name,
            SeasonNumber = 0,
            ItemId = Guid.Parse(source.Id),
            Name = $"{movie.Name} ({source.Name})",
            Path = source.Path,
            IsEpisode = false,
        });
    }

    // Source-generated logging
    [LoggerMessage(EventId = 1200, Level = LogLevel.Debug, Message = "Not analyzing library \"{Name}\": not selected by user")]
    private static partial void LogLibraryNotSelected(ILogger logger, string name);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information, Message = "Running enqueue of items in library {Name} ({ItemId})")]
    private static partial void LogEnqueueLibrary(ILogger logger, string name, string itemId);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Error, Message = "Failed to enqueue items from library {Name}")]
    private static partial void LogEnqueueFailure(ILogger logger, string name, Exception ex);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Error, Message = "Skipping TV Shows: Failed to parse season number '{Nr}' for tv show: {Name}. Fix your config!")]
    private static partial void LogSeasonParseFailure(ILogger logger, string nr, string name);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Information, Message = "Limiting analysis to the following libraries: {Selected}")]
    private static partial void LogSelectedLibraries(ILogger logger, string selected);

    [LoggerMessage(EventId = 1205, Level = LogLevel.Debug, Message = "Not limiting analysis by library name")]
    private static partial void LogNoLibraryFilter(ILogger logger);

    [LoggerMessage(EventId = 1206, Level = LogLevel.Debug, Message = "Constructing anonymous internal query")]
    private static partial void LogConstructingQuery(ILogger logger);

    [LoggerMessage(EventId = 1207, Level = LogLevel.Debug, Message = "Getting items")]
    private static partial void LogGettingItems(ILogger logger);

    [LoggerMessage(EventId = 1208, Level = LogLevel.Error, Message = "Library query result is null")]
    private static partial void LogLibraryQueryNull(ILogger logger);

    [LoggerMessage(EventId = 1209, Level = LogLevel.Debug, Message = "Iterating through library items")]
    private static partial void LogIteratingItems(ILogger logger);

    [LoggerMessage(EventId = 1210, Level = LogLevel.Information, Message = "Skipping episode: '{EpisodeName}' of series: '{SeriesName} S{Season}'")]
    private static partial void LogSkippingEpisode(ILogger logger, string episodeName, string seriesName, int? season);

    [LoggerMessage(EventId = 1211, Level = LogLevel.Information, Message = "Skipping Movie: '{Name}'")]
    private static partial void LogSkippingMovie(ILogger logger, string name);

    [LoggerMessage(EventId = 1212, Level = LogLevel.Information, Message = "Adding movie: '{Name} ({Format})'")]
    private static partial void LogAddingMovie(ILogger logger, string name, string format);

    [LoggerMessage(EventId = 1213, Level = LogLevel.Debug, Message = "Item {Name} is not an episode or movie")]
    private static partial void LogItemNotEpisodeOrMovie(ILogger logger, string name);

    [LoggerMessage(EventId = 1214, Level = LogLevel.Debug, Message = "Queued {Count} media items")]
    private static partial void LogQueuedItemCount(ILogger logger, int count);

    [LoggerMessage(EventId = 1215, Level = LogLevel.Warning, Message = "Not queuing episode \"{Name}\" from series \"{Series}\" ({Id}) as no path was provided by Jellyfin")]
    private static partial void LogEpisodeNoPath(ILogger logger, string name, string series, Guid id);

    [LoggerMessage(EventId = 1216, Level = LogLevel.Warning, Message = "Not queuing episode \"{Name}\" from series \"{Series}\" ({Id}) as no duration was provided by Jellyfin")]
    private static partial void LogEpisodeNoDuration(ILogger logger, string name, string series, Guid id);

    [LoggerMessage(EventId = 1217, Level = LogLevel.Warning, Message = "Not queuing movie '{Name} ({Source})' ({Id}) as no path was provided by Jellyfin")]
    private static partial void LogMovieNoPath(ILogger logger, string name, string source, string id);

    [LoggerMessage(EventId = 1218, Level = LogLevel.Warning, Message = "Not queuing movie '{Name} ({Source})' ({Id}) as no duration was provided by Jellyfin")]
    private static partial void LogMovieNoDuration(ILogger logger, string name, string source, string id);

    /// <summary>
    /// Holds the parsed analysis filter settings for a single <see cref="GetMediaItems"/> invocation.
    /// </summary>
    private sealed record AnalysisFilters(
        List<string> SelectedLibraries,
        List<string> SkippedMovies,
        Dictionary<string, List<int>> SkippedTvShows);
}
