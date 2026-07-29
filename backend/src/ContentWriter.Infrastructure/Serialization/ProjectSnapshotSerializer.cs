using System.Text.Json;
using System.Text.Json.Serialization;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;

namespace ContentWriter.Infrastructure.Serialization;

/// <summary>
/// Serializes Project to/from JSON for durable persistence. The Project.Client navigation
/// is nulled before serialization to avoid duplication; Client is rehydrated from the
/// client cache on load.
/// </summary>
public static class ProjectSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(Project project)
    {
        // Snapshot excludes the Client navigation; it's rehydrated from the client cache on load.
        var snapshot = new ProjectSnapshot(
            SchemaVersion: 1,
            Id: project.Id,
            ClientId: project.ClientId,
            Name: project.Name,
            ProjectUrl: project.ProjectUrl,
            TargetKeyword: project.TargetKeyword,
            Department: project.Department,
            Status: project.Status,
            PreferredProvider: project.PreferredProvider,
            UseExactKeywordAsTitle: project.UseExactKeywordAsTitle,
            Notes: project.Notes,
            CreatedAtUtc: project.CreatedAtUtc,
            UpdatedAtUtc: project.UpdatedAtUtc,
            CrawledSite: project.CrawledSite,
            KeywordSources: project.KeywordSources,
            GeneratedContents: project.GeneratedContents);

        return JsonSerializer.Serialize(snapshot, Options);
    }

    public static Project Deserialize(string json, Client? client)
    {
        var snapshot = JsonSerializer.Deserialize<ProjectSnapshot>(json, Options)
            ?? throw new InvalidOperationException("Failed to deserialize project snapshot.");

        return new Project
        {
            Id = snapshot.Id,
            ClientId = snapshot.ClientId,
            Name = snapshot.Name,
            ProjectUrl = snapshot.ProjectUrl,
            TargetKeyword = snapshot.TargetKeyword,
            Department = snapshot.Department,
            Status = snapshot.Status,
            PreferredProvider = snapshot.PreferredProvider,
            UseExactKeywordAsTitle = snapshot.UseExactKeywordAsTitle,
            Notes = snapshot.Notes,
            CreatedAtUtc = snapshot.CreatedAtUtc,
            UpdatedAtUtc = snapshot.UpdatedAtUtc,
            Client = client,
            CrawledSite = snapshot.CrawledSite,
            KeywordSources = snapshot.KeywordSources,
            GeneratedContents = snapshot.GeneratedContents
        };
    }

    private sealed record ProjectSnapshot(
        int SchemaVersion,
        Guid Id,
        Guid ClientId,
        string Name,
        string ProjectUrl,
        string TargetKeyword,
        string Department,
        ProjectStatus Status,
        LlmProviderType PreferredProvider,
        bool UseExactKeywordAsTitle,
        string? Notes,
        DateTime CreatedAtUtc,
        DateTime? UpdatedAtUtc,
        CrawledSite? CrawledSite,
        List<KeywordSource> KeywordSources,
        List<GeneratedContent> GeneratedContents);
}
