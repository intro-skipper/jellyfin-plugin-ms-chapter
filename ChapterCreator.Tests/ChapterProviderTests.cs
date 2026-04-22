using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChapterCreator.Configuration;
using ChapterCreator.Managers;
using ChapterCreator.Providers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ChapterCreator.Tests;

public class ChapterProviderTests
{
    [Fact]
    public void HasChanged_WhenAutoRefreshAndImportXmlChaptersAreDisabled_ReturnsFalse()
    {
        using var _ = ConfigureMediaSourceManager(MediaProtocol.File);
        var chapterOutputService = new Mock<IChapterOutputService>(MockBehavior.Strict);
        var mediaSegmentManager = new Mock<IMediaSegmentManager>(MockBehavior.Strict);
        var directoryService = new Mock<IDirectoryService>(MockBehavior.Strict);
        var movie = CreateMovie();
        movie.DateLastSaved = new DateTime(2026, 4, 18, 12, 0, 0, DateTimeKind.Utc);

        directoryService
            .Setup(service => service.GetFile(movie.Path))
            .Returns(new FileSystemMetadata { LastWriteTimeUtc = movie.DateLastSaved.AddMinutes(-1) });

        var sut = CreateSubject(chapterOutputService, mediaSegmentManager, new PluginConfiguration
        {
            AutoRefresh = false,
            ImportXmlChapters = false
        });

        var hasChanged = sut.HasChanged(movie, directoryService.Object);

        Assert.False(hasChanged);
        directoryService.Verify(service => service.GetFile(movie.Path), Times.Once);
        directoryService.VerifyNoOtherCalls();
    }

    [Fact]
    public void HasChanged_WhenMediaFileIsNewerThanLastSave_ReturnsTrue()
    {
        using var _ = ConfigureMediaSourceManager(MediaProtocol.File);
        var chapterOutputService = new Mock<IChapterOutputService>(MockBehavior.Strict);
        var mediaSegmentManager = new Mock<IMediaSegmentManager>(MockBehavior.Strict);
        var directoryService = new Mock<IDirectoryService>(MockBehavior.Strict);
        var lastSaved = new DateTime(2026, 4, 18, 12, 0, 0, DateTimeKind.Utc);
        var movie = CreateMovie();
        movie.DateLastSaved = lastSaved;

        directoryService
            .Setup(service => service.GetFile(movie.Path))
            .Returns(new FileSystemMetadata { LastWriteTimeUtc = lastSaved.AddMinutes(1) });

        var sut = CreateSubject(chapterOutputService, mediaSegmentManager, new PluginConfiguration
        {
            AutoRefresh = false,
            ImportXmlChapters = true
        });

        var hasChanged = sut.HasChanged(movie, directoryService.Object);

        Assert.True(hasChanged);
        directoryService.Verify(service => service.GetFile(movie.Path), Times.Once);
        directoryService.VerifyNoOtherCalls();
    }

    [Fact]
    public void HasChanged_WhenConfiguredButNeitherMediaNorXmlChanged_ReturnsFalse()
    {
        using var _ = ConfigureMediaSourceManager(MediaProtocol.File);
        var chapterOutputService = new Mock<IChapterOutputService>(MockBehavior.Strict);
        var mediaSegmentManager = new Mock<IMediaSegmentManager>(MockBehavior.Strict);
        var directoryService = new Mock<IDirectoryService>(MockBehavior.Strict);
        var lastSaved = new DateTime(2026, 4, 18, 12, 0, 0, DateTimeKind.Utc);
        var movie = CreateMovie();
        movie.DateLastSaved = lastSaved;

        directoryService
            .Setup(service => service.GetFile(movie.Path))
            .Returns(new FileSystemMetadata { LastWriteTimeUtc = lastSaved.AddMinutes(-1) });

        var sut = CreateSubject(chapterOutputService, mediaSegmentManager, new PluginConfiguration
        {
            AutoRefresh = true,
            ImportXmlChapters = true
        });

        var hasChanged = sut.HasChanged(movie, directoryService.Object);

        Assert.False(hasChanged);
        directoryService.Verify(service => service.GetFile(movie.Path), Times.Once);
        directoryService.VerifyNoOtherCalls();
    }

