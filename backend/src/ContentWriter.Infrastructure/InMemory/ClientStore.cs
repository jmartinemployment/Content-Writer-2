using System.Collections.Concurrent;
using ContentWriter.Domain.Entities;

namespace ContentWriter.Infrastructure.InMemory;

/// <summary>Holds every Client (and its PublishTarget) for the lifetime of this process. No database — see IProjectStore.</summary>
public interface IClientStore
{
    Task<List<Client>> ListAsync(CancellationToken cancellationToken = default);
    Task<Client?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Client client, CancellationToken cancellationToken = default);
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);
}

public sealed class ClientStore : IClientStore
{
    private readonly ConcurrentDictionary<Guid, Client> _clients = new();

    public Task<List<Client>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_clients.Values.OrderBy(c => c.Name).ToList());

    public Task<Client?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_clients.GetValueOrDefault(id));

    public Task AddAsync(Client client, CancellationToken cancellationToken = default)
    {
        _clients[client.Id] = client;
        return Task.CompletedTask;
    }

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(!_clients.IsEmpty);
}
