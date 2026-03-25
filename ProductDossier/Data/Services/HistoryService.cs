using Microsoft.EntityFrameworkCore;
using ProductDossier.Data.Entities;
using ProductDossier.Data.Enums;

namespace ProductDossier.Data.Services
{
    // Сервис для работы с историей изменений документов (поиск и фильтрация записей)
    public static class HistoryService
    {
        // Метод для поиска записей истории по фильтрам (ФИО, операция, изделие, документ)
        // с ограничением количества результатов
        public static async Task<List<DocumentChangeHistory>> SearchAsync(
            string? fioQuery,
            HistoryOperationEnum? operation,
            string? productQuery,
            string? documentQuery,
            int limit = 500)
        {
            fioQuery = (fioQuery ?? string.Empty).Trim();
            documentQuery = (documentQuery ?? string.Empty).Trim();

            await using var db = new AppDbContext();

            IQueryable<DocumentChangeHistory> q = db.DocumentChangeHistory.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(fioQuery))
            {
                string pat = $"%{fioQuery}%";
                q = q.Where(h =>
                    EF.Functions.ILike(h.UserSurname, pat) ||
                    EF.Functions.ILike(h.UserName, pat) ||
                    (h.UserPatronymic != null && EF.Functions.ILike(h.UserPatronymic, pat)));
            }

            if (operation.HasValue)
            {
                q = q.Where(h => h.Operation == operation.Value);
            }

            productQuery = (productQuery ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(productQuery))
            {
                string pat = $"%{productQuery}%";
                q = q.Where(h =>
                    EF.Functions.ILike(h.ProductNumber, pat) ||
                    EF.Functions.ILike(h.ProductName, pat));
            }

            if (!string.IsNullOrWhiteSpace(documentQuery))
            {
                string pat = $"%{documentQuery}%";
                q = q.Where(h =>
                    EF.Functions.ILike(h.DocumentNumber, pat) ||
                    EF.Functions.ILike(h.DocumentName, pat));
            }

            return await q.OrderByDescending(h => h.ChangedAt)
                          .Take(limit)
                          .ToListAsync();
        }
    }
}