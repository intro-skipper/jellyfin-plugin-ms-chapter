using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using ChapterCreator.Configuration;
using ChapterCreator.Data;
using ChapterCreator.Managers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ChapterCreator.Tests;

public class ChapterOutputServiceTests
{
    [Fact]
    public async Task ImportFromXmlAsync_WhenXmlFileIsMissing_ReturnsQuietly()
    {
        var itemId = Guid.NewGuid();
        var mediaPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mkv");
        var chapterFileManager = new Mock<IChapterFileManager>(MockBehavior.Strict);
        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        libraryManager
            .Setup(manager => manager.GetItemById(itemId))
            .Returns(new Video { Id = itemId, Path = mediaPath });

        var jellyfinChapterManager = new Mock<MediaBrowser.Controller.Chapters.IChapterManager>(MockBehavior.Strict);
        var sut = new ChapterOutputService(
            chapterFileManager.Object,
            jellyfinChapterManager.Object,
            libraryManager.Object,
            NullLogger<ChapterOutputService>.Instance,
            CreateConfigurationAccessor(new PluginConfiguration
            {
                ImportXmlChapters = true,
                OutputMode = ChapterOutputMode.InjectOnly
            }).Object);

        await sut.ImportFromXmlAsync(itemId, CancellationToken.None);

        libraryManager.Verify(manager => manager.GetItemById(itemId), Times.Once);
        libraryManager.VerifyNoOtherCalls();
        chapterFileManager.VerifyNoOtherCalls();
        jellyfinChapterManager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ImportFromXmlAsync_WhenXmlIsInvalid_LogsAndRethrowsXmlException()
    {
        var itemId = Guid.NewGuid();
        var tempDir = Directory.CreateTempSubdirectory();

        try
        {
            var mediaPath = Path.Combine(tempDir.FullName, "episode.mkv");
            var chapterDir = Path.Combine(tempDir.FullName, Constants.ChaptersDirectory);
            Directory.CreateDirectory(chapterDir);
            var chapterXmlPath = Path.Combine(chapterDir, $"episode{Constants.ChapterFileSuffix}.xml");
            await File.WriteAllTextAsync(chapterXmlPath, "<Chapters><EditionEntry><ChapterAtom>");

            var logger = new ListLogger<ChapterOutputService>();
            var chapterFileManager = new Mock<IChapterFileManager>(MockBehavior.Strict);
            var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
            libraryManager
                .Setup(manager => manager.GetItemById(itemId))
                .Returns(new Video { Id = itemId, Path = mediaPath });

            var jellyfinChapterManager = new Mock<MediaBrowser.Controller.Chapters.IChapterManager>(MockBehavior.Strict);
            var sut = new ChapterOutputService(
                chapterFileManager.Object,
                jellyfinChapterManager.Object,
                libraryManager.Object,
                logger,
                CreateConfigurationAccessor(new PluginConfiguration
                {
                    ImportXmlChapters = true,
                    OutputMode = ChapterOutputMode.InjectOnly
                }).Object);

            await Assert.ThrowsAsync<XmlException>(async () =>
                await sut.ImportFromXmlAsync(itemId, CancellationToken.None));

            Assert.Contains(
                logger.Entries,
                entry => entry.LogLevel == LogLevel.Error &&
                    entry.Message.Contains(chapterXmlPath, StringComparison.Ordinal) &&
                    entry.Message.Contains(itemId.ToString(), StringComparison.Ordinal) &&
                    entry.Exception is XmlException);
            libraryManager.Verify(manager => manager.GetItemById(itemId), Times.Once);
            chapterFileManager.VerifyNoOtherCalls();
            jellyfinChapterManager.VerifyNoOtherCalls();
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ImportFromXmlAsync_WhenSaveChaptersFails_LogsAndRethrows()
    {
        var itemId = Guid.NewGuid();
        var expected = new InvalidOperationException("save failed");
        var tempDir = Directory.CreateTempSubdirectory();

        try
        {
            var mediaPath = Path.Combine(tempDir.FullName, "episode.mkv");
            var chapterDir = Path.Combine(tempDir.FullName, Constants.ChaptersDirectory);
            Directory.CreateDirectory(chapterDir);
            var chapterXmlPath = Path.Combine(chapterDir, $"episode{Constants.ChapterFileSuffix}.xml");
            await File.WriteAllTextAsync(
                chapterXmlPath,
                "<Chapters><EditionEntry><ChapterAtom><ChapterTimeStart>00:00:05</ChapterTimeStart><ChapterDisplay><ChapterString>Intro</ChapterString></ChapterDisplay></ChapterAtom></EditionEntry></Chapters>");

            var logger = new ListLogger<ChapterOutputService>();
            var chapterFileManager = new Mock<IChapterFileManager>(MockBehavior.Strict);
            var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
            libraryManager
                .Setup(manager => manager.GetItemById(itemId))
                .Returns(new Video { Id = itemId, Path = mediaPath });

            var jellyfinChapterManager = new Mock<MediaBrowser.Controller.Chapters.IChapterManager>(MockBehavior.Strict);
            jellyfinChapterManager
                .Setup(manager => manager.SaveChapters(
                    It.IsAny<Video>(),
                    It.IsAny<List<MediaBrowser.Model.Entities.ChapterInfo>>()))
                .Throws(expected);

            var sut = new ChapterOutputService(
                chapterFileManager.Object,
                jellyfinChapterManager.Object,
                libraryManager.Object,
                logger,
                CreateConfigurationAccessor(new PluginConfiguration
                {
                    ImportXmlChapters = true,
                    OutputMode = ChapterOutputMode.InjectOnly
                }).Object);

            var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await sut.ImportFromXmlAsync(itemId, CancellationToken.None));

            Assert.Same(expected, actual);
            Assert.Contains(
                logger.Entries,
                entry => entry.LogLevel == LogLevel.Error &&
                    entry.Message.Contains(chapterXmlPath, StringComparison.Ordinal) &&
                    entry.Message.Contains(itemId.ToString(), StringComparison.Ordinal) &&
                    ReferenceEquals(entry.Exception, expected));
            libraryManager.Verify(manager => manager.GetItemById(itemId), Times.Once);
            jellyfinChapterManager.Verify(
                manager => manager.SaveChapters(
                    It.IsAny<Video>(),
                    It.IsAny<List<MediaBrowser.Model.Entities.ChapterInfo>>()),
                Times.Once);
            chapterFileManager.VerifyNoOtherCalls();
            jellyfinChapterManager.VerifyNoOtherCalls();
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ProcessChaptersAsync_ThrowsWhenXmlWriteFailsInXmlOnly()
    {
        var itemId = Guid.NewGuid();
        var expected = new InvalidOperationException("xml failed");
        var chapterFileManager = new Mock<IChapterFileManager>(MockBehavior.Strict);
        chapterFileManager
            .Setup(manager => manager.UpdateChapterFile(It.IsAny<KeyValuePair<Guid, List<MediaSegmentDto>>>(), false))
            .Throws(expected);

        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        var jellyfinChapterManager = new Mock<MediaBrowser.Controller.Chapters.IChapterManager>(MockBehavior.Strict);
        var sut = CreateSubject(ChapterOutputMode.XmlOnly, chapterFileManager, jellyfinChapterManager, libraryManager);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sut.ProcessChaptersAsync(CreateSegments(itemId), false, CancellationToken.None));

        Assert.Same(expected, actual);
        chapterFileManager.Verify(manager => manager.UpdateChapterFile(It.IsAny<KeyValuePair<Guid, List<MediaSegmentDto>>>(), false), Times.Once);
        libraryManager.VerifyNoOtherCalls();
        jellyfinChapterManager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessChaptersAsync_ThrowsWhenDbInjectionFailsInInjectOnly()
    {
        var itemId = Guid.NewGuid();
        var expected = new InvalidOperationException("db failed");
        var chapterFileManager = new Mock<IChapterFileManager>(MockBehavior.Strict);
        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        libraryManager
            .Setup(manager => manager.GetItemById(itemId))
            .Throws(expected);

        var jellyfinChapterManager = new Mock<MediaBrowser.Controller.Chapters.IChapterManager>(MockBehavior.Strict);
        var sut = CreateSubject(ChapterOutputMode.InjectOnly, chapterFileManager, jellyfinChapterManager, libraryManager);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sut.ProcessChaptersAsync(CreateSegments(itemId), false, CancellationToken.None));

        Assert.Same(expected, actual);
        libraryManager.Verify(manager => manager.GetItemById(itemId), Times.Once);
        chapterFileManager.VerifyNoOtherCalls();
        jellyfinChapterManager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessChaptersAsync_InBothMode_AggregatesFailuresAndStillRunsDbInjection()
    {
        var itemId = Guid.NewGuid();
        var xmlFailure = new InvalidOperationException("xml failed");
        var dbFailure = new InvalidOperationException("db failed");
        var chapterFileManager = new Mock<IChapterFileManager>(MockBehavior.Strict);
        chapterFileManager
            .Setup(manager => manager.UpdateChapterFile(It.IsAny<KeyValuePair<Guid, List<MediaSegmentDto>>>(), false))
            .Throws(xmlFailure);

        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        libraryManager
            .Setup(manager => manager.GetItemById(itemId))
            .Throws(dbFailure);

        var jellyfinChapterManager = new Mock<MediaBrowser.Controller.Chapters.IChapterManager>(MockBehavior.Strict);
        var sut = CreateSubject(ChapterOutputMode.Both, chapterFileManager, jellyfinChapterManager, libraryManager);

        var aggregate = await Assert.ThrowsAsync<AggregateException>(async () =>
            await sut.ProcessChaptersAsync(CreateSegments(itemId), false, CancellationToken.None));

        Assert.Collection(
            aggregate.InnerExceptions,
            exception => Assert.Same(xmlFailure, exception),
            exception => Assert.Same(dbFailure, exception));
        chapterFileManager.Verify(manager => manager.UpdateChapterFile(It.IsAny<KeyValuePair<Guid, List<MediaSegmentDto>>>(), false), Times.Once);
        libraryManager.Verify(manager => manager.GetItemById(itemId), Times.Once);
        jellyfinChapterManager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessChaptersAsync_InBothMode_WhenDbInjectionAloneFails_PreservesOriginalStackTrace()
    {
        var itemId = Guid.NewGuid();
        var chapterFileManager = new Mock<IChapterFileManager>(MockBehavior.Strict);
        chapterFileManager
            .Setup(manager => manager.UpdateChapterFile(It.IsAny<KeyValuePair<Guid, List<MediaSegmentDto>>>(), false));

        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        libraryManager
            .Setup(manager => manager.GetItemById(itemId))
            .Returns(() => ThrowDbFailure());

        var jellyfinChapterManager = new Mock<MediaBrowser.Controller.Chapters.IChapterManager>(MockBehavior.Strict);
        var sut = CreateSubject(ChapterOutputMode.Both, chapterFileManager, jellyfinChapterManager, libraryManager);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await sut.ProcessChaptersAsync(CreateSegments(itemId), false, CancellationToken.None));

        Assert.Contains(nameof(ThrowDbFailure), actual.StackTrace, StringComparison.Ordinal);
        chapterFileManager.Verify(manager => manager.UpdateChapterFile(It.IsAny<KeyValuePair<Guid, List<MediaSegmentDto>>>(), false), Times.Once);
        libraryManager.Verify(manager => manager.GetItemById(itemId), Times.Once);
        jellyfinChapterManager.VerifyNoOtherCalls();
    }

    private static ChapterOutputService CreateSubject(
        ChapterOutputMode outputMode,
        Mock<IChapterFileManager> chapterFileManager,
        Mock<MediaBrowser.Controller.Chapters.IChapterManager> jellyfinChapterManager,
        Mock<ILibraryManager> libraryManager)
    {
        return new ChapterOutputService(
            chapterFileManager.Object,
            jellyfinChapterManager.Object,
            libraryManager.Object,
            NullLogger<ChapterOutputService>.Instance,
            CreateConfigurationAccessor(new PluginConfiguration { OutputMode = outputMode }).Object);
    }

    private static Mock<IPluginConfigurationAccessor> CreateConfigurationAccessor(PluginConfiguration configuration)
    {
        var configurationAccessor = new Mock<IPluginConfigurationAccessor>(MockBehavior.Strict);
        configurationAccessor
            .Setup(accessor => accessor.GetConfiguration())
            .Returns(configuration);
        return configurationAccessor;
    }

    private static KeyValuePair<Guid, List<MediaSegmentDto>> CreateSegments(Guid itemId)
    {
        return new KeyValuePair<Guid, List<MediaSegmentDto>>(
            itemId,
            [
                new MediaSegmentDto
                {
                    StartTicks = 0,
                    EndTicks = 10_000_000,
                    Type = Jellyfin.Database.Implementations.Enums.MediaSegmentType.Intro
                }
            ]);
    }

    private static BaseItem ThrowDbFailure()
    {
        throw new InvalidOperationException("db failed");
    }
}
