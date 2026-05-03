using System;
using System.Collections.ObjectModel;
using System.Linq;
using ChapterCreator.Configuration;
using ChapterCreator.Data;
using ChapterCreator.Managers;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ChapterCreator.Tests;

public class TestChapter
{
    private readonly TestableChapterFileManager _chapterFileManager;

    public TestChapter()
    {
        // Create a real ChapterFileManager with a null logger and a test configuration
        var config = new PluginConfiguration
        {
            Intro = "Intro",
            Outro = "Outro",
            Recap = "Recap",
            Preview = "Preview",
            Commercial = "Commercial",
            Unknown = "Unknown",
            // Disable gap detection for tests
            MaxGap = 0
        };

        _chapterFileManager = new TestableChapterFileManager(
            NullLogger<ChapterFileManager>.Instance,
            Mock.Of<ILibraryManager>(),
            Mock.Of<IChapterRepository>(),
            config);
    }

    [Theory]
    [InlineData(53000000, 71000000, MediaSegmentType.Recap,
        "CHAPTER1=00:00:05.30\nCHAPTER1NAME=Recap")]
    [InlineData(150000000, 167000000, MediaSegmentType.Intro,
        "CHAPTER1=00:00:15.00\nCHAPTER1NAME=Intro")]
    [InlineData(4200000000, 8220000000, MediaSegmentType.Commercial,
        "CHAPTER1=00:07:00.00\nCHAPTER1NAME=Commercial")]
    [InlineData(10000009, 2553000000, MediaSegmentType.Outro,
        "CHAPTER1=00:00:01.00\nCHAPTER1NAME=Outro")]
    [InlineData(11234568, 56546475, MediaSegmentType.Preview,
        "CHAPTER1=00:00:01.12\nCHAPTER1NAME=Preview")]
    public void TestChapterSerialization(long start, long end, MediaSegmentType type, string expected)
    {
        var segments = new ReadOnlyCollection<MediaSegmentDto>(
        [
            new MediaSegmentDto
            {
                StartTicks = start,
                EndTicks = end,
                Type = type
            }
        ]);

        var chapters = _chapterFileManager.ToChapter(Guid.NewGuid(), segments);

        // Find the chapter with the matching type
        var chapter = chapters.FirstOrDefault(c => c.Title == type.ToString());
        if (chapter == null)
        {
            // If not found by type name, find by start time
            var expectedStartTime = expected.Split('\n')[0].Split('=')[1];
            chapter = chapters.FirstOrDefault(c => c.StartTime == expectedStartTime);
        }

        Assert.NotNull(chapter);

        // Format the chapter as expected
        var actual = $"CHAPTER1={chapter.StartTime}\nCHAPTER1NAME={chapter.Title}";

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Chapter_WithSameValues_IsValueEqual()
    {
        var left = new Chapter
        {
            StartTime = "00:00:01.00",
            EndTime = "00:00:02.00",
            Title = "Intro"
        };

        var right = new Chapter
        {
            StartTime = "00:00:01.00",
            EndTime = "00:00:02.00",
            Title = "Intro"
        };

        Assert.Equal(left, right);
    }

    [Fact]
    public void ToChapter_WhenOnlyIntroExists_AddsTrailingMainChapter()
    {
        var id = Guid.NewGuid();
        var runtime = 60_000_000;
        _chapterFileManager.SetRuntimeTicks(id, runtime);

        var chapters = _chapterFileManager.ToChapter(id, new ReadOnlyCollection<MediaSegmentDto>(
        [
            new MediaSegmentDto
            {
                StartTicks = 10_000_000,
                EndTicks = 20_000_000,
                Type = MediaSegmentType.Intro
            }
        ]));

        var trailingChapter = Assert.Single(chapters, chapter => chapter.Title == "Main");
        Assert.Equal("00:00:02.00", trailingChapter.StartTime);
        Assert.Equal("00:00:06.00", trailingChapter.EndTime);
    }

    [Fact]
    public void ToChapter_WhenOutroIsLast_AddsTrailingEpilogueChapter()
    {
        var id = Guid.NewGuid();
        var runtime = 60_000_000;
        _chapterFileManager.SetRuntimeTicks(id, runtime);

        var chapters = _chapterFileManager.ToChapter(id, new ReadOnlyCollection<MediaSegmentDto>(
        [
            new MediaSegmentDto
            {
                StartTicks = 40_000_000,
                EndTicks = 50_000_000,
                Type = MediaSegmentType.Outro
            }
        ]));

        var trailingChapter = Assert.Single(chapters, chapter => chapter.Title == "Epilogue");
        Assert.Equal("00:00:05.00", trailingChapter.StartTime);
        Assert.Equal("00:00:06.00", trailingChapter.EndTime);
    }

    [Fact]
    public void ToChapter_WhenLastSegmentEndsAtRuntime_DoesNotAddTrailingChapter()
    {
        var id = Guid.NewGuid();
        var runtime = 20_000_000;
        _chapterFileManager.SetRuntimeTicks(id, runtime);

        var chapters = _chapterFileManager.ToChapter(id, new ReadOnlyCollection<MediaSegmentDto>(
        [
            new MediaSegmentDto
            {
                StartTicks = 10_000_000,
                EndTicks = 20_000_000,
                Type = MediaSegmentType.Intro
            }
        ]));

        // No trailing chapter should be added when the last segment ends exactly at the runtime.
        // A Prologue chapter before the Intro is expected, but nothing should start at or after
        // the last segment's end time (00:00:02.00 = runtime).
        Assert.DoesNotContain(chapters, chapter => chapter.StartTime == "00:00:02.00");
    }

    private sealed class TestableChapterFileManager(
        ILogger<ChapterFileManager> logger,
        ILibraryManager libraryManager,
        IChapterRepository chapterRepository,
        PluginConfiguration configuration)
        : ChapterFileManager(logger, libraryManager, chapterRepository, CreateConfigurationAccessor(configuration).Object)
    {
        private readonly Collection<(Guid Id, long RuntimeTicks)> _runtimes = [];

        public void SetRuntimeTicks(Guid id, long runtimeTicks)
        {
            _runtimes.Add((id, runtimeTicks));
        }

        protected override long GetRuntimeTicks(Guid id)
        {
            var runtime = _runtimes.LastOrDefault(entry => entry.Id == id);
            return runtime == default ? 0 : runtime.RuntimeTicks;
        }

        private static Mock<IPluginConfigurationAccessor> CreateConfigurationAccessor(PluginConfiguration configuration)
        {
            var configurationAccessor = new Mock<IPluginConfigurationAccessor>(MockBehavior.Strict);
            configurationAccessor
                .Setup(accessor => accessor.GetConfiguration())
                .Returns(configuration);
            return configurationAccessor;
        }
    }
}
