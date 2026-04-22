using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChapterCreator.Data;
using ChapterCreator.Managers;
using ChapterCreator.SheduledTasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.MediaSegments;
using Moq;
using Xunit;

namespace ChapterCreator.Tests;

public class CreateChapterTaskTests
{
    [Fact]
    public async Task ExecuteAsync_EnumeratesFlatQueuedMediaItemsAndForwardsCollectedSegments()
    {
        var firstItemId = Guid.NewGuid();
        var secondItemId = Guid.NewGuid();
        var queuedMedia = new[]
        {
            new QueuedMedia { SeriesName = "Series A", SeasonNumber = 1, ItemId = firstItemId, Name = "Episode 1", Path = @"C:\media\series-a-s01e01.mkv", IsEpisode = true },
            new QueuedMedia { SeriesName = "Movie A", SeasonNumber = 0, ItemId = secondItemId, Name = "Movie A (1080p)", Path = @"C:\media\movie-a-1080p.mkv", IsEpisode = false }
        };
        var firstItem = new Movie { Id = firstItemId, Path = queuedMedia[0].Path, Name = queuedMedia[0].Name };
        var secondItem = new Movie { Id = secondItemId, Path = queuedMedia[1].Path, Name = queuedMedia[1].Name };
        var firstSegments = new List<MediaSegmentDto>
        {
            new() { ItemId = firstItemId, StartTicks = 10, EndTicks = 20 }
        };
        var secondSegments = new List<MediaSegmentDto>
        {
            new() { ItemId = secondItemId, StartTicks = 30, EndTicks = 40 },
            new() { ItemId = secondItemId, StartTicks = 50, EndTicks = 60 }
        };

        var mediaSegmentManager = new Mock<IMediaSegmentManager>(MockBehavior.Strict);
        mediaSegmentManager
            .Setup(manager => manager.GetSegmentsAsync(firstItem, null, It.Is<LibraryOptions>(options => options.GetType() == typeof(LibraryOptions)), true))
            .ReturnsAsync(firstSegments);
        mediaSegmentManager
            .Setup(manager => manager.GetSegmentsAsync(secondItem, null, It.Is<LibraryOptions>(options => options.GetType() == typeof(LibraryOptions)), true))
            .ReturnsAsync(secondSegments);

        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        libraryManager.Setup(manager => manager.GetItemById(firstItemId)).Returns(firstItem);
        libraryManager.Setup(manager => manager.GetItemById(secondItemId)).Returns(secondItem);

        var queueManager = new Mock<IQueueManager>(MockBehavior.Strict);
        queueManager.Setup(manager => manager.GetMediaItems()).Returns(queuedMedia);

        var chapterTaskRunner = new Mock<IChapterTaskRunner>(MockBehavior.Strict);
        IReadOnlyCollection<MediaSegmentDto>? createdSegments = null;
        chapterTaskRunner
            .Setup(runner => runner.CreateChaptersAsync(It.IsAny<IProgress<double>>(), It.IsAny<IReadOnlyCollection<MediaSegmentDto>>(), false, It.IsAny<CancellationToken>()))
            .Callback<IProgress<double>, IReadOnlyCollection<MediaSegmentDto>, bool, CancellationToken>((_, segments, _, _) => createdSegments = segments)
            .Returns(Task.CompletedTask);

        var legacyChapterMigrator = new Mock<ILegacyChapterMigrator>(MockBehavior.Strict);
        legacyChapterMigrator.Setup(migrator => migrator.MigrateIfNeeded());

        var sut = new CreateChapterTask(
            libraryManager.Object,
            mediaSegmentManager.Object,
            legacyChapterMigrator.Object,
            queueManager.Object,
            chapterTaskRunner.Object);

        await sut.ExecuteAsync(Mock.Of<IProgress<double>>(), CancellationToken.None);

        Assert.NotNull(createdSegments);
        Assert.Equal(new MediaSegmentDto[]
        {
            firstSegments[0],
            secondSegments[0],
            secondSegments[1]
        },
        createdSegments);
        legacyChapterMigrator.Verify(migrator => migrator.MigrateIfNeeded(), Times.Once);
        queueManager.Verify(manager => manager.GetMediaItems(), Times.Once);
        libraryManager.Verify(manager => manager.GetItemById(firstItemId), Times.Once);
        libraryManager.Verify(manager => manager.GetItemById(secondItemId), Times.Once);
        chapterTaskRunner.Verify(runner => runner.CreateChaptersAsync(It.IsAny<IProgress<double>>(), It.IsAny<IReadOnlyCollection<MediaSegmentDto>>(), false, It.IsAny<CancellationToken>()), Times.Once);
        mediaSegmentManager.Verify(manager => manager.GetSegmentsAsync(firstItem, null, It.IsAny<LibraryOptions>(), true), Times.Once);
        mediaSegmentManager.Verify(manager => manager.GetSegmentsAsync(secondItem, null, It.IsAny<LibraryOptions>(), true), Times.Once);
        libraryManager.VerifyNoOtherCalls();
        queueManager.VerifyNoOtherCalls();
        chapterTaskRunner.VerifyNoOtherCalls();
        legacyChapterMigrator.VerifyNoOtherCalls();
        mediaSegmentManager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_WhenItemLookupReturnsNull_SkipsItemAndContinues()
    {
        var validItemId = Guid.NewGuid();
        var missingItemId = Guid.NewGuid();
        var queuedMedia = new[]
        {
            new QueuedMedia { SeriesName = "Series A", SeasonNumber = 1, ItemId = missingItemId, Name = "Missing Episode", Path = @"C:\media\missing.mkv", IsEpisode = true },
            new QueuedMedia { SeriesName = "Series A", SeasonNumber = 1, ItemId = validItemId, Name = "Episode 1", Path = @"C:\media\series-a-s01e01.mkv", IsEpisode = true }
        };
        var validItem = new Movie { Id = validItemId, Path = queuedMedia[1].Path, Name = queuedMedia[1].Name };
        var segments = new List<MediaSegmentDto>
        {
            new() { ItemId = validItemId, StartTicks = 10, EndTicks = 20 }
        };

        var mediaSegmentManager = new Mock<IMediaSegmentManager>(MockBehavior.Strict);
        mediaSegmentManager
            .Setup(manager => manager.GetSegmentsAsync(validItem, null, It.Is<LibraryOptions>(options => options.GetType() == typeof(LibraryOptions)), true))
            .ReturnsAsync(segments);

        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        libraryManager.Setup(manager => manager.GetItemById(missingItemId)).Returns((BaseItem?)null);
        libraryManager.Setup(manager => manager.GetItemById(validItemId)).Returns(validItem);

        var queueManager = new Mock<IQueueManager>(MockBehavior.Strict);
        queueManager.Setup(manager => manager.GetMediaItems()).Returns(queuedMedia);

        var chapterTaskRunner = new Mock<IChapterTaskRunner>(MockBehavior.Strict);
        IReadOnlyCollection<MediaSegmentDto>? createdSegments = null;
        chapterTaskRunner
            .Setup(runner => runner.CreateChaptersAsync(It.IsAny<IProgress<double>>(), It.IsAny<IReadOnlyCollection<MediaSegmentDto>>(), false, It.IsAny<CancellationToken>()))
            .Callback<IProgress<double>, IReadOnlyCollection<MediaSegmentDto>, bool, CancellationToken>((_, segments, _, _) => createdSegments = segments)
            .Returns(Task.CompletedTask);

        var legacyChapterMigrator = new Mock<ILegacyChapterMigrator>(MockBehavior.Strict);
        legacyChapterMigrator.Setup(migrator => migrator.MigrateIfNeeded());

        var sut = new CreateChapterTask(
            libraryManager.Object,
            mediaSegmentManager.Object,
            legacyChapterMigrator.Object,
            queueManager.Object,
            chapterTaskRunner.Object);

        await sut.ExecuteAsync(Mock.Of<IProgress<double>>(), CancellationToken.None);

        Assert.NotNull(createdSegments);
        Assert.Single(createdSegments);
        Assert.Equal(segments[0], createdSegments.Single());
        legacyChapterMigrator.Verify(migrator => migrator.MigrateIfNeeded(), Times.Once);
        queueManager.Verify(manager => manager.GetMediaItems(), Times.Once);
        libraryManager.Verify(manager => manager.GetItemById(missingItemId), Times.Once);
        libraryManager.Verify(manager => manager.GetItemById(validItemId), Times.Once);
        chapterTaskRunner.Verify(runner => runner.CreateChaptersAsync(It.IsAny<IProgress<double>>(), It.IsAny<IReadOnlyCollection<MediaSegmentDto>>(), false, It.IsAny<CancellationToken>()), Times.Once);
        mediaSegmentManager.Verify(manager => manager.GetSegmentsAsync(validItem, null, It.IsAny<LibraryOptions>(), true), Times.Once);
        libraryManager.VerifyNoOtherCalls();
        queueManager.VerifyNoOtherCalls();
        chapterTaskRunner.VerifyNoOtherCalls();
        legacyChapterMigrator.VerifyNoOtherCalls();
        mediaSegmentManager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoSegmentsAreCollected_DoesNotCreateChapters()
    {
        var itemId = Guid.NewGuid();
        var queuedMedia = new[]
        {
            new QueuedMedia { SeriesName = "Series A", SeasonNumber = 1, ItemId = itemId, Name = "Episode 1", Path = @"C:\media\series-a-s01e01.mkv", IsEpisode = true }
        };
        var item = new Movie { Id = itemId, Path = queuedMedia[0].Path, Name = queuedMedia[0].Name };

        var mediaSegmentManager = new Mock<IMediaSegmentManager>(MockBehavior.Strict);
        mediaSegmentManager
            .Setup(manager => manager.GetSegmentsAsync(item, null, It.Is<LibraryOptions>(options => options.GetType() == typeof(LibraryOptions)), true))
            .ReturnsAsync([]);

        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        libraryManager.Setup(manager => manager.GetItemById(itemId)).Returns(item);

        var queueManager = new Mock<IQueueManager>(MockBehavior.Strict);
        queueManager.Setup(manager => manager.GetMediaItems()).Returns(queuedMedia);

        var chapterTaskRunner = new Mock<IChapterTaskRunner>(MockBehavior.Strict);

        var legacyChapterMigrator = new Mock<ILegacyChapterMigrator>(MockBehavior.Strict);
        legacyChapterMigrator.Setup(migrator => migrator.MigrateIfNeeded());

        var sut = new CreateChapterTask(
            libraryManager.Object,
            mediaSegmentManager.Object,
            legacyChapterMigrator.Object,
            queueManager.Object,
            chapterTaskRunner.Object);

        await sut.ExecuteAsync(Mock.Of<IProgress<double>>(), CancellationToken.None);

        legacyChapterMigrator.Verify(migrator => migrator.MigrateIfNeeded(), Times.Once);
        queueManager.Verify(manager => manager.GetMediaItems(), Times.Once);
        libraryManager.Verify(manager => manager.GetItemById(itemId), Times.Once);
        chapterTaskRunner.Verify(runner => runner.CreateChaptersAsync(It.IsAny<IProgress<double>>(), It.IsAny<IReadOnlyCollection<MediaSegmentDto>>(), false, It.IsAny<CancellationToken>()), Times.Never);
        mediaSegmentManager.Verify(manager => manager.GetSegmentsAsync(item, null, It.IsAny<LibraryOptions>(), true), Times.Once);
        libraryManager.VerifyNoOtherCalls();
        queueManager.VerifyNoOtherCalls();
        chapterTaskRunner.VerifyNoOtherCalls();
        legacyChapterMigrator.VerifyNoOtherCalls();
        mediaSegmentManager.VerifyNoOtherCalls();
    }
}
