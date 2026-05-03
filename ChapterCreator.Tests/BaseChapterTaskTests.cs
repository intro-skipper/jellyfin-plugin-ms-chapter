using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChapterCreator.Configuration;
using ChapterCreator.Managers;
using ChapterCreator.SheduledTasks;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ChapterCreator.Tests;

public class BaseChapterTaskTests
{
    [Fact]
    public async Task CreateChaptersAsync_WhenProcessChaptersFailsForGroupedItem_ProcessesRemainingItemsAndReportsProgress()
    {
        var failingItemId = Guid.NewGuid();
        var nextItemId = Guid.NewGuid();
        var failure = new InvalidOperationException("process failed");
        var progressReports = new List<double>();

        var progress = new Mock<IProgress<double>>(MockBehavior.Strict);
        progress
            .Setup(reporter => reporter.Report(It.IsAny<double>()))
            .Callback<double>(progressReports.Add);

        var chapterOutputService = new Mock<IChapterOutputService>(MockBehavior.Strict);
        chapterOutputService
            .Setup(service => service.LogConfiguration());
        chapterOutputService
            .Setup(service => service.ProcessChaptersAsync(
                It.Is<KeyValuePair<Guid, List<MediaSegmentDto>>>(segments =>
                    segments.Key == failingItemId &&
                    segments.Value.Count == 2 &&
                    segments.Value[0].StartTicks == 10),
                false,
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask(Task.FromException(failure)));
        chapterOutputService
            .Setup(service => service.ProcessChaptersAsync(
                It.Is<KeyValuePair<Guid, List<MediaSegmentDto>>>(segments =>
                    segments.Key == nextItemId &&
                    segments.Value.Count == 1 &&
                    segments.Value[0].StartTicks == 30),
                false,
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var configurationAccessor = new Mock<IPluginConfigurationAccessor>(MockBehavior.Strict);
        configurationAccessor
            .Setup(accessor => accessor.GetConfiguration())
            .Returns(new PluginConfiguration { MaxParallelism = 1 });
        var logger = new ListLogger<BaseChapterTask>();
        var sut = new BaseChapterTask(chapterOutputService.Object, configurationAccessor.Object, logger);

        await sut.CreateChaptersAsync(
            progress.Object,
            [
                new MediaSegmentDto { ItemId = failingItemId, StartTicks = 20, EndTicks = 25 },
                new MediaSegmentDto { ItemId = nextItemId, StartTicks = 30, EndTicks = 35 },
                new MediaSegmentDto { ItemId = failingItemId, StartTicks = 10, EndTicks = 15 }
            ],
            false,
            CancellationToken.None);

        Assert.Equal(new[] { 50.0, 100.0 }, progressReports);
        Assert.Contains(
            logger.Entries,
            entry => entry.LogLevel == LogLevel.Error &&
                entry.Message.Contains(failingItemId.ToString(), StringComparison.Ordinal) &&
                ReferenceEquals(entry.Exception, failure));
        chapterOutputService.Verify(service => service.LogConfiguration(), Times.Once);
        chapterOutputService.Verify(service => service.ProcessChaptersAsync(
            It.Is<KeyValuePair<Guid, List<MediaSegmentDto>>>(segments => segments.Key == failingItemId),
            false,
            It.IsAny<CancellationToken>()), Times.Once);
        chapterOutputService.Verify(service => service.ProcessChaptersAsync(
            It.Is<KeyValuePair<Guid, List<MediaSegmentDto>>>(segments => segments.Key == nextItemId),
            false,
            It.IsAny<CancellationToken>()), Times.Once);
        configurationAccessor.Verify(accessor => accessor.GetConfiguration(), Times.Once);
        progress.Verify(reporter => reporter.Report(It.IsAny<double>()), Times.Exactly(2));
        chapterOutputService.VerifyNoOtherCalls();
        configurationAccessor.VerifyNoOtherCalls();
        progress.VerifyNoOtherCalls();
    }
}
