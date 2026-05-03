using System;
using System.Collections.Generic;
using System.Linq;
using ChapterCreator.Configuration;
using ChapterCreator.Data;
using ChapterCreator.Managers;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ChapterCreator.Tests;

public class QueueManagerTests
{
    [Fact]
    public void GetMediaItems_UsesInjectedConfigurationWithoutPluginInstance()
    {
        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        libraryManager
            .Setup(manager => manager.GetVirtualFolders())
            .Returns([]);
        var mediaSourceManager = new Mock<IMediaSourceManager>(MockBehavior.Strict);
        var configurationAccessor = CreateConfigurationAccessor(new PluginConfiguration());

        var sut = new QueueManager(
            NullLogger<QueueManager>.Instance,
            libraryManager.Object,
            mediaSourceManager.Object,
            configurationAccessor.Object);

        var result = sut.GetMediaItems();

        Assert.Empty(result);
        libraryManager.Verify(manager => manager.GetVirtualFolders(), Times.Once);
        mediaSourceManager.VerifyNoOtherCalls();
        libraryManager.VerifyNoOtherCalls();
    }

    [Fact]
    public void GetMediaItems_ReturnsFlatOrderedListAcrossEpisodesAndMovieSources()
    {
        var selectedFolder = new VirtualFolderInfo
        {
            Name = "Selected",
            ItemId = Guid.NewGuid().ToString()
        };
        var seasonId = Guid.NewGuid();
        var episodeOneId = Guid.NewGuid();
        var episodeTwoId = Guid.NewGuid();
        var skippedEpisodeId = Guid.NewGuid();
        var movieSourceOneId = Guid.NewGuid();
        var movieSourceTwoId = Guid.NewGuid();
        var skippedMovieSourceId = Guid.NewGuid();
        var movie = CreateMovie(
            "Movie A",
            TimeSpan.FromMinutes(100),
            (movieSourceOneId, "1080p", @"C:\media\movie-a-1080p.mkv"),
            (movieSourceTwoId, "4K", @"C:\media\movie-a-4k.mkv"));
        var skippedMovie = CreateMovie(
            "Movie Missing Runtime",
            null,
            (skippedMovieSourceId, "1080p", @"C:\media\movie-missing-runtime.mkv"));
        var queries = new List<InternalItemsQuery>();
        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        libraryManager
            .Setup(manager => manager.GetVirtualFolders())
            .Returns([selectedFolder]);
        libraryManager
            .Setup(manager => manager.GetItemList(It.IsAny<InternalItemsQuery>(), false))
            .Callback<InternalItemsQuery, bool>((query, _) => queries.Add(query))
            .Returns([
                CreateEpisode("Series A", "Episode 1", 1, seasonId, episodeOneId, @"C:\media\series-a-s01e01.mkv", TimeSpan.FromMinutes(21)),
                CreateEpisode("Series A", "Episode 2", 1, seasonId, episodeTwoId, @"C:\media\series-a-s01e02.mkv", TimeSpan.FromMinutes(22)),
                movie.Movie,
                CreateEpisode("Series A", "Missing Path", 1, seasonId, skippedEpisodeId, string.Empty, TimeSpan.FromMinutes(23)),
                skippedMovie.Movie,
            ]);

        var mediaSourceManager = new Mock<IMediaSourceManager>(MockBehavior.Strict);
        mediaSourceManager
            .Setup(manager => manager.GetStaticMediaSources(movie.Movie, false, null))
            .Returns(movie.Sources);
        mediaSourceManager
            .Setup(manager => manager.GetStaticMediaSources(skippedMovie.Movie, false, null))
            .Returns(skippedMovie.Sources);

        var sut = new QueueManager(
            NullLogger<QueueManager>.Instance,
            libraryManager.Object,
            mediaSourceManager.Object,
            CreateConfigurationAccessor(new PluginConfiguration()).Object);

        var result = sut.GetMediaItems();

        Assert.Collection(
            result,
            media => AssertQueuedMedia(media, "Series A", 1, episodeOneId, "Episode 1", @"C:\media\series-a-s01e01.mkv", true),
            media => AssertQueuedMedia(media, "Series A", 1, episodeTwoId, "Episode 2", @"C:\media\series-a-s01e02.mkv", true),
            media => AssertQueuedMedia(media, "Movie A", 0, movieSourceOneId, "Movie A (1080p)", @"C:\media\movie-a-1080p.mkv", false),
            media => AssertQueuedMedia(media, "Movie A", 0, movieSourceTwoId, "Movie A (4K)", @"C:\media\movie-a-4k.mkv", false));

        var query = Assert.Single(queries);
        Assert.Equal(Guid.Parse(selectedFolder.ItemId), query.ParentId);
        Assert.Collection(
            query.IncludeItemTypes,
            item => Assert.Equal(BaseItemKind.Episode, item),
            item => Assert.Equal(BaseItemKind.Movie, item));
        Assert.Equal(ItemSortBy.SeriesSortName, query.OrderBy[0].Item1);
        Assert.Equal(SortOrder.Ascending, query.OrderBy[0].Item2);
        Assert.Equal(ItemSortBy.ParentIndexNumber, query.OrderBy[1].Item1);
        Assert.Equal(SortOrder.Ascending, query.OrderBy[1].Item2);
        Assert.Equal(ItemSortBy.IndexNumber, query.OrderBy[2].Item1);
        Assert.Equal(SortOrder.Ascending, query.OrderBy[2].Item2);
        libraryManager.Verify(manager => manager.GetVirtualFolders(), Times.Once);
        libraryManager.Verify(manager => manager.GetItemList(It.IsAny<InternalItemsQuery>(), false), Times.Once);
        mediaSourceManager.Verify(manager => manager.GetStaticMediaSources(movie.Movie, false, null), Times.Once);
        mediaSourceManager.Verify(manager => manager.GetStaticMediaSources(skippedMovie.Movie, false, null), Times.Once);
        libraryManager.VerifyNoOtherCalls();
        mediaSourceManager.VerifyNoOtherCalls();
    }

