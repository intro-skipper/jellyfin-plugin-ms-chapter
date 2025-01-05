using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using ChapterCreator.Data;
using ChapterCreator.Helper;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.Logging;

namespace ChapterCreator
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

            _logger.LogDebug("Overwrite Chapter files: {Regenerate}", config.OverwriteFiles);
            _logger.LogDebug("Max Parallelism: {Action}", config.MaxParallelism);
        }

        /// <summary>
        /// Update Chapter file for the provided segments.
        /// </summary>
        /// <param name="psegment">Key value pair of segments dictionary.</param>
        /// <param name="forceOverwrite">Force the file overwrite.</param>
        public static void UpdateChapterFile(KeyValuePair<Guid, List<MediaSegmentDto>> psegment, bool forceOverwrite)
        {
            ArgumentNullException.ThrowIfNull(psegment.Value);

            var id = psegment.Key;
            var segments = psegment.Value;
            var overwrite = Plugin.Instance!.Configuration.OverwriteFiles || forceOverwrite;

            _logger?.LogDebug("Processing chapters for item {Id}", id);

            try
            {
                var chapterContent = ToChapter(id, segments.AsReadOnly());

                if (chapterContent.Count == 0)
                {
                    _logger?.LogDebug("Skip id ({Id}): no chapter data generated", id);
                    return;
                }

                var filePath = Plugin.Instance!.GetItemPath(id);
                if (string.IsNullOrEmpty(filePath))
                {
                    _logger?.LogWarning("Skip id ({Id}): unable to get item path", id);
                    return;
                }

                if (!File.Exists(filePath))
                {
                    _logger?.LogWarning("Skip id ({Id}): media file not found at {Path}", id, filePath);
                    return;
                }

                var chapterPath = GetChapterPath(filePath);
                _logger?.LogDebug("Writing chapters to {Path}", chapterPath);

                MKVChapterWriter.CreateChapterXmlFile(chapterPath, chapterContent, overwrite, _logger);
                _logger?.LogDebug("Successfully created chapter file for {Id} at {Path}", id, chapterPath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to create chapter file for item {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// Convert segments to a Kodi compatible Chapter entry.
        /// </summary>
        /// <param name="id">The ItemId.</param>
        /// <param name="segments">The Segments.</param>
        /// <returns>String content of chapter file.</returns>
        public static IReadOnlyList<Chapter> ToChapter(Guid id, ReadOnlyCollection<MediaSegmentDto> segments)
        {
            if (segments is null || segments.Count == 0)
            {
                return [];
            }

            var config = Plugin.Instance!.Configuration;

            // Get episode runtime from item
            var item = Plugin.Instance?.GetItem(id);
            var runtime = item?.RunTimeTicks ?? 0;

            var chapters = new List<Chapter>();
            MediaSegmentDto? previousSegment = null;
            var maxGap = 10_000_000 * config.MaxGap;

            var hasSeenIntro = false;
            var hasSeenOutro = false;

            foreach (var segment in segments)
            {
                var isIntro = segment.Type == MediaSegmentType.Intro;
                // Check for gap between segments
                var gap = segment.StartTicks - (previousSegment?.EndTicks ?? 0);
                if (gap >= maxGap)
                {
                    // Name the placeholder chapter based on position
                    var placeholderName = isIntro && !hasSeenIntro ? config.Prologue :
                                        hasSeenOutro ? config.Epilogue :
                                        config.Main;

                    chapters.Add(new Chapter
                    {
                        StartTime = TickToTime(previousSegment?.EndTicks ?? 0),
                        EndTime = TickToTime(segment.StartTicks),
                        Title = placeholderName
                    });
                }

                // Update intro/outro flags before processing the segment
                hasSeenIntro = isIntro || hasSeenIntro;

                if (segment.Type == MediaSegmentType.Outro)
                {
                    hasSeenOutro = true;
                }

                var name = GetChapterName(segment.Type);

                // Only add chapter if it has a name
                if (!string.IsNullOrEmpty(name))
                {
                    chapters.Add(new Chapter
                    {
                        StartTime = TickToTime(segment.StartTicks),
                        EndTime = TickToTime(segment.EndTicks),
                        Title = name
                    });
                }

                // Add final chapter if there's significant runtime remaining
                if (hasSeenOutro && runtime > 0 && segment == segments[^1] && runtime - segment.EndTicks >= maxGap)
                {
                    var placeholderName = hasSeenOutro ? config.Epilogue : config.Main;
                    chapters.Add(new Chapter
                    {
                        StartTime = TickToTime(segment.EndTicks),
                        EndTime = TickToTime(runtime),
                        Title = placeholderName
                    });
                }

                previousSegment = segment;
            }

            return chapters;
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
            return Path.Combine(Path.GetDirectoryName(mediaPath)!, $"{filename}_chapter.xml");
        }

        private static string TickToTime(long ticks)
        {
            var timeSpan = TimeSpan.FromTicks(ticks);
            return timeSpan.ToString(@"hh\:mm\:ss\.ff", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
