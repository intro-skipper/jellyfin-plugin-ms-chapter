using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using ChapterCreator.Configuration;
using ChapterCreator.Data;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.Logging;
using JellyfinChapterManager = MediaBrowser.Controller.Chapters.IChapterManager;

namespace ChapterCreator.Managers;

/// <summary>
/// Unified chapter output service. Routes chapter data to XML files
/// and/or Jellyfin's internal database based on plugin configuration.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ChapterOutputService"/> class.
/// </remarks>
/// <param name="chapterFileManager">The chapter file manager for XML output.</param>
/// <param name="jellyfinChapterManager">Jellyfin's chapter manager for DB injection.</param>
/// <param name="libraryManager">The library manager for resolving items.</param>
/// <param name="logger">The logger instance.</param>
/// <param name="configurationAccessor">The plugin configuration accessor.</param>
public partial class ChapterOutputService(
    IChapterFileManager chapterFileManager,
    JellyfinChapterManager jellyfinChapterManager,
    ILibraryManager libraryManager,
    ILogger<ChapterOutputService> logger,
    IPluginConfigurationAccessor configurationAccessor) : IChapterOutputService
{
    /// <summary>
    /// Supported time formats for parsing chapter timestamps from external XML files.
    /// Matroska/XML chapters may use varying fractional-second precision.
    /// </summary>
    private static readonly string[] _timeFormats =
    [
        @"hh\:mm\:ss\.FFFFFFF",
        @"hh\:mm\:ss"
    ];

    private readonly IChapterFileManager _chapterFileManager = chapterFileManager;
    private readonly JellyfinChapterManager _jellyfinChapterManager = jellyfinChapterManager;
    private readonly ILibraryManager _libraryManager = libraryManager;
    private readonly ILogger<ChapterOutputService> _logger = logger;
    private readonly IPluginConfigurationAccessor _configurationAccessor = configurationAccessor;

    /// <inheritdoc />
    public void LogConfiguration()
    {
        _chapterFileManager.LogConfiguration();
    }

    /// <inheritdoc />
    public ValueTask ProcessChaptersAsync(
        KeyValuePair<Guid, List<MediaSegmentDto>> segments,
        bool forceOverwrite,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var config = _configurationAccessor.GetConfiguration();
        var outputMode = config.OutputMode;
        List<ExceptionDispatchInfo>? failures = null;

        if (outputMode is ChapterOutputMode.XmlOnly or ChapterOutputMode.Both)
        {
            try
            {
                _chapterFileManager.UpdateChapterFile(segments, forceOverwrite);
            }
            catch (Exception ex)
            {
                LogXmlWriteFailure(_logger, segments.Key, ex);
                failures ??= [];
                failures.Add(ExceptionDispatchInfo.Capture(ex));
            }
        }

        if (outputMode is ChapterOutputMode.InjectOnly or ChapterOutputMode.Both)
        {
            try
            {
                InjectChaptersIntoJellyfin(segments.Key, segments.Value);
            }
            catch (Exception ex)
            {
                LogDbInjectionFailure(_logger, segments.Key, ex);
                failures ??= [];
                failures.Add(ExceptionDispatchInfo.Capture(ex));
            }
        }

        if (failures is { Count: 1 })
        {
            failures[0].Throw();
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException(failures.ConvertAll(static failure => failure.SourceException));
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask ImportFromXmlAsync(Guid itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var config = _configurationAccessor.GetConfiguration();
        if (!config.ImportXmlChapters)
        {
            return default;
        }

        if (config.OutputMode == ChapterOutputMode.XmlOnly)
        {
            return default;
        }

        var item = _libraryManager.GetItemById(itemId);
        if (item is not Video video || string.IsNullOrEmpty(video.Path))
        {
            LogSkipXmlImportNoPath(_logger, itemId);
            return default;
        }

        string chapterXmlPath;
        try
        {
            chapterXmlPath = ChapterFileManager.GetChapterPath(video.Path, _logger);
        }
        catch (InvalidOperationException ex)
        {
            LogSkipXmlImportNoChapterPath(_logger, itemId, ex);
            return default;
        }

        if (!System.IO.File.Exists(chapterXmlPath))
        {
            LogNoXmlFileFound(_logger, chapterXmlPath, itemId);
            return default;
        }

        try
        {
            var chapterInfos = ParseChapterXml(chapterXmlPath);
            if (chapterInfos.Count > 0)
            {
                _jellyfinChapterManager.SaveChapters(video, chapterInfos);
                LogImportedChapters(_logger, chapterInfos.Count, itemId);
            }
        }
        catch (Exception ex)
        {
            LogXmlImportFailure(_logger, chapterXmlPath, itemId, ex);
            throw;
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask ClearChaptersAsync(Guid itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var config = _configurationAccessor.GetConfiguration();
        var outputMode = config.OutputMode;
        List<ExceptionDispatchInfo>? failures = null;

        if (outputMode is ChapterOutputMode.XmlOnly or ChapterOutputMode.Both)
        {
            try
            {
                DeleteChapterFile(itemId);
            }
            catch (Exception ex)
            {
                LogXmlDeleteFailure(_logger, itemId, ex);
                failures ??= [];
                failures.Add(ExceptionDispatchInfo.Capture(ex));
            }
        }

        if (outputMode is ChapterOutputMode.InjectOnly or ChapterOutputMode.Both)
        {
            try
            {
                ClearInjectedChapters(itemId);
            }
            catch (Exception ex)
            {
                LogDbClearFailure(_logger, itemId, ex);
                failures ??= [];
                failures.Add(ExceptionDispatchInfo.Capture(ex));
            }
        }

        if (failures is { Count: 1 })
        {
            failures[0].Throw();
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException(failures.ConvertAll(static failure => failure.SourceException));
        }

        return default;
    }

    private static List<ChapterInfo> ParseChapterXml(string xmlPath)
    {
        var chapters = new List<ChapterInfo>();

        using var reader = XmlReader.Create(xmlPath, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });

        string? timeStart = null;
        string? chapterName = null;
        var inChapterAtom = false;
        var inChapterDisplay = false;

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    switch (reader.LocalName)
                    {
                        case "ChapterAtom":
                            inChapterAtom = true;
                            timeStart = null;
                            chapterName = null;
                            break;
                        case "ChapterTimeStart" when inChapterAtom:
                            timeStart = reader.ReadElementContentAsString();
                            break;
                        case "ChapterDisplay" when inChapterAtom:
                            inChapterDisplay = true;
                            break;
                        case "ChapterString" when inChapterDisplay:
                            chapterName = reader.ReadElementContentAsString();
                            break;
                    }

                    break;

                case XmlNodeType.EndElement:
                    switch (reader.LocalName)
                    {
                        case "ChapterAtom":
                            if (!string.IsNullOrEmpty(timeStart) &&
                                TimeSpan.TryParseExact(
                                    timeStart,
                                    _timeFormats,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out var ts))
                            {
                                chapters.Add(new ChapterInfo
                                {
                                    StartPositionTicks = ts.Ticks,
                                    Name = chapterName ?? string.Empty
                                });
                            }

                            inChapterAtom = false;
                            inChapterDisplay = false;
                            break;

                        case "ChapterDisplay":
                            inChapterDisplay = false;
                            break;
                    }

                    break;
            }
        }

        return chapters;
    }

    private void InjectChaptersIntoJellyfin(Guid itemId, List<MediaSegmentDto> segmentDtos)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is not Video video)
        {
            LogNotAVideoInjection(_logger, itemId);
            return;
        }

        var chapters = _chapterFileManager.ToChapter(itemId, segmentDtos);
        if (chapters.Count == 0)
        {
            LogNoChaptersGenerated(_logger, itemId);
            return;
        }

        var chapterInfos = new List<ChapterInfo>();
        foreach (var c in chapters)
        {
            if (TimeSpan.TryParseExact(
                c.StartTime,
                @"hh\:mm\:ss\.ff",
                System.Globalization.CultureInfo.InvariantCulture,
                out var ts))
            {
                chapterInfos.Add(new ChapterInfo
                {
                    StartPositionTicks = ts.Ticks,
                    Name = c.Title
                });
            }
            else
            {
                LogUnparseableStartTime(_logger, c.StartTime, itemId);
            }
        }

        _jellyfinChapterManager.SaveChapters(video, chapterInfos);
        LogInjectedChapters(_logger, chapterInfos.Count, itemId);
    }

    private void ClearInjectedChapters(Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is not Video video)
        {
            LogNotAVideoClearing(_logger, itemId);
            return;
        }

        _jellyfinChapterManager.SaveChapters(video, []);
        LogClearedInjectedChapters(_logger, itemId);
    }

    private void DeleteChapterFile(Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        var filePath = item?.Path;
        if (string.IsNullOrEmpty(filePath))
        {
            LogSkipXmlDeleteNoPath(_logger, itemId);
            return;
        }

        string chapterPath;
        try
        {
            chapterPath = ChapterFileManager.GetChapterPath(filePath, _logger);
        }
        catch (InvalidOperationException ex)
        {
            LogSkipXmlDeleteNoChapterPath(_logger, itemId, ex);
            return;
        }

        if (!System.IO.File.Exists(chapterPath))
        {
            LogNoXmlFileToDelete(_logger, chapterPath, itemId);
            return;
        }

        System.IO.File.Delete(chapterPath);
        LogDeletedXmlFile(_logger, chapterPath, itemId);
    }

    [LoggerMessage(EventId = 1100, Level = LogLevel.Error, Message = "Failed to write XML chapter file for item {Id}")]
    private static partial void LogXmlWriteFailure(ILogger logger, Guid id, Exception ex);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Error, Message = "Failed to inject chapters into Jellyfin DB for item {Id}")]
    private static partial void LogDbInjectionFailure(ILogger logger, Guid id, Exception ex);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Error, Message = "Failed to delete XML chapter file for item {Id}")]
    private static partial void LogXmlDeleteFailure(ILogger logger, Guid id, Exception ex);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Error, Message = "Failed to clear injected Jellyfin chapters for item {Id}")]
    private static partial void LogDbClearFailure(ILogger logger, Guid id, Exception ex);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Debug, Message = "Skip XML import for {Id}: unable to get item path")]
    private static partial void LogSkipXmlImportNoPath(ILogger logger, Guid id);

    [LoggerMessage(EventId = 1105, Level = LogLevel.Debug, Message = "Skip XML import for {Id}: could not resolve chapter path")]
    private static partial void LogSkipXmlImportNoChapterPath(ILogger logger, Guid id, Exception ex);

    [LoggerMessage(EventId = 1106, Level = LogLevel.Debug, Message = "Skip XML deletion for {Id}: unable to get item path")]
    private static partial void LogSkipXmlDeleteNoPath(ILogger logger, Guid id);

    [LoggerMessage(EventId = 1107, Level = LogLevel.Debug, Message = "Skip XML deletion for {Id}: could not resolve chapter path")]
    private static partial void LogSkipXmlDeleteNoChapterPath(ILogger logger, Guid id, Exception ex);

    [LoggerMessage(EventId = 1108, Level = LogLevel.Debug, Message = "No XML chapter file found at {Path} for item {Id}")]
    private static partial void LogNoXmlFileFound(ILogger logger, string path, Guid id);

    [LoggerMessage(EventId = 1109, Level = LogLevel.Debug, Message = "No XML chapter file found to delete at {Path} for item {Id}")]
    private static partial void LogNoXmlFileToDelete(ILogger logger, string path, Guid id);

    [LoggerMessage(EventId = 1110, Level = LogLevel.Debug, Message = "Deleted XML chapter file {Path} for item {Id}")]
    private static partial void LogDeletedXmlFile(ILogger logger, string path, Guid id);

    [LoggerMessage(EventId = 1112, Level = LogLevel.Debug, Message = "Imported {Count} chapters from XML for item {Id}")]
    private static partial void LogImportedChapters(ILogger logger, int count, Guid id);

    [LoggerMessage(EventId = 1113, Level = LogLevel.Error, Message = "Failed to import chapters from XML file {Path} for item {Id}")]
    private static partial void LogXmlImportFailure(ILogger logger, string path, Guid id, Exception ex);

    [LoggerMessage(EventId = 1114, Level = LogLevel.Warning, Message = "Item {Id} is not a Video, skipping Jellyfin chapter injection")]
    private static partial void LogNotAVideoInjection(ILogger logger, Guid id);

    [LoggerMessage(EventId = 1115, Level = LogLevel.Debug, Message = "No chapters generated for item {Id}, skipping injection")]
    private static partial void LogNoChaptersGenerated(ILogger logger, Guid id);

    [LoggerMessage(EventId = 1116, Level = LogLevel.Warning, Message = "Skipping chapter with unparseable StartTime '{StartTime}' for item {Id}")]
    private static partial void LogUnparseableStartTime(ILogger logger, string startTime, Guid id);

    [LoggerMessage(EventId = 1117, Level = LogLevel.Debug, Message = "Injected {Count} chapters into Jellyfin DB for item {Id}")]
    private static partial void LogInjectedChapters(ILogger logger, int count, Guid id);

    [LoggerMessage(EventId = 1118, Level = LogLevel.Debug, Message = "Cleared injected Jellyfin chapters for item {Id}")]
    private static partial void LogClearedInjectedChapters(ILogger logger, Guid id);

    [LoggerMessage(EventId = 1119, Level = LogLevel.Warning, Message = "Item {Id} is not a Video, skipping Jellyfin chapter clearing")]
    private static partial void LogNotAVideoClearing(ILogger logger, Guid id);
}
