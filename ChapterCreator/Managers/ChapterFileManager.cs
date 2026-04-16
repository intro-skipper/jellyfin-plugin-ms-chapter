using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using ChapterCreator.Configuration;
using ChapterCreator.Data;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.Logging;

namespace ChapterCreator.Managers;

/// <summary>
/// ChapterFileManager class.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ChapterFileManager"/> class.
/// </remarks>
/// <param name="logger">The logger instance.</param>
public partial class ChapterFileManager(ILogger<ChapterFileManager> logger) : IChapterFileManager
{
    private readonly ILogger<ChapterFileManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly PluginConfiguration? _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChapterFileManager"/> class.
    /// Used for unit testing to inject a specific configuration without requiring Plugin.Instance.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="configuration">The plugin configuration. If null, will try to use Plugin.Instance.Configuration or create a new one.</param>
    public ChapterFileManager(ILogger<ChapterFileManager> logger, PluginConfiguration? configuration) : this(logger)
    {
        _configuration = configuration;
    }

    private PluginConfiguration EffectiveConfiguration =>
        _configuration ?? Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>
    /// Logs the configuration that will be used during Chapter file creation.
    /// </summary>
    public void LogConfiguration()
    {
        var config = Plugin.Instance!.Configuration;

        LogOverwriteSetting(_logger, config.OverwriteFiles);
        LogMaxParallelism(_logger, config.MaxParallelism);
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
            LogSkippingEmbeddedChapters(_logger, id);
            return;
        }

        LogProcessingChapters(_logger, id);

