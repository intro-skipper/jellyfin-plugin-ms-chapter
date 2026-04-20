using System;
using System.IO;
using System.Reflection;
using ChapterCreator.Managers;
using Xunit;

namespace ChapterCreator.Tests;

public class ChapterPathTests
{
    private const string LegacyChaptersDirectory = "chapters";
    private const string HiddenChaptersDirectory = ".chapters";
    private const string ChapterFileSuffix = "_chapters";

    [Fact]
    public void GetChapterPath_UsesHiddenDirectory_AndMigratesLegacyFolder()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"chaptercreator-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var mediaFilePath = Path.Combine(tempRoot, "Episode01.mkv");
            File.WriteAllText(mediaFilePath, string.Empty);

            var legacyChapterDirectory = Path.Combine(tempRoot, LegacyChaptersDirectory);
            Directory.CreateDirectory(legacyChapterDirectory);
            var legacyChapterFile = Path.Combine(
                legacyChapterDirectory,
                $"{Path.GetFileNameWithoutExtension(mediaFilePath)}{ChapterFileSuffix}.xml");
            File.WriteAllText(legacyChapterFile, "<Chapters />");

            var method = typeof(ChapterFileManager).GetMethod(
                "GetChapterPath",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.NotNull(method);

            var chapterPath = method!.Invoke(null, [mediaFilePath, null]) as string;
            var expectedPath = Path.Combine(
                tempRoot,
                HiddenChaptersDirectory,
                $"{Path.GetFileNameWithoutExtension(mediaFilePath)}{ChapterFileSuffix}.xml");

            Assert.Equal(expectedPath, chapterPath);
            Assert.True(File.Exists(expectedPath));
            Assert.False(Directory.Exists(legacyChapterDirectory));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