    [Fact]
    public void HasChanged_WhenChapterXmlIsNewerThanLastSave_ReturnsFalse()
    {
        using var _ = ConfigureMediaSourceManager(MediaProtocol.File);
        var chapterOutputService = new Mock<IChapterOutputService>(MockBehavior.Strict);
        var mediaSegmentManager = new Mock<IMediaSegmentManager>(MockBehavior.Strict);
        var directoryService = new Mock<IDirectoryService>(MockBehavior.Strict);
        var lastSaved = new DateTime(2026, 4, 18, 12, 0, 0, DateTimeKind.Utc);
        var movie = CreateMovie();
        movie.DateLastSaved = lastSaved;

        directoryService
            .Setup(service => service.GetFile(movie.Path))
            .Returns(new FileSystemMetadata { LastWriteTimeUtc = lastSaved.AddMinutes(-1) });

        var sut = CreateSubject(chapterOutputService, mediaSegmentManager, new PluginConfiguration
        {
            AutoRefresh = false,
            ImportXmlChapters = true
        });

        var hasChanged = sut.HasChanged(movie, directoryService.Object);

        Assert.False(hasChanged);
        directoryService.Verify(service => service.GetFile(movie.Path), Times.Once);
        directoryService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FetchAsync_WhenAutoRefreshIsDisabledAndImportXmlChaptersIsEnabledWithoutSegments_ImportsXml()
    {
        var chapterOutputService = new Mock<IChapterOutputService>(MockBehavior.Strict);
        chapterOutputService
            .Setup(service => service.ImportFromXmlAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var mediaSegmentManager = new Mock<IMediaSegmentManager>(MockBehavior.Strict);
        mediaSegmentManager
            .Setup(manager => manager.GetSegmentsAsync(It.IsAny<Movie>(), null, It.IsAny<LibraryOptions>(), true))
            .ReturnsAsync(new List<MediaSegmentDto>());

        var sut = CreateSubject(chapterOutputService, mediaSegmentManager, new PluginConfiguration
        {
            AutoRefresh = false,
            ImportXmlChapters = true
        });

        var movie = CreateMovie();
        var result = await sut.FetchAsync(movie, null!, CancellationToken.None);

        Assert.Equal(ItemUpdateType.None, result);
        mediaSegmentManager.Verify(manager => manager.GetSegmentsAsync(It.IsAny<Movie>(), null, It.IsAny<LibraryOptions>(), true), Times.Once);
        chapterOutputService.Verify(service => service.ImportFromXmlAsync(movie.Id, It.IsAny<CancellationToken>()), Times.Once);
        chapterOutputService.Verify(service => service.ProcessChaptersAsync(It.IsAny<KeyValuePair<Guid, List<MediaSegmentDto>>>(), false, It.IsAny<CancellationToken>()), Times.Never);
        chapterOutputService.VerifyNoOtherCalls();
        mediaSegmentManager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FetchAsync_WhenSegmentsExistAndAutoRefreshIsDisabled_DoesNotProcessChapters()
    {
        var chapterOutputService = new Mock<IChapterOutputService>(MockBehavior.Strict);
        var mediaSegmentManager = new Mock<IMediaSegmentManager>(MockBehavior.Strict);
        mediaSegmentManager
            .Setup(manager => manager.GetSegmentsAsync(It.IsAny<Movie>(), null, It.IsAny<LibraryOptions>(), true))
            .ReturnsAsync(CreateSegments());

        var sut = CreateSubject(chapterOutputService, mediaSegmentManager, new PluginConfiguration
        {
            AutoRefresh = false,
            ImportXmlChapters = true
        });

        var result = await sut.FetchAsync(CreateMovie(), null!, CancellationToken.None);

        Assert.Equal(ItemUpdateType.None, result);
        mediaSegmentManager.Verify(manager => manager.GetSegmentsAsync(It.IsAny<Movie>(), null, It.IsAny<LibraryOptions>(), true), Times.Once);
        chapterOutputService.Verify(service => service.ImportFromXmlAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        chapterOutputService.Verify(service => service.ProcessChaptersAsync(It.IsAny<KeyValuePair<Guid, List<MediaSegmentDto>>>(), false, It.IsAny<CancellationToken>()), Times.Never);
        chapterOutputService.VerifyNoOtherCalls();
        mediaSegmentManager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FetchAsync_WhenAutoRefreshIsEnabledAndSegmentsExist_ProcessesChapters()
    {
        var chapterOutputService = new Mock<IChapterOutputService>(MockBehavior.Strict);
        chapterOutputService
            .Setup(service => service.ProcessChaptersAsync(It.IsAny<KeyValuePair<Guid, List<MediaSegmentDto>>>(), false, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var mediaSegmentManager = new Mock<IMediaSegmentManager>(MockBehavior.Strict);
        mediaSegmentManager
            .Setup(manager => manager.GetSegmentsAsync(It.IsAny<Movie>(), null, It.IsAny<LibraryOptions>(), true))
            .ReturnsAsync(CreateSegments());

        var sut = CreateSubject(chapterOutputService, mediaSegmentManager, new PluginConfiguration
        {
            AutoRefresh = true,
            ImportXmlChapters = false
        });

        var movie = CreateMovie();
        var result = await sut.FetchAsync(movie, null!, CancellationToken.None);

        Assert.Equal(ItemUpdateType.None, result);
        mediaSegmentManager.Verify(manager => manager.GetSegmentsAsync(It.IsAny<Movie>(), null, It.IsAny<LibraryOptions>(), true), Times.Once);
        chapterOutputService.Verify(service => service.ProcessChaptersAsync(
            It.Is<KeyValuePair<Guid, List<MediaSegmentDto>>>(segments => segments.Key == movie.Id && segments.Value.Count == 2),
            false,
            It.IsAny<CancellationToken>()), Times.Once);
        chapterOutputService.Verify(service => service.ImportFromXmlAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        chapterOutputService.VerifyNoOtherCalls();
        mediaSegmentManager.VerifyNoOtherCalls();
    }

    private static ChapterProvider CreateSubject(
        Mock<IChapterOutputService> chapterOutputService,
        Mock<IMediaSegmentManager> mediaSegmentManager,
        PluginConfiguration configuration)
    {
        var configurationAccessor = new Mock<IPluginConfigurationAccessor>(MockBehavior.Strict);
        configurationAccessor
            .Setup(accessor => accessor.GetConfiguration())
            .Returns(configuration);

        return new(
            chapterOutputService.Object,
            mediaSegmentManager.Object,
            NullLogger<ChapterProvider>.Instance,
            configurationAccessor.Object);
    }

    private static Movie CreateMovie(string path = @"C:\media\movie.mkv")
    {
        return new Movie { Id = Guid.NewGuid(), Path = path };
    }

    private static List<MediaSegmentDto> CreateSegments()
    {
        return
        [
            new MediaSegmentDto { StartTicks = 20, EndTicks = 40 },
            new MediaSegmentDto { StartTicks = 10, EndTicks = 15 }
        ];
    }

    private static IDisposable ConfigureMediaSourceManager(MediaProtocol mediaProtocol)
    {
        var previous = BaseItem.MediaSourceManager;
        var mediaSourceManager = new Mock<IMediaSourceManager>(MockBehavior.Strict);
        mediaSourceManager
            .Setup(manager => manager.GetPathProtocol(It.IsAny<string>()))
            .Returns(mediaProtocol);

        BaseItem.MediaSourceManager = mediaSourceManager.Object;
        return new RestoreMediaSourceManager(previous);
    }

    private sealed class RestoreMediaSourceManager(IMediaSourceManager? previous) : IDisposable
    {
        public void Dispose()
        {
            BaseItem.MediaSourceManager = previous;
        }
    }
}
