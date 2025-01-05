using System;
using System.Collections.ObjectModel;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.MediaSegments;
using Xunit;

namespace Jellyfin.Plugin.ChapterCreator.Tests;

public class TestChapter
{
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

        var actual = ChapterManager.ToChapter(Guid.NewGuid(), segments);

        Assert.Equal(expected, actual);
    }
}
