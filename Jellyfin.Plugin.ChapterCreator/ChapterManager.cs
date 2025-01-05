using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ChapterCreator
{
    /// <summary>
    /// ChapterManager class.
    /// </summary>
    public static class ChapterManager
    {
        private static ILogger? _logger;

        /// <summary>
        /// Initialize ChapterManager with a logger.
        /// </summary>
        /// <param name="logger">ILogger.</param>
        public static void Initialize(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Logs the configuration that will be used during Chapter file creation.
        /// </summary>
        public static void LogConfiguration()
        {
            if (_logger is null)
            {
                throw new InvalidOperationException("Logger must not be null");
            }

            var config = Plugin.Instance!.Configuration;

            _logger.LogDebug("Overwrite XML files: {Regenerate}", config.OverwriteFiles);
            _logger.LogDebug("Max Parallelism: {Action}", config.MaxParallelism);
        }

        /// <summary>
        /// Update Chapter file for the provided segments.
        /// </summary>
        /// <param name="psegment">Key value pair of segments dictionary.</param>
        /// <param name="forceOverwrite">Force the file overwrite.</param>
        public static void UpdateChapterFile(KeyValuePair<Guid, List<MediaSegmentDto>> psegment, bool forceOverwrite)
        {
            var overwrite = Plugin.Instance!.Configuration.OverwriteFiles || forceOverwrite;
            var id = psegment.Key;
            var segments = psegment.Value;

            var chapterContent = ToChapter(id, segments.AsReadOnly());

            // Test if we generated data
            if (!string.IsNullOrEmpty(chapterContent))
            {
                var filePath = Plugin.Instance!.GetItemPath(id);

                // guard for missing media file/folder.
                if (File.Exists(filePath))
                {
                    var chapterPath = GetChapterPath(filePath);
                    var fexists = File.Exists(chapterPath);

                    // User may not want an override
                    if (!fexists || (fexists && overwrite))
                    {
                        var oldContent = string.Empty;
                        var update = false;

                        try
                        {
                            oldContent = File.ReadAllText(chapterPath);
                        }
                        catch (Exception)
                        {
                        }

                        // check if we need to update
                        if (!string.IsNullOrEmpty(oldContent) && oldContent != chapterContent)
                        {
                            update = true;
                        }

                        if (!fexists || update)
                        {
                            _logger?.LogDebug("{Action} Chapter file '{File}'", update ? "Overwrite/Update" : "Create", chapterPath);
                            File.WriteAllText(chapterPath, chapterContent);
                        }
                    }
                    else
                    {
                        _logger?.LogDebug("Chapter File exists, but overwrite is disabled: '{File}'", chapterPath);
                    }
                }
            }
            else
            {
                _logger?.LogDebug("Skip id ({Id}) no chapter data generated", id);
            }
        }

        /// <summary>
        /// Convert segments to a Kodi compatible Chapter entry.
        /// </summary>
        /// <param name="id">The ItemId.</param>
        /// <param name="segments">The Segments.</param>
        /// <returns>String content of chapter file.</returns>
        public static string ToChapter(Guid id, ReadOnlyCollection<MediaSegmentDto> segments)
        {
            if (segments is null || segments.Count == 0)
            {
                return string.Empty;
            }

            // Get episode runtime from item
            var item = Plugin.Instance?.GetItem(id);
            var runtime = item?.RunTimeTicks ?? 0;

            var fstring = string.Empty;
            var chapterNumber = 1;  // Initialize counter
            MediaSegmentDto? previousSegment = null;
            var maxGap = 10_000_000 * Plugin.Instance!.Configuration.MaxGap;

            var hasSeenIntro = false;
            var hasSeenOutro = false;

            foreach (var segment in segments)
            {
                // Check for gap between segments
                var gap = segment.StartTicks - (previousSegment?.EndTicks ?? 0);
                if (gap >= maxGap)
                {
                    // Name the placeholder chapter based on position
                    var placeholderName = !hasSeenIntro ? "Prologue" :
                                        hasSeenOutro ? "Epilogue" :
                                        "Main";

                    fstring += ToChapterString(previousSegment?.EndTicks ?? 0, segment.StartTicks, placeholderName, chapterNumber);
                    chapterNumber++;
                }

                // Update intro/outro flags before processing the segment
                if (segment.Type == MediaSegmentType.Intro)
                {
                    hasSeenIntro = true;
                }
                else if (segment.Type == MediaSegmentType.Outro)
                {
                    hasSeenOutro = true;
                }

                var name = GetChapterName(segment.Type);

                // Only add chapter if it has a name
                if (!string.IsNullOrEmpty(name))
                {
                    fstring += ToChapterString(segment.StartTicks, segment.EndTicks, name, chapterNumber);
                    chapterNumber++;
                }

                // Add final chapter if there's significant runtime remaining
                if (runtime > 0 && segment == segments[^1] && runtime - segment.EndTicks >= maxGap)
                {
                    var placeholderName = hasSeenOutro ? "Epilogue" : "Main";
                    fstring += ToChapterString(segment.EndTicks, runtime, placeholderName, chapterNumber);
                    chapterNumber++;
                }

                previousSegment = segment;
            }

            // remove last newline
            var newlineInd = fstring.LastIndexOf('\n');
            return newlineInd > 0 ? fstring.Substring(0, newlineInd) : fstring;
        }

        /// <summary>
        /// Create Chapter string based on Action with newline. Public for tests.
        /// </summary>
        /// <param name="start">Start position.</param>
        /// <param name="end">End position.</param>
        /// <param name="name">The Chapter Name.</param>
        /// <param name="chapterNumber">The Chapter Number.</param>
        /// <returns>String content of chapter file.</returns>
        public static string ToChapterString(long start, long end, string name, int chapterNumber)
        {
            // Convert ticks to TimeSpan for easier formatting
            var startTime = TimeSpan.FromTicks(start);

            // Format as HH:MM:SS.ss using invariant culture for consistent formatting
            var startFormatted = startTime.ToString(@"hh\:mm\:ss\.ff", System.Globalization.CultureInfo.InvariantCulture);
            // Format as MKV chapter entry using provided chapter number
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "CHAPTER{0}={1}\nCHAPTER{0}NAME={2}\n",
                chapterNumber,
                startFormatted,
                name);
        }

        /// <summary>
        /// Convert a segments Type to an edl Action based on user settings.
        /// </summary>
        /// <param name="type">The Segments type.</param>
        /// <returns>String content of chapter file.</returns>
        private static string GetChapterName(MediaSegmentType type)
        {
            return type switch
            {
                MediaSegmentType.Intro => Plugin.Instance!.Configuration.Intro,
                MediaSegmentType.Outro => Plugin.Instance!.Configuration.Outro,
                MediaSegmentType.Recap => Plugin.Instance!.Configuration.Recap,
                MediaSegmentType.Preview => Plugin.Instance!.Configuration.Preview,
                MediaSegmentType.Commercial => Plugin.Instance!.Configuration.Commercial,
                _ => Plugin.Instance!.Configuration.Unknown,
            };
        }

        /// <summary>
        /// Given the path to an episode, return the path to the associated chapters Chapter file.
        /// </summary>
        /// <param name="mediaPath">Full path to episode.</param>
        /// <returns>Full path to chapters Chapter file.</returns>
        public static string GetChapterPath(string mediaPath)
        {
            var filename = Path.GetFileNameWithoutExtension(mediaPath);
            return Path.Combine(Path.GetDirectoryName(mediaPath)!, $"{filename}_chapter.txt");
        }
    }
}
