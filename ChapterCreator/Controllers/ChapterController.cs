using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using ChapterCreator.Managers;
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
/// <param name="chapterFileManager">ChapterFileManager.</param>
/// <param name="chapterOutputService">ChapterOutputService.</param>
[Authorize(Policy = "RequiresElevation")]
[ApiController]
[Produces(MediaTypeNames.Application.Json)]
[Route("PluginChapter")]
public class ChapterController(
    IMediaSegmentManager mediaSegmentManager,
    IChapterFileManager chapterFileManager,
    IChapterOutputService chapterOutputService) : ControllerBase
{
    private readonly IMediaSegmentManager _mediaSegmentManager = mediaSegmentManager;
    private readonly IChapterFileManager _chapterFileManager = chapterFileManager;
    private readonly IChapterOutputService _chapterOutputService = chapterOutputService;

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

        var rawstring = _chapterFileManager.ToChapter(itemId, segmentsList);

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
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ok.</returns>
    [HttpPost("Chapter")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<OkResult> GenerateData(
        [FromBody, Required] Guid[] itemIds,
        CancellationToken cancellationToken)
    {
        if (itemIds is null || itemIds.Length == 0)
        {
            throw new ArgumentNullException(nameof(itemIds));
        }

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

        // Group segments by ItemId and sort by StartTicks
        var sortedSegments = segmentsList.GroupAndSortByItem();

        foreach (var segment in sortedSegments)
        {
            await _chapterOutputService.ProcessChaptersAsync(segment, true, cancellationToken).ConfigureAwait(false);
        }

        return new OkResult();
    }

    /// <summary>
    /// Refresh chapter data for the given item IDs. Intended for use by external
    /// plugins (e.g. Intro Skipper) after they update media segments.
    /// </summary>
    /// <param name="itemIds">Array of item IDs to refresh chapters for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ok.</returns>
    [HttpPost("Refresh")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<OkResult> RefreshChapterData(
        [FromBody, Required] Guid[] itemIds,
        CancellationToken cancellationToken)
    {
        if (itemIds is null || itemIds.Length == 0)
        {
            throw new ArgumentNullException(nameof(itemIds));
        }

        foreach (var id in itemIds)
        {
            var item = Plugin.Instance!.GetItem(id);
            if (item is null)
            {
                continue;
            }

            var segmentsList = await _mediaSegmentManager.GetSegmentsAsync(
                item, null, new LibraryOptions(), true).ConfigureAwait(false);

            if (!segmentsList.Any())
            {
                continue;
            }

            var sortedSegments = segmentsList.SortForItem(id);

            await _chapterOutputService.ProcessChaptersAsync(sortedSegments, true, cancellationToken)
                .ConfigureAwait(false);
        }

        return new OkResult();
    }
}