    [Fact]
    public void GetMediaItems_AppliesLibraryAndSkipFilters()
    {
        var includedFolder = new VirtualFolderInfo
        {
            Name = "Included",
            ItemId = Guid.NewGuid().ToString()
        };
        var excludedFolder = new VirtualFolderInfo
        {
            Name = "Excluded",
            ItemId = Guid.NewGuid().ToString()
        };
        var keepEpisodeId = Guid.NewGuid();
        var keepMovieSourceId = Guid.NewGuid();
        var wholeShowSkipEpisodeId = Guid.NewGuid();
        var skippedSeasonEpisodeId = Guid.NewGuid();
        var skippedMovieSourceId = Guid.NewGuid();
        var keepMovie = CreateMovie("Keep Movie", TimeSpan.FromMinutes(90), (keepMovieSourceId, "1080p", @"C:\media\keep-movie.mkv"));
        var skippedMovie = CreateMovie("Skip Movie", TimeSpan.FromMinutes(90), (skippedMovieSourceId, "1080p", @"C:\media\skip-movie.mkv"));
        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        libraryManager
            .Setup(manager => manager.GetVirtualFolders())
            .Returns([includedFolder, excludedFolder]);
        libraryManager
            .Setup(manager => manager.GetItemList(It.Is<InternalItemsQuery>(query => query.ParentId == Guid.Parse(includedFolder.ItemId)), false))
            .Returns([
                CreateEpisode("Keep Show", "Keep Episode", 1, Guid.NewGuid(), keepEpisodeId, @"C:\media\keep-show-s01e01.mkv", TimeSpan.FromMinutes(24)),
                CreateEpisode("Skip Entire Show", "Skipped Episode", 1, Guid.NewGuid(), wholeShowSkipEpisodeId, @"C:\media\skip-entire-show-s01e01.mkv", TimeSpan.FromMinutes(24)),
                CreateEpisode("Skip Season Show", "Skipped Season Episode", 2, Guid.NewGuid(), skippedSeasonEpisodeId, @"C:\media\skip-season-show-s02e01.mkv", TimeSpan.FromMinutes(24)),
                keepMovie.Movie,
                skippedMovie.Movie,
            ]);

        var mediaSourceManager = new Mock<IMediaSourceManager>(MockBehavior.Strict);
        mediaSourceManager
            .Setup(manager => manager.GetStaticMediaSources(keepMovie.Movie, false, null))
            .Returns(keepMovie.Sources);

        var sut = new QueueManager(
            NullLogger<QueueManager>.Instance,
            libraryManager.Object,
            mediaSourceManager.Object,
            CreateConfigurationAccessor(new PluginConfiguration
            {
                SelectedLibraries = "Included",
                SkippedTvShows = "Skip Entire Show, Skip Season Show;S02",
                SkippedMovies = "Skip Movie"
            }).Object);

        var result = sut.GetMediaItems();

        Assert.Collection(
            result,
            media => AssertQueuedMedia(media, "Keep Show", 1, keepEpisodeId, "Keep Episode", @"C:\media\keep-show-s01e01.mkv", true),
            media => AssertQueuedMedia(media, "Keep Movie", 0, keepMovieSourceId, "Keep Movie (1080p)", @"C:\media\keep-movie.mkv", false));

        libraryManager.Verify(manager => manager.GetVirtualFolders(), Times.Once);
        libraryManager.Verify(manager => manager.GetItemList(It.Is<InternalItemsQuery>(query => query.ParentId == Guid.Parse(includedFolder.ItemId)), false), Times.Once);
        mediaSourceManager.Verify(manager => manager.GetStaticMediaSources(keepMovie.Movie, false, null), Times.Once);
        libraryManager.VerifyNoOtherCalls();
        mediaSourceManager.VerifyNoOtherCalls();
    }

