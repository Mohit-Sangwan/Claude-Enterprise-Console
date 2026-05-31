namespace ClaudeEnterprise.Domain.Catalog;

/// <summary>
/// Represents a Claude model exposed to clients.
/// </summary>
public sealed record ClaudeModel(
    string Id,
    string DisplayName,
    string Family,
    string Tier,
    int ContextWindow,
    int MaxOutputTokens,
    bool IsDefault,
    bool Recommended,
    string Description,
    decimal InputCostPerMTok,
    decimal OutputCostPerMTok);

public interface IModelCatalog
{
    IReadOnlyList<ClaudeModel> All();
    ClaudeModel? Find(string id);
    ClaudeModel GetDefault();
}
