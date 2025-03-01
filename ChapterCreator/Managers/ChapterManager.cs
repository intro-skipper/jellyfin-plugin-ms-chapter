using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using ChapterCreator.Configuration;
using ChapterCreator.Data;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.Logging;

namespace ChapterCreator.Managers;

/// <summary>
/// ChapterManager class.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ChapterManager"/> class.
/// </remarks>
/// <param name="logger">The logger instance.</param>
public class ChapterManager(ILogger<ChapterManager> logger) : IChapterManager
{
    private readonly ILogger<ChapterManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly PluginConfiguration _configuration = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ChapterManager"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="configuration">The plugin configuration. If null, will try to use Plugin.Instance.Configuration or create a new one.</param>
    public ChapterManager(ILogger<ChapterManager> logger, PluginConfiguration? configuration = null) : this(logger)
    {
        _configuration = configuration ?? Plugin.Instance?.Configuration ?? new PluginConfiguration();
    }

    /// <summary>
    /// Logs the configuration that will be used during Chapter file creation.
    /// </summary>
    public void LogConfiguration()
    {
        var config = Plugin.Instance!.Configuration;

        _logger.LogDebug("Overwrite Chapter files: {Regenerate}", config.OverwriteFiles);
        _logger.LogDebug("Max Parallelism: {Action}", config.MaxParallelism);
    }

    /// <summary>
    /// Update Chapter file for the provided segments.
    /// </summary>
    /// <param name="psegment">Key value pair of segments dictionary.</param>
    /// <param name="forceOverwrite">Force the file overwrite.</param>
    public void UpdateChapterFile(KeyValuePair<Guid, List<MediaSegmentDto>> psegment, bool forceOverwrite)
    {
        ArgumentNullException.ThrowIfNull(psegment.Value);

        var id = psegment.Key;
        var segments = psegment.Value;
        var config = Plugin.Instance!.Configuration;
        var overwrite = config.OverwriteFiles || forceOverwrite;

        var embeddedChapters = Plugin.Instance.GetChapters(id);
        if (!forceOverwrite && embeddedChapters.Count > 0 && config.SkipEmbeddedChapters)
        {
            _logger.LogDebug("Skipping item {Id} as it already has embedded chapters", id);
            return;
        }

        _logger.LogDebug("Processing chapters for item {Id}", id);

        try
        {
            var chapterContent = ToChapter(id, segments);

            if (chapterContent.Count == 0)
            {
                _logger.LogDebug("Skip id ({Id}): no chapter data generated", id);
                return;
            }

            var filePath = Plugin.Instance!.GetItemPath(id);
            if (string.IsNullOrEmpty(filePath))
            {
                _logger.LogWarning("Skip id ({Id}): unable to get item path", id);
                return;
            }

            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Skip id ({Id}): media file not found at {Path}", id, filePath);
                return;
            }

            var chapterPath = GetChapterPath(filePath);
            _logger.LogDebug("Writing chapters to {Path}", chapterPath);

            CreateChapterXmlFile(chapterPath, chapterContent, overwrite, _logger);
            _logger.LogDebug("Successfully created chapter file for {Id} at {Path}", id, chapterPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create chapter file for item {Id}", id);
            throw;
        }
    }