    private static Mock<IPluginConfigurationAccessor> CreateConfigurationAccessor(PluginConfiguration configuration)
    {
        var configurationAccessor = new Mock<IPluginConfigurationAccessor>(MockBehavior.Strict);
        configurationAccessor
            .Setup(accessor => accessor.GetConfiguration())
            .Returns(configuration);
        return configurationAccessor;
    }

    private static Episode CreateEpisode(string seriesName, string name, int seasonNumber, Guid seasonId, Guid id, string path, TimeSpan? runtime)
    {
        return new Episode
        {
            Id = id,
            Name = name,
            Path = path,
            RunTimeTicks = runtime?.Ticks,
            SeriesName = seriesName,
            ParentIndexNumber = seasonNumber,
            SeasonId = seasonId
        };
    }

    private static (Movie Movie, IReadOnlyList<MediaSourceInfo> Sources) CreateMovie(string name, TimeSpan? runtime, params (Guid Id, string SourceName, string Path)[] sources)
    {
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            Name = name,
            RunTimeTicks = runtime?.Ticks,
        };

        var mediaSources = sources
            .Select(source => new MediaSourceInfo
            {
                Id = source.Id.ToString(),
                Name = source.SourceName,
                Path = source.Path
            })
            .ToList();

        return (movie, mediaSources);
    }

    private static void AssertQueuedMedia(QueuedMedia media, string expectedSeriesName, int expectedSeasonNumber, Guid expectedItemId, string expectedName, string expectedPath, bool expectedIsEpisode)
    {
        Assert.Equal(expectedSeriesName, media.SeriesName);
        Assert.Equal(expectedSeasonNumber, media.SeasonNumber);
        Assert.Equal(expectedItemId, media.ItemId);
        Assert.Equal(expectedName, media.Name);
        Assert.Equal(expectedPath, media.Path);
        Assert.Equal(expectedIsEpisode, media.IsEpisode);
    }

}
