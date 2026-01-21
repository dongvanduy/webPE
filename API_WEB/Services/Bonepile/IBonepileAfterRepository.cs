using API_WEB.Models.Bonepile;

namespace API_WEB.Services.Bonepile
{
    public interface IBonepileAfterRepository
    {
        Task<List<BonepileAfterKanbanResult>> ExecuteBonepileAfterKanbanBasicQueryAsync(CancellationToken cancellationToken);
        List<string> GetExcludedSerialNumbers();
    }
}
