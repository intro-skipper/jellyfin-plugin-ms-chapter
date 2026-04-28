using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChapterCreator.Controllers;
using ChapterCreator.Managers;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ChapterCreator.Tests;

public class ChapterControllerTests
{
    [Fact]
    public async Task RefreshChapterData_WhenSegmentsAreMissing_ClearsExistingChapterOutput()
    {
        var itemId = Guid.NewGuid();
        var item = new Movie { Id = itemId, Path = @"C:\media\movie.mkv" };
        var mediaSegmentManager = new Mock<IMediaSegmentManager>(MockBehavior.Strict);
        mediaSegmentManager
            .Setup(manager => manager.GetSegmentsAsync(item, null, It.IsAny<LibraryOptions>(), true))
            .ReturnsAsync(new List<MediaSegmentDto>());

        var chapterFileManager = new Mock<IChapterFileManager>(MockBehavior.Strict);
        var chapterOutputService = new Mock<IChapterOutputService>(MockBehavior.Strict);
        chapterOutputService
            .Setup(service => service.ClearChaptersAsync(itemId, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        libraryManager
            .Setup(manager => manager.GetItemById(itemId))
            .Returns(item);

        var sut = new ChapterController(
            mediaSegmentManager.Object,
            chapterFileManager.Object,
            chapterOutputService.Object,
            libraryManager.Object,
            NullLogger<ChapterController>.Instance);

        await sut.RefreshChapterData([itemId], CancellationToken.None);

        libraryManager.Verify(manager => manager.GetItemById(itemId), Times.Once);
        mediaSegmentManager.Verify(manager => manager.GetSegmentsAsync(item, null, It.IsAny<LibraryOptions>(), true), Times.Once);
        chapterOutputService.Verify(service => service.ClearChaptersAsync(itemId, It.IsAny<CancellationToken>()), Times.Once);
        chapterFileManager.VerifyNoOtherCalls();
        chapterOutputService.VerifyNoOtherCalls();
        mediaSegmentManager.VerifyNoOtherCalls();
        libraryManager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RefreshChapterData_WhenGetSegmentsFailsForItem_ProcessesNextItem()
    {
        var failingItemId = Guid.NewGuid();
        var nextItemId = Guid.NewGuid();
        var failingItem = new Movie { Id = failingItemId, Path = @"C:\media\failing.mkv" };
        var nextItem = new Movie { Id = nextItemId, Path = @"C:\media\next.mkv" };
        var failure = new InvalidOperationException("segments failed");

        var mediaSegmentManager = new Mock<IMediaSegmentManager>(MockBehavior.Strict);
        mediaSegmentManager
            .Setup(manager => manager.GetSegmentsAsync(failingItem, null, It.IsAny<LibraryOptions>(), true))
            .ThrowsAsync(failure);
        mediaSegmentManager
            .Setup(manager => manager.GetSegmentsAsync(nextItem, null, It.IsAny<LibraryOptions>(), true))
            .ReturnsAsync(CreateSegments(nextItemId));

        var chapterFileManager = new Mock<IChapterFileManager>(MockBehavior.Strict);
        var chapterOutputService = new Mock<IChapterOutputService>(MockBehavior.Strict);
        chapterOutputService
            .Setup(service => service.ProcessChaptersAsync(
                It.Is<KeyValuePair<Guid, List<MediaSegmentDto>>>(segments => segments.Key == nextItemId && segments.Value.Count == 2),
                true,
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        libraryManager
            .Setup(manager => manager.GetItemById(failingItemId))
            .Returns(failingItem);
        libraryManager
            .Setup(manager => manager.GetItemById(nextItemId))
            .Returns(nextItem);
        var logger = new ListLogger<ChapterController>();

        var sut = new ChapterController(
            mediaSegmentManager.Object,
            chapterFileManager.Object,
            chapterOutputService.Object,
            libraryManager.Object,
            logger);

        await sut.RefreshChapterData([failingItemId, nextItemId], CancellationToken.None);

        Assert.Contains(
            logger.Entries,
            entry => entry.LogLevel == LogLevel.Error &&
                entry.Message.Contains("Failed to retrieve media segments", StringComparison.Ordinal) &&
                entry.Message.Contains(failingItemId.ToString(), StringComparison.Ordinal) &&
                ReferenceEquals(entry.Exception, failure));
        libraryManager.Verify(manager => manager.GetItemById(failingItemId), Times.Once);
        libraryManager.Verify(manager => manager.GetItemById(nextItemId), Times.Once);
        mediaSegmentManager.Verify(manager => manager.GetSegmentsAsync(failingItem, null, It.IsAny<LibraryOptions>(), true), Times.Once);
        mediaSegmentManager.Verify(manager => manager.GetSegmentsAsync(nextItem, null, It.IsAny<LibraryOptions>(), true), Times.Once);
        chapterOutputService.Verify(service => service.ClearChaptersAsync(failingItemId, It.IsAny<CancellationToken>()), Times.Never);
        chapterOutputService.Verify(service => service.ProcessChaptersAsync(
            It.Is<KeyValuePair<Guid, List<MediaSegmentDto>>>(segments => segments.Key == failingItemId),
            true,
            It.IsAny<CancellationToken>()), Times.Never);
        chapterOutputService.Verify(service => service.ProcessChaptersAsync(
            It.Is<KeyValuePair<Guid, List<MediaSegmentDto>>>(segments => segments.Key == nextItemId && segments.Value.Count == 2),
            true,
            It.IsAny<CancellationToken>()), Times.Once);
        chapterFileManager.VerifyNoOtherCalls();
        chapterOutputService.VerifyNoOtherCalls();
        mediaSegmentManager.VerifyNoOtherCalls();
        libraryManager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RefreshChapterData_WhenGetSegmentsIsCanceled_ThrowsAndDoesNotProcessNextItem()
    {
        var failingItemId = Guid.NewGuid();
        var nextItemId = Guid.NewGuid();
        var failingItem = new Movie { Id = failingItemId, Path = @"C:\media\failing.mkv" };
        var nextItem = new Movie { Id = nextItemId, Path = @"C:\media\next.mkv" };
        var cancellation = new OperationCanceledException("segments canceled");

        var mediaSegmentManager = new Mock<IMediaSegmentManager>(MockBehavior.Strict);
        mediaSegmentManager
            .Setup(manager => manager.GetSegmentsAsync(failingItem, null, It.IsAny<LibraryOptions>(), true))
            .ThrowsAsync(cancellation);

        var chapterFileManager = new Mock<IChapterFileManager>(MockBehavior.Strict);
        var chapterOutputService = new Mock<IChapterOutputService>(MockBehavior.Strict);

        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        libraryManager
            .Setup(manager => manager.GetItemById(failingItemId))
            .Returns(failingItem);
        libraryManager
            .Setup(manager => manager.GetItemById(nextItemId))
            .Returns(nextItem);
        var logger = new ListLogger<ChapterController>();

        var sut = new ChapterController(
            mediaSegmentManager.Object,
            chapterFileManager.Object,
            chapterOutputService.Object,
            libraryManager.Object,
            logger);

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(
            () => sut.RefreshChapterData([failingItemId, nextItemId], CancellationToken.None));

        Assert.Same(cancellation, thrown);
        Assert.Empty(logger.Entries);
        libraryManager.Verify(manager => manager.GetItemById(failingItemId), Times.Once);
        libraryManager.Verify(manager => manager.GetItemById(nextItemId), Times.Never);
        mediaSegmentManager.Verify(manager => manager.GetSegmentsAsync(failingItem, null, It.IsAny<LibraryOptions>(), true), Times.Once);
        chapterFileManager.VerifyNoOtherCalls();
        chapterOutputService.VerifyNoOtherCalls();
        mediaSegmentManager.VerifyNoOtherCalls();
        libraryManager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RefreshChapterData_WhenClearChaptersFailsForItem_ProcessesNextItem()
    {
        var failingItemId = Guid.NewGuid();
        var nextItemId = Guid.NewGuid();
        var failingItem = new Movie { Id = failingItemId, Path = @"C:\media\failing.mkv" };
        var nextItem = new Movie { Id = nextItemId, Path = @"C:\media\next.mkv" };
        var failure = new InvalidOperationException("clear failed");

        var mediaSegmentManager = new Mock<IMediaSegmentManager>(MockBehavior.Strict);
        mediaSegmentManager
            .Setup(manager => manager.GetSegmentsAsync(failingItem, null, It.IsAny<LibraryOptions>(), true))
            .ReturnsAsync(new List<MediaSegmentDto>());
        mediaSegmentManager
            .Setup(manager => manager.GetSegmentsAsync(nextItem, null, It.IsAny<LibraryOptions>(), true))
            .ReturnsAsync(CreateSegments(nextItemId));

        var chapterFileManager = new Mock<IChapterFileManager>(MockBehavior.Strict);
        var chapterOutputService = new Mock<IChapterOutputService>(MockBehavior.Strict);
        chapterOutputService
            .Setup(service => service.ClearChaptersAsync(failingItemId, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask(Task.FromException(failure)));
        chapterOutputService
            .Setup(service => service.ProcessChaptersAsync(
                It.Is<KeyValuePair<Guid, List<MediaSegmentDto>>>(segments => segments.Key == nextItemId && segments.Value.Count == 2),
                true,
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        libraryManager
            .Setup(manager => manager.GetItemById(failingItemId))
            .Returns(failingItem);
        libraryManager
            .Setup(manager => manager.GetItemById(nextItemId))
            .Returns(nextItem);
        var logger = new ListLogger<ChapterController>();

        var sut = new ChapterController(
            mediaSegmentManager.Object,
            chapterFileManager.Object,
            chapterOutputService.Object,
            libraryManager.Object,
            logger);

        await sut.RefreshChapterData([failingItemId, nextItemId], CancellationToken.None);

        Assert.Contains(
            logger.Entries,
            entry => entry.LogLevel == LogLevel.Error &&
                entry.Message.Contains(failingItemId.ToString(), StringComparison.Ordinal) &&
                ReferenceEquals(entry.Exception, failure));
        libraryManager.Verify(manager => manager.GetItemById(failingItemId), Times.Once);
        libraryManager.Verify(manager => manager.GetItemById(nextItemId), Times.Once);
        mediaSegmentManager.Verify(manager => manager.GetSegmentsAsync(failingItem, null, It.IsAny<LibraryOptions>(), true), Times.Once);
        mediaSegmentManager.Verify(manager => manager.GetSegmentsAsync(nextItem, null, It.IsAny<LibraryOptions>(), true), Times.Once);
        chapterOutputService.Verify(service => service.ClearChaptersAsync(failingItemId, It.IsAny<CancellationToken>()), Times.Once);
        chapterOutputService.Verify(service => service.ProcessChaptersAsync(
            It.Is<KeyValuePair<Guid, List<MediaSegmentDto>>>(segments => segments.Key == nextItemId && segments.Value.Count == 2),
            true,
            It.IsAny<CancellationToken>()), Times.Once);
        chapterFileManager.VerifyNoOtherCalls();
        chapterOutputService.VerifyNoOtherCalls();
        mediaSegmentManager.VerifyNoOtherCalls();
        libraryManager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RefreshChapterData_WhenProcessChaptersFailsForItem_ProcessesNextItem()
    {
        var failingItemId = Guid.NewGuid();
        var nextItemId = Guid.NewGuid();
        var failingItem = new Movie { Id = failingItemId, Path = @"C:\media\failing.mkv" };
        var nextItem = new Movie { Id = nextItemId, Path = @"C:\media\next.mkv" };
        var failure = new InvalidOperationException("process failed");

        var mediaSegmentManager = new Mock<IMediaSegmentManager>(MockBehavior.Strict);
        mediaSegmentManager
            .Setup(manager => manager.GetSegmentsAsync(failingItem, null, It.IsAny<LibraryOptions>(), true))
            .ReturnsAsync(CreateSegments(failingItemId));
        mediaSegmentManager
            .Setup(manager => manager.GetSegmentsAsync(nextItem, null, It.IsAny<LibraryOptions>(), true))
            .ReturnsAsync(CreateSegments(nextItemId));

        var chapterFileManager = new Mock<IChapterFileManager>(MockBehavior.Strict);
        var chapterOutputService = new Mock<IChapterOutputService>(MockBehavior.Strict);
        chapterOutputService
            .Setup(service => service.ProcessChaptersAsync(
                It.Is<KeyValuePair<Guid, List<MediaSegmentDto>>>(segments => segments.Key == failingItemId && segments.Value.Count == 2),
                true,
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask(Task.FromException(failure)));
        chapterOutputService
            .Setup(service => service.ProcessChaptersAsync(
                It.Is<KeyValuePair<Guid, List<MediaSegmentDto>>>(segments => segments.Key == nextItemId && segments.Value.Count == 2),
                true,
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        libraryManager
            .Setup(manager => manager.GetItemById(failingItemId))
            .Returns(failingItem);
        libraryManager
            .Setup(manager => manager.GetItemById(nextItemId))
            .Returns(nextItem);
        var logger = new ListLogger<ChapterController>();

        var sut = new ChapterController(
            mediaSegmentManager.Object,
            chapterFileManager.Object,
            chapterOutputService.Object,
            libraryManager.Object,
            logger);

        await sut.RefreshChapterData([failingItemId, nextItemId], CancellationToken.None);

        Assert.Contains(
            logger.Entries,
            entry => entry.LogLevel == LogLevel.Error &&
                entry.Message.Contains(failingItemId.ToString(), StringComparison.Ordinal) &&
                ReferenceEquals(entry.Exception, failure));
        libraryManager.Verify(manager => manager.GetItemById(failingItemId), Times.Once);
        libraryManager.Verify(manager => manager.GetItemById(nextItemId), Times.Once);
        mediaSegmentManager.Verify(manager => manager.GetSegmentsAsync(failingItem, null, It.IsAny<LibraryOptions>(), true), Times.Once);
        mediaSegmentManager.Verify(manager => manager.GetSegmentsAsync(nextItem, null, It.IsAny<LibraryOptions>(), true), Times.Once);
        chapterOutputService.Verify(service => service.ProcessChaptersAsync(
            It.Is<KeyValuePair<Guid, List<MediaSegmentDto>>>(segments => segments.Key == failingItemId && segments.Value.Count == 2),
            true,
            It.IsAny<CancellationToken>()), Times.Once);
        chapterOutputService.Verify(service => service.ProcessChaptersAsync(
            It.Is<KeyValuePair<Guid, List<MediaSegmentDto>>>(segments => segments.Key == nextItemId && segments.Value.Count == 2),
            true,
            It.IsAny<CancellationToken>()), Times.Once);
        chapterFileManager.VerifyNoOtherCalls();
        chapterOutputService.VerifyNoOtherCalls();
        mediaSegmentManager.VerifyNoOtherCalls();
        libraryManager.VerifyNoOtherCalls();
    }

    private static List<MediaSegmentDto> CreateSegments(Guid itemId)
    {
        return
        [
            new MediaSegmentDto { ItemId = itemId, StartTicks = 20, EndTicks = 30 },
            new MediaSegmentDto { ItemId = itemId, StartTicks = 10, EndTicks = 15 }
        ];
    }
}