    /// <summary>
    /// Convert segments to a Kodi compatible Chapter entry.
    /// </summary>
    /// <param name="id">The ItemId.</param>
    /// <param name="segments">The Segments.</param>
    /// <returns>String content of chapter file.</returns>
    public IReadOnlyList<Chapter> ToChapter(Guid id, IReadOnlyCollection<MediaSegmentDto> segments)
    {
        if (segments is null || segments.Count == 0)
        {
            return [];
        }

        var config = _configuration;

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
            if (hasSeenOutro && runtime > 0 && segment == segments.Last() && runtime - segment.EndTicks >= maxGap)
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

    private string GetChapterName(MediaSegmentType type)
    {
        return type switch
        {
            MediaSegmentType.Intro => _configuration.Intro,
            MediaSegmentType.Outro => _configuration.Outro,
            MediaSegmentType.Recap => _configuration.Recap,
            MediaSegmentType.Preview => _configuration.Preview,
            MediaSegmentType.Commercial => _configuration.Commercial,
            _ => _configuration.Unknown,
        };
    }

    private static string GetChapterPath(string mediaPath) => Path.Combine(Path.GetDirectoryName(mediaPath)!, $"{Path.GetFileNameWithoutExtension(mediaPath)}_chapters.xml");

    private static string TickToTime(long ticks) => TimeSpan.FromTicks(ticks).ToString(@"hh\:mm\:ss\.ff", System.Globalization.CultureInfo.InvariantCulture);

    private static void CreateChapterXmlFile(string filename, IReadOnlyList<Chapter> chapters, bool overwrite, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(filename);

        if (chapters is null || chapters.Count == 0)
        {
            logger?.LogDebug("No chapters provided for file {Filename}", filename);
            return;
        }

        if (File.Exists(filename))
        {
            if (!overwrite)
            {
                logger?.LogDebug("Skipping existing chapter file {Filename} (overwrite disabled)", filename);
                return;
            }

            logger?.LogDebug("Overwriting existing chapter file {Filename}", filename);
        }

        var directoryPath = Path.GetDirectoryName(filename)!;
        Directory.CreateDirectory(directoryPath);
        logger?.LogDebug("Ensuring directory exists: {Directory}", directoryPath);

        // Generate random UIDs for the Edition and Chapters
        long editionUID = GenerateUID();
        long[] chapterUIDs = [.. Enumerable.Range(0, chapters.Count).Select(_ => GenerateUID())];

        logger?.LogDebug("Writing {Count} chapters to {Filename}", chapters.Count, filename);

        // Create an XML writer with appropriate settings
        XmlWriterSettings settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineOnAttributes = false
        };

        using XmlWriter writer = XmlWriter.Create(filename, settings);

        // Write the XML structure using constants for element names
        writer.WriteStartDocument();
        writer.WriteStartElement("Chapters");
        {
            writer.WriteStartElement("EditionEntry");
            {
                writer.WriteElementString("EditionUID", editionUID.ToString(System.Globalization.CultureInfo.InvariantCulture));
                writer.WriteElementString("EditionFlagDefault", "1");  // Add default flag
                writer.WriteElementString("EditionFlagHidden", "0");   // Add hidden flag

                // Write each chapter
                for (int i = 0; i < chapters.Count; i++)
                {
                    WriteChapterAtom(writer, chapters[i], chapterUIDs[i]);
                }
            }

            writer.WriteEndElement(); // End EditionEntry
        }

        writer.WriteEndElement(); // End Chapters
        writer.WriteEndDocument();
    }

    private static void WriteChapterAtom(XmlWriter writer, Chapter chapter, long chapterUID)
    {
        writer.WriteStartElement("ChapterAtom");
        {
            writer.WriteElementString("ChapterUID", chapterUID.ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteElementString("ChapterFlagHidden", "0");
            writer.WriteElementString("ChapterFlagEnabled", "1");
            writer.WriteElementString("ChapterTimeStart", chapter.StartTime);
            writer.WriteElementString("ChapterTimeEnd", chapter.EndTime);

            writer.WriteStartElement("ChapterDisplay");
            {
                writer.WriteElementString("ChapterString", chapter.Title);
                writer.WriteElementString("ChapterLanguage", "und");
            }

            writer.WriteEndElement(); // End ChapterDisplay
        }

        writer.WriteEndElement(); // End ChapterAtom
    }

    // Method to generate a random UID (Matroska recommends 64-bit unsigned integers)
    private static long GenerateUID()
    {
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        byte[] buffer = new byte[8];
        rng.GetBytes(buffer);
        return BitConverter.ToInt64(buffer, 0) & 0x7FFFFFFFFFFFFFFF; // Ensure positive number
    }
}
