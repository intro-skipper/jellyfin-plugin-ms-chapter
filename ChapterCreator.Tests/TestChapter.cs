using System;
using System.Collections.ObjectModel;
using System.Linq;
using ChapterCreator.Configuration;
using ChapterCreator.Managers;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChapterCreator.Tests;

public class TestChapter
{
    private readonly ChapterManager _chapterManager;

    public TestChapter()
    {
        // Create a real ChapterManager with a null logger and a test configuration
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

        _chapterManager = new ChapterManager(
            NullLogger<ChapterManager>.Instance,
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

        var chapters = _chapterManager.ToChapter(Guid.NewGuid(), segments);

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
}
