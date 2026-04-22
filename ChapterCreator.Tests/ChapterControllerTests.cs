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
            libraryManager.Object);

        await sut.RefreshChapterData([itemId], CancellationToken.None);

        libraryManager.Verify(manager => manager.GetItemById(itemId), Times.Once);
        mediaSegmentManager.Verify(manager => manager.GetSegmentsAsync(item, null, It.IsAny<LibraryOptions>(), true), Times.Once);
        chapterOutputService.Verify(service => service.ClearChaptersAsync(itemId, It.IsAny<CancellationToken>()), Times.Once);
        chapterFileManager.VerifyNoOtherCalls();
        chapterOutputService.VerifyNoOtherCalls();
        mediaSegmentManager.VerifyNoOtherCalls();
        libraryManager.VerifyNoOtherCalls();
    }
}
