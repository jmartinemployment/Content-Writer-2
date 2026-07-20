using ContentWriter.Api.Contracts;
using ContentWriter.Domain.Entities;
using ContentWriter.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContentWriter.Api.Controllers;

[ApiController]
[Route("api/clients")]
public class ClientsController : ControllerBase
{
    private readonly ContentWriterDbContext _db;

    public ClientsController(ContentWriterDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<ClientResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var clients = await _db.Clients
            .Include(c => c.PublishTarget)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return Ok(clients.Select(ToResponse).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ClientResponse>> Create([FromBody] CreateClientRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        var client = new Client { Name = request.Name, Notes = request.Notes };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetAll), new { }, ToResponse(client));
    }

    [HttpPut("{clientId:guid}/publish-target")]
    public async Task<ActionResult<ClientResponse>> UpsertPublishTarget(
        Guid clientId, [FromBody] CreatePublishTargetRequest request, CancellationToken cancellationToken)
    {
        var client = await _db.Clients
            .Include(c => c.PublishTarget)
            .FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);

        if (client is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.GeekBackendApiBaseUrl)
            || string.IsNullOrWhiteSpace(request.OAuthTokenEndpoint)
            || string.IsNullOrWhiteSpace(request.ClientIdEnvVar)
            || string.IsNullOrWhiteSpace(request.ClientSecretEnvVar))
        {
            return BadRequest("GeekBackendApiBaseUrl, OAuthTokenEndpoint, ClientIdEnvVar, and ClientSecretEnvVar are required.");
        }

        if (client.PublishTarget is null)
        {
            client.PublishTarget = new PublishTarget { ClientId = client.Id };
            _db.PublishTargets.Add(client.PublishTarget);
        }

        client.PublishTarget.GeekBackendApiBaseUrl = request.GeekBackendApiBaseUrl;
        client.PublishTarget.OAuthTokenEndpoint = request.OAuthTokenEndpoint;
        client.PublishTarget.ClientIdEnvVar = request.ClientIdEnvVar;
        client.PublishTarget.ClientSecretEnvVar = request.ClientSecretEnvVar;
        client.PublishTarget.DefaultAuthorId = request.DefaultAuthorId;
        client.PublishTarget.CategoryStrategy = request.CategoryStrategy;

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(client));
    }

    private static ClientResponse ToResponse(Client client) => new(
        client.Id, client.Name, client.Notes, client.CreatedAtUtc,
        client.PublishTarget is null
            ? null
            : new PublishTargetResponse(
                client.PublishTarget.Id, client.PublishTarget.GeekBackendApiBaseUrl,
                client.PublishTarget.OAuthTokenEndpoint, client.PublishTarget.ClientIdEnvVar,
                client.PublishTarget.ClientSecretEnvVar, client.PublishTarget.DefaultAuthorId,
                client.PublishTarget.CategoryStrategy));
}
