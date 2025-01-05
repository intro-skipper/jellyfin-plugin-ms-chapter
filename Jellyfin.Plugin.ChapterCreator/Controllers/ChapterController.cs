using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ChapterCreator.SheduledTasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.MediaSegments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ChapterCreator.Controllers;

/// <summary>
/// Chapter controller.
/// </summary>
[Authorize(Policy = "RequiresElevation")]
[ApiController]
[Produces(MediaTypeNames.Application.Json)]
[Route("PluginChapter")]
public class ChapterController : ControllerBase
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSegmentManager _mediaSegmentManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChapterController"/> class.
    /// </summary>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <param name="libraryManager">LibraryManager.</param>
    /// <param name="mediaSegmentManager">MediaSegmentsManager.</param>
    public ChapterController(
        ILoggerFactory loggerFactory,
        ILibraryManager libraryManager,
        IMediaSegmentManager mediaSegmentManager)
    {
        _loggerFactory = loggerFactory;
        _libraryManager = libraryManager;
        _mediaSegmentManager = mediaSegmentManager;
    }

    /// <summary>
    /// Plugin meta endpoint.
    /// </summary>
    /// <returns>The version info.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public JsonResult GetPluginMetadata()
    {
        var json = new
        {
            version = Plugin.Instance!.Version.ToString(3),
        };

        return new JsonResult(json);
    }

    /// <summary>
    /// Get Chapter data based on itemId.
    /// </summary>
    /// <param name="itemId">ItemId.</param>
    /// <returns>The chapter data.</returns>
    [HttpGet("Chapter/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<JsonResult> GetChapterData(
        [FromRoute, Required] Guid itemId)
    {
        var queueManager = new QueueManager(_loggerFactory.CreateLogger<QueueManager>(), _libraryManager);

        var segmentsList = new List<MediaSegmentDto>();
        // get ItemIds
        var mediaItems = queueManager.GetMediaItemsById([itemId]);
        // get MediaSegments from itemIds
        foreach (var kvp in mediaItems)
        {
            foreach (var media in kvp.Value)
            {
                segmentsList.AddRange(await _mediaSegmentManager.GetSegmentsAsync(media.ItemId, null, true).ConfigureAwait(false));
            }
        }

        var rawstring = ChapterManager.ToChapter(itemId, segmentsList.AsReadOnly());

        var json = new
        {
            itemId,
            chapter = rawstring
        };

        return new JsonResult(json);
    }

    /// <summary>
    /// Force chapter recreation for itemIds.
    /// </summary>
    /// <param name="itemIds">ItemIds.</param>
    /// <returns>Ok.</returns>
    [HttpPost("Chapter")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<OkResult> GenerateData(
        [FromBody, Required] Guid[] itemIds)
    {
        var baseChapterTask = new BaseChapterTask(
            _loggerFactory.CreateLogger<CreateChapterTask>());

        var queueManager = new QueueManager(_loggerFactory.CreateLogger<QueueManager>(), _libraryManager);

        var segmentsList = new List<MediaSegmentDto>();
        // get ItemIds
        var mediaItems = queueManager.GetMediaItemsById(itemIds);
        // get MediaSegments from itemIds
        foreach (var kvp in mediaItems)
        {
            foreach (var media in kvp.Value)
            {
                segmentsList.AddRange(await _mediaSegmentManager.GetSegmentsAsync(media.ItemId, null, true).ConfigureAwait(false));
            }
        }

        IProgress<double> progress = new Progress<double>();
        CancellationToken cancellationToken = CancellationToken.None;

        // write chapter files
        baseChapterTask.CreateChapters(progress, segmentsList.AsReadOnly(), true, cancellationToken);

        return new OkResult();
    }
}
