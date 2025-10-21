using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using ChapterCreator.Managers;
using ChapterCreator.SheduledTasks;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.MediaSegments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ChapterCreator.Controllers;

/// <summary>
/// Chapter controller.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ChapterController"/> class.
/// </remarks>
/// <param name="mediaSegmentManager">MediaSegmentsManager.</param>
/// <param name="chapterManager">ChapterManager.</param>
[Authorize(Policy = "RequiresElevation")]
[ApiController]
[Produces(MediaTypeNames.Application.Json)]
[Route("PluginChapter")]
public class ChapterController(
    IMediaSegmentManager mediaSegmentManager,
    IChapterManager chapterManager) : ControllerBase
{
    private readonly IMediaSegmentManager _mediaSegmentManager = mediaSegmentManager;
    private readonly IChapterManager _chapterManager = chapterManager;

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
        var segmentsList = new List<MediaSegmentDto>();
        var item = Plugin.Instance!.GetItem(itemId) ?? throw new ArgumentNullException(nameof(itemId), "Item not found");
        segmentsList.AddRange(await _mediaSegmentManager.GetSegmentsAsync(item, null, new LibraryOptions(), true).ConfigureAwait(false));

        var rawstring = _chapterManager.ToChapter(itemId, segmentsList);

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
        if (itemIds is null || itemIds.Length == 0)
        {
            throw new ArgumentNullException(nameof(itemIds));
        }

        var baseChapterTask = new BaseChapterTask(_chapterManager);

        var segmentsList = new List<MediaSegmentDto>();

        foreach (var id in itemIds)
        {
            var item = Plugin.Instance!.GetItem(id);
            if (item is null)
            {
                continue;
            }

            segmentsList.AddRange(await _mediaSegmentManager.GetSegmentsAsync(item, null, new LibraryOptions(), true).ConfigureAwait(false));
        }

        IProgress<double> progress = new Progress<double>();
        CancellationToken cancellationToken = CancellationToken.None;

        // write chapter files
        baseChapterTask.CreateChapters(progress, segmentsList, true, cancellationToken);

        return new OkResult();
    }
}
