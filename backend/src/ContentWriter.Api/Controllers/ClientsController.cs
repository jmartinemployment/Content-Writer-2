using ContentWriter.Api.Contracts;
using ContentWriter.Domain.Entities;
using ContentWriter.Infrastructure.InMemory;
using Microsoft.AspNetCore.Mvc;

namespace ContentWriter.Api.Controllers;

[ApiController]
[Route("api/clients")]
public class ClientsController : ControllerBase
{
    private readonly IClientStore _clientStore;

    public ClientsController(IClientStore clientStore)
    {
        _clientStore = clientStore;
    }

    [HttpGet]
    public async Task<ActionResult<List<ClientResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var clients = await _clientStore.ListAsync(cancellationToken);
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
        await _clientStore.AddAsync(client, cancellationToken);

        return CreatedAtAction(nameof(GetAll), new { }, ToResponse(client));
    }

    [HttpPut("{clientId:guid}/publish-target")]
    public async Task<ActionResult<ClientResponse>> UpsertPublishTarget(
        Guid clientId, [FromBody] CreatePublishTargetRequest request, CancellationToken cancellationToken)
    {
        var client = await _clientStore.GetAsync(clientId, cancellationToken);
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

        client.PublishTarget ??= new PublishTarget { ClientId = client.Id };

        client.PublishTarget.GeekBackendApiBaseUrl = request.GeekBackendApiBaseUrl;
        client.PublishTarget.OAuthTokenEndpoint = request.OAuthTokenEndpoint;
        client.PublishTarget.ClientIdEnvVar = request.ClientIdEnvVar;
        client.PublishTarget.ClientSecretEnvVar = request.ClientSecretEnvVar;
        client.PublishTarget.DefaultAuthorId = request.DefaultAuthorId;
        client.PublishTarget.CategoryStrategy = request.CategoryStrategy;

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
