namespace ContentWriter.Application.Services.Batch;

public interface IBatchPipelineRunner
{
    /// <summary>Runs (or resumes) one BatchJobItem through the full generation pipeline.</summary>
    Task RunItemAsync(Guid batchJobItemId, CancellationToken cancellationToken = default);
}
