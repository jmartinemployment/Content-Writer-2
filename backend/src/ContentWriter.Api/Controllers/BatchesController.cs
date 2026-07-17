using ContentWriter.Api.Contracts;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContentWriter.Api.Controllers;

[ApiController]
[Route("api/batches")]
public class BatchesController : ControllerBase
{
    private readonly ContentWriterDbContext _db;

    public BatchesController(ContentWriterDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    public async Task<ActionResult<BatchJobResponse>> Create([FromBody] CreateBatchJobRequest request, CancellationToken cancellationToken)
    {
        if (request.ClientId == Guid.Empty || request.Items is null || request.Items.Count == 0)
        {
            return BadRequest("ClientId and at least one item are required.");
        }

        var clientExists = await _db.Clients.AnyAsync(c => c.Id == request.ClientId, cancellationToken);
        if (!clientExists)
        {
            return NotFound($"Client {request.ClientId} was not found.");
        }

        var job = new BatchJob
        {
            ClientId = request.ClientId,
            TotalItems = request.Items.Count,
        };

        foreach (var itemRequest in request.Items)
        {
            if (string.IsNullOrWhiteSpace(itemRequest.TargetKeyword) || string.IsNullOrWhiteSpace(itemRequest.ProjectUrl))
                continue;

            job.Items.Add(new BatchJobItem
            {
                BatchJobId = job.Id,
                TargetKeyword = itemRequest.TargetKeyword,
                ProjectUrl = itemRequest.ProjectUrl,
                PreferredProvider = itemRequest.PreferredProvider ?? LlmProviderType.LmStudio,
            });
        }

        job.TotalItems = job.Items.Count;
        if (job.TotalItems == 0)
        {
            return BadRequest("No valid items — TargetKeyword and ProjectUrl are required on each item.");
        }

        _db.BatchJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = job.Id }, await ToResponseAsync(job.Id, cancellationToken));
    }

    [HttpGet]
    public async Task<ActionResult<List<BatchJobSummaryResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var jobs = await _db.BatchJobs
            .OrderByDescending(j => j.CreatedAtUtc)
            .Take(50)
            .Select(j => new BatchJobSummaryResponse(
                j.Id, j.ClientId, j.Status, j.TotalItems, j.CompletedItems, j.FailedItems, j.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(jobs);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BatchJobResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var response = await ToResponseAsync(id, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var job = await _db.BatchJobs.Include(j => j.Items).FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        // Only stops items that haven't started — an in-flight item's current step finishes, then
        // BatchPipelineRunner naturally halts since there's nothing left in Queued for it to pick up.
        foreach (var item in job.Items.Where(i => i.Status == BatchJobItemStatus.Queued))
        {
            item.Status = BatchJobItemStatus.Cancelled;
        }

        if (job.Items.All(i => i.Status is BatchJobItemStatus.Completed or BatchJobItemStatus.Failed or BatchJobItemStatus.Cancelled))
        {
            job.Status = BatchJobStatus.Cancelled;
            job.CompletedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(await ToResponseAsync(id, cancellationToken));
    }

    private async Task<BatchJobResponse?> ToResponseAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _db.BatchJobs
            .Include(j => j.Items)
                .ThenInclude(i => i.Steps)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job is null)
            return null;

        var items = job.Items
            .OrderBy(i => i.CreatedAtUtc)
            .Select(i => new BatchJobItemResponse(
                i.Id, i.ProjectId, i.TargetKeyword, i.Status, i.ErrorText, i.StartedAtUtc, i.CompletedAtUtc,
                i.Steps
                    .OrderBy(s => s.StepName)
                    .Select(s => new BatchStepResponse(
                        s.StepName, s.Status, s.AttemptCount, s.ErrorText, s.StartedAtUtc, s.CompletedAtUtc))
                    .ToList()))
            .ToList();

        return new BatchJobResponse(
            job.Id, job.ClientId, job.Status, job.TotalItems, job.CompletedItems, job.FailedItems,
            job.CreatedAtUtc, job.StartedAtUtc, job.CompletedAtUtc, items);
    }
}