        try
        {
            var chapterContent = ToChapter(id, segments);

            if (chapterContent.Count == 0)
            {
                LogNoChapterData(_logger, id);
                return;
            }

            var filePath = Plugin.Instance!.GetItemPath(id);
            if (string.IsNullOrEmpty(filePath))
            {
                LogUnableToGetPath(_logger, id);
                return;
            }

            if (!File.Exists(filePath))
            {
                LogMediaFileNotFound(_logger, id, filePath);
                return;
            }

            var chapterPath = GetChapterPath(filePath, _logger);
            LogWritingChapters(_logger, chapterPath);

            CreateChapterXmlFile(chapterPath, chapterContent, overwrite, _logger);
            LogChapterFileCreated(_logger, id, chapterPath);
        }
        catch (Exception ex)
        {
            LogChapterFileError(_logger, id, ex);
            throw;
        }
    }

    /// <summary>
    /// Convert segments to chapter entries.
    /// </summary>
    /// <param name="id">The ItemId.</param>
    /// <param name="segments">The Segments.</param>
    /// <returns>List of chapter entries.</returns>
    public IReadOnlyList<Chapter> ToChapter(Guid id, IReadOnlyCollection<MediaSegmentDto> segments)
    {
        if (segments is null || segments.Count == 0)
        {
            return [];
        }

        var config = EffectiveConfiguration;

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

            var name = GetChapterName(segment.Type, config);

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

    private static string GetChapterName(MediaSegmentType type, PluginConfiguration config)
    {
        return type switch
        {
            MediaSegmentType.Intro => config.Intro,
            MediaSegmentType.Outro => config.Outro,
            MediaSegmentType.Recap => config.Recap,
            MediaSegmentType.Preview => config.Preview,
            MediaSegmentType.Commercial => config.Commercial,
            _ => config.Unknown,
        };
    }

    internal static string GetChapterPath(string mediaPath, ILogger? logger = null)
    {
        // Resolve any VFS symlink so the chapter file is placed next to the real media file.
        // Fall back to the original path if resolution fails (e.g. broken symlink).
        string resolvedPath;
        try
        {
            resolvedPath = File.ResolveLinkTarget(mediaPath, returnFinalTarget: true)?.FullName ?? mediaPath;
        }
        catch (Exception ex)
        {
            if (logger is not null)
            {
                LogSymlinkResolutionFailed(logger, mediaPath, ex);
            }

            resolvedPath = mediaPath;
        }

        var dir = Path.GetDirectoryName(resolvedPath);
        if (string.IsNullOrEmpty(dir))
        {
            dir = Path.GetDirectoryName(mediaPath);
        }

        if (string.IsNullOrEmpty(dir))
        {
            throw new InvalidOperationException($"Unable to determine directory for media path '{mediaPath}'");
        }

        var chaptersDir = Path.Combine(dir, Constants.ChaptersDirectory);
        return Path.Combine(chaptersDir, $"{Path.GetFileNameWithoutExtension(resolvedPath)}{Constants.ChapterFileSuffix}.xml");
    }

    private static string TickToTime(long ticks) => TimeSpan.FromTicks(ticks).ToString(@"hh\:mm\:ss\.ff", System.Globalization.CultureInfo.InvariantCulture);

    private static void CreateChapterXmlFile(string filename, IReadOnlyList<Chapter> chapters, bool overwrite, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(filename);

        if (chapters is null || chapters.Count == 0)
        {
            if (logger is not null)
            {
                LogNoChaptersProvided(logger, filename);
            }

            return;
        }

        if (File.Exists(filename))
        {
            if (!overwrite)
            {
                if (logger is not null)
                {
                    LogSkippingExistingFile(logger, filename);
                }

                return;
            }

            if (logger is not null)
            {
                LogOverwritingFile(logger, filename);
            }
        }

        var directoryPath = Path.GetDirectoryName(filename)!;
        Directory.CreateDirectory(directoryPath);

        if (logger is not null)
        {
            LogEnsureDirectory(logger, directoryPath);
        }

        // Generate random UIDs for the Edition and Chapters
        long editionUID = GenerateUID();
        long[] chapterUIDs = [.. Enumerable.Range(0, chapters.Count).Select(_ => GenerateUID())];

        if (logger is not null)
        {
            LogWritingChapterCount(logger, chapters.Count, filename);
        }

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

    // Source-generated logging methods

    [LoggerMessage(Level = LogLevel.Debug, Message = "Overwrite Chapter files: {OverwriteFiles}")]
    private static partial void LogOverwriteSetting(ILogger logger, bool overwriteFiles);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Max Parallelism: {MaxParallelism}")]
    private static partial void LogMaxParallelism(ILogger logger, int maxParallelism);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping item {Id} as it already has embedded chapters")]
    private static partial void LogSkippingEmbeddedChapters(ILogger logger, Guid id);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processing chapters for item {Id}")]
    private static partial void LogProcessingChapters(ILogger logger, Guid id);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skip id ({Id}): no chapter data generated")]
    private static partial void LogNoChapterData(ILogger logger, Guid id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skip id ({Id}): unable to get item path")]
    private static partial void LogUnableToGetPath(ILogger logger, Guid id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skip id ({Id}): media file not found at {Path}")]
    private static partial void LogMediaFileNotFound(ILogger logger, Guid id, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Writing chapters to {Path}")]
    private static partial void LogWritingChapters(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Successfully created chapter file for {Id} at {Path}")]
    private static partial void LogChapterFileCreated(ILogger logger, Guid id, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to create chapter file for item {Id}")]
    private static partial void LogChapterFileError(ILogger logger, Guid id, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not resolve symlink for {Path}, using original path")]
    private static partial void LogSymlinkResolutionFailed(ILogger logger, string path, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No chapters provided for file {Filename}")]
    private static partial void LogNoChaptersProvided(ILogger logger, string filename);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping existing chapter file {Filename} (overwrite disabled)")]
    private static partial void LogSkippingExistingFile(ILogger logger, string filename);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Overwriting existing chapter file {Filename}")]
    private static partial void LogOverwritingFile(ILogger logger, string filename);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ensuring directory exists: {Directory}")]
    private static partial void LogEnsureDirectory(ILogger logger, string directory);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Writing {Count} chapters to {Filename}")]
    private static partial void LogWritingChapterCount(ILogger logger, int count, string filename);
}
