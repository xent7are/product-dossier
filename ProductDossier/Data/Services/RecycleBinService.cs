using Microsoft.EntityFrameworkCore;
using ProductDossier.Data.Entities;
using ProductDossier.Data.Enums;
using ProductDossier.Data.Models;
using ProductFile = ProductDossier.Data.Entities.FileItem;
using System.IO;
using ProductDossier.UI.Tree;

namespace ProductDossier.Data.Services
{
    // Сервис для перемещения объектов в корзину, восстановления и окончательного удаления
    public static class RecycleBinService
    {
        // Метод для получения списка объектов корзины
        public static async Task<List<RecycleBinItemDto>> GetItemsAsync()
        {
            await using AppDbContext db = new AppDbContext();

            List<User> users = await db.Users.AsNoTracking().ToListAsync();
            Dictionary<long, User> userById = users.ToDictionary(x => x.IdUser, x => x);

            List<Product> deletedProducts = await db.Products.AsNoTracking()
                .Where(x => x.Status == ProductStatusEnum.В_корзине && x.DeletedAt != null)
                .OrderByDescending(x => x.DeletedAt)
                .ToListAsync();

            List<Document> deletedDocuments = await db.Documents.AsNoTracking()
                .Where(x => x.Status == DocumentStatusEnum.В_корзине && x.IsDeletedRoot && x.DeletedAt != null)
                .OrderByDescending(x => x.DeletedAt)
                .ToListAsync();

            List<RecycleBinItemDto> result = new List<RecycleBinItemDto>();

            foreach (Product product in deletedProducts)
            {
                result.Add(new RecycleBinItemDto
                {
                    ObjectId = product.IdProduct,
                    ObjectType = RecycleBinObjectType.Product,
                    DisplayName = $"{product.ProductNumber} — {product.NameProduct}",
                    TypeDisplayName = "Изделие",
                    DeletedAtUtc = product.DeletedAt ?? DateTime.UtcNow,
                    DeletedByDisplayName = BuildUserDisplayName(product.DeletedBy, userById),
                    ProductDisplayName = $"{product.ProductNumber} — {product.NameProduct}",
                    DocumentDisplayName = "—"
                });
            }

            foreach (Document document in deletedDocuments)
            {
                Product? product = await TryGetProductForDocumentAsync(db, document.IdDocument);

                result.Add(new RecycleBinItemDto
                {
                    ObjectId = document.IdDocument,
                    ObjectType = RecycleBinObjectType.Document,
                    DisplayName = $"{document.DocumentNumber} — {document.NameDocument}",
                    TypeDisplayName = "Документ",
                    DeletedAtUtc = document.DeletedAt ?? DateTime.UtcNow,
                    DeletedByDisplayName = BuildUserDisplayName(document.DeletedBy, userById),
                    ProductDisplayName = product == null
                        ? "—"
                        : $"{product.ProductNumber} — {product.NameProduct}",
                    DocumentDisplayName = $"{document.DocumentNumber} — {document.NameDocument}"
                });
            }

            return result
                .OrderByDescending(x => x.DeletedAtUtc)
                .ThenBy(x => x.TypeDisplayName)
                .ThenBy(x => x.DisplayName)
                .ToList();
        }

        // Метод для перемещения файла в корзину через связанный с ним документ
        public static async Task MoveFileToRecycleBinAsync(long fileId, long currentUserId)
        {
            await using AppDbContext db = new AppDbContext();

            ProductFile file = await db.Files.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IdFile == fileId)
                ?? throw new InvalidOperationException("Файл не найден");

            await MoveDocumentToRecycleBinCoreAsync(file.IdDocument, currentUserId);
        }

        // Метод для перемещения документа и дочерних документов в корзину
        public static async Task MoveDocumentToRecycleBinAsync(long documentId, long currentUserId, DeletePermissionRole role)
        {
            if (role == DeletePermissionRole.Employee)
            {
                throw new UnauthorizedAccessException("Недостаточно прав для удаления");
            }

            await MoveDocumentToRecycleBinCoreAsync(documentId, currentUserId);
        }

        // Метод для перемещения изделия со всеми связанными документами и файлами в корзину
        public static async Task MoveProductToRecycleBinAsync(long productId, long currentUserId, DeletePermissionRole role)
        {
            if (role == DeletePermissionRole.Employee)
            {
                throw new UnauthorizedAccessException("Недостаточно прав для удаления");
            }

            await using AppDbContext db = new AppDbContext();

            User user = await db.Users.FirstOrDefaultAsync(x => x.IdUser == currentUserId)
                ?? throw new InvalidOperationException("Текущий пользователь не найден");

            Product product = await db.Products.FirstOrDefaultAsync(x => x.IdProduct == productId)
                ?? throw new InvalidOperationException("Изделие не найдено");

            if (product.Status == ProductStatusEnum.В_корзине)
            {
                throw new InvalidOperationException("Изделие уже находится в корзине");
            }

            List<long> documentIds = await GetProductDocumentClosureIdsAsync(db, productId);
            List<Document> documents = documentIds.Count == 0
                ? new List<Document>()
                : await db.Documents.Where(x => documentIds.Contains(x.IdDocument)).ToListAsync();

            List<ProductFile> files = documentIds.Count == 0
                ? new List<ProductFile>()
                : await db.Files.Where(x => documentIds.Contains(x.IdDocument)).ToListAsync();

            Dictionary<long, Product> productById = new Dictionary<long, Product>
            {
                [product.IdProduct] = product
            };

            Dictionary<long, long> productIdByDocumentId = documents.ToDictionary(x => x.IdDocument, _ => product.IdProduct);

            DateTime nowUtc = DateTime.UtcNow;

            List<PlannedFileMove> plannedMoves = BuildFileMovesForProductDelete(
                files,
                productIdByDocumentId,
                productById);

            List<ExecutedFileMove> executedMoves = new List<ExecutedFileMove>();

            try
            {
                ExecuteDeleteFileMoves(plannedMoves, executedMoves);
                TryDeleteEmptyMainProductFolders(product);

                await using var tx = await db.Database.BeginTransactionAsync();

                try
                {
                    product.StatusBeforeDelete = product.Status;
                    product.Status = ProductStatusEnum.В_корзине;
                    product.DeletedAt = nowUtc;
                    product.DeletedBy = currentUserId;

                    foreach (Document document in documents)
                    {
                        if (document.Status != DocumentStatusEnum.В_корзине)
                        {
                            document.StatusBeforeDelete = document.Status;
                        }

                        document.Status = DocumentStatusEnum.В_корзине;
                        document.DeletedAt = nowUtc;
                        document.DeletedBy = currentUserId;
                        document.IsDeletedRoot = false;
                    }

                    foreach (PlannedFileMove plannedMove in plannedMoves)
                    {
                        if (!string.IsNullOrWhiteSpace(plannedMove.TargetPathForDb))
                        {
                            plannedMove.File.RecycleBinFilePath = plannedMove.TargetPathForDb;
                        }
                    }

                    AddMoveToRecycleHistoryForProduct(db, user, product, files, nowUtc);

                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                catch
                {
                    try
                    {
                        await tx.RollbackAsync();
                    }
                    catch
                    {
                    }

                    throw;
                }
            }
            catch
            {
                RollbackExecutedMoves(executedMoves);
                throw;
            }
        }

        // Метод для восстановления объекта из корзины
        public static async Task RestoreAsync(
            RecycleBinObjectType objectType,
            long objectId,
            long currentUserId,
            DeletePermissionRole role)
        {
            if (role == DeletePermissionRole.Employee)
            {
                throw new UnauthorizedAccessException("Недостаточно прав для восстановления");
            }

            if (objectType == RecycleBinObjectType.Product)
            {
                await RestoreProductAsync(objectId, currentUserId);
                return;
            }

            await RestoreDocumentAsync(objectId, currentUserId);
        }

        // Метод для окончательного удаления объекта из корзины
        public static async Task DeletePermanentlyAsync(
            RecycleBinObjectType objectType,
            long objectId,
            long currentUserId,
            DeletePermissionRole role)
        {
            if (role != DeletePermissionRole.SuperAdmin)
            {
                throw new UnauthorizedAccessException("Окончательное удаление доступно только Супер-администратору");
            }

            if (objectType == RecycleBinObjectType.Product)
            {
                await DeleteProductPermanentlyAsync(objectId, currentUserId);
                return;
            }

            await DeleteDocumentPermanentlyAsync(objectId, currentUserId);
        }

        // Метод для перемещения документа в корзину без повторной проверки роли
        private static async Task MoveDocumentToRecycleBinCoreAsync(long documentId, long currentUserId)
        {
            await using AppDbContext db = new AppDbContext();

            User user = await db.Users.FirstOrDefaultAsync(x => x.IdUser == currentUserId)
                ?? throw new InvalidOperationException("Текущий пользователь не найден");

            Document rootDocument = await db.Documents.FirstOrDefaultAsync(x => x.IdDocument == documentId)
                ?? throw new InvalidOperationException("Документ не найден");

            if (rootDocument.Status == DocumentStatusEnum.В_корзине)
            {
                throw new InvalidOperationException("Документ уже находится в корзине");
            }

            List<long> documentIds = await GetDocumentSubtreeIdsAsync(db, documentId);
            List<Document> documents = await db.Documents.Where(x => documentIds.Contains(x.IdDocument)).ToListAsync();
            List<ProductFile> files = await db.Files.Where(x => documentIds.Contains(x.IdDocument)).ToListAsync();
            List<ProductDocument> links = await db.ProductDocuments.ToListAsync();

            Dictionary<long, long> productIdByDocumentId = BuildProductIdByDocumentId(documents, links);
            List<long> productIds = productIdByDocumentId.Values.Distinct().ToList();

            List<Product> products = productIds.Count == 0
                ? new List<Product>()
                : await db.Products.Where(x => productIds.Contains(x.IdProduct)).ToListAsync();

            Dictionary<long, Product> productById = products.ToDictionary(x => x.IdProduct, x => x);

            DateTime nowUtc = DateTime.UtcNow;

            List<PlannedFileMove> plannedMoves = BuildFileMovesForDocumentDelete(
                files,
                productIdByDocumentId,
                productById,
                rootDocument);

            List<ExecutedFileMove> executedMoves = new List<ExecutedFileMove>();

            try
            {
                ExecuteDeleteFileMoves(plannedMoves, executedMoves);

                if (productIdByDocumentId.TryGetValue(rootDocument.IdDocument, out long rootProductId)
                    && productById.TryGetValue(rootProductId, out Product? rootProductForCleanup))
                {
                    TryDeleteEmptyMainProductFolders(rootProductForCleanup);
                }

                await using var tx = await db.Database.BeginTransactionAsync();

                try
                {
                    foreach (Document document in documents)
                    {
                        if (document.Status != DocumentStatusEnum.В_корзине)
                        {
                            document.StatusBeforeDelete = document.Status;
                        }

                        document.Status = DocumentStatusEnum.В_корзине;
                        document.DeletedAt = nowUtc;
                        document.DeletedBy = currentUserId;
                        document.IsDeletedRoot = document.IdDocument == rootDocument.IdDocument;
                    }

                    foreach (PlannedFileMove plannedMove in plannedMoves)
                    {
                        if (!string.IsNullOrWhiteSpace(plannedMove.TargetPathForDb))
                        {
                            plannedMove.File.RecycleBinFilePath = plannedMove.TargetPathForDb;
                        }
                    }

                    Product? product = null;
                    if (productIdByDocumentId.TryGetValue(rootDocument.IdDocument, out long productId))
                    {
                        productById.TryGetValue(productId, out product);
                    }

                    AddMoveToRecycleHistoryForDocument(db, user, product, rootDocument, files, nowUtc);

                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                catch
                {
                    try
                    {
                        await tx.RollbackAsync();
                    }
                    catch
                    {
                    }

                    throw;
                }
            }
            catch
            {
                RollbackExecutedMoves(executedMoves);
                throw;
            }
        }

        // Метод для восстановления изделия
        private static async Task RestoreProductAsync(long productId, long currentUserId)
        {
            await using AppDbContext db = new AppDbContext();

            User user = await db.Users.FirstOrDefaultAsync(x => x.IdUser == currentUserId)
                ?? throw new InvalidOperationException("Текущий пользователь не найден");

            Product product = await db.Products.FirstOrDefaultAsync(x => x.IdProduct == productId)
                ?? throw new InvalidOperationException("Изделие не найдено");

            if (product.Status != ProductStatusEnum.В_корзине)
            {
                throw new InvalidOperationException("Изделие не находится в корзине");
            }

            List<long> documentIds = await GetProductDocumentClosureIdsAsync(db, productId);
            List<Document> documents = documentIds.Count == 0
                ? new List<Document>()
                : await db.Documents.Where(x => documentIds.Contains(x.IdDocument)).ToListAsync();

            List<ProductFile> files = documentIds.Count == 0
                ? new List<ProductFile>()
                : await db.Files.Where(x => documentIds.Contains(x.IdDocument)).ToListAsync();

            Dictionary<long, Product> productById = new Dictionary<long, Product>
            {
                [product.IdProduct] = product
            };

            Dictionary<long, long> productIdByDocumentId = documents.ToDictionary(x => x.IdDocument, _ => product.IdProduct);

            List<PlannedFileMove> plannedMoves = BuildFileMovesForRestore(files, productIdByDocumentId, productById);
            List<ExecutedFileMove> executedMoves = new List<ExecutedFileMove>();
            DateTime nowUtc = DateTime.UtcNow;

            try
            {
                ExecuteRestoreFileMoves(plannedMoves, executedMoves);
                TryDeleteEmptyRecycleFoldersForProduct(product);

                await using var tx = await db.Database.BeginTransactionAsync();

                try
                {
                    product.Status = product.StatusBeforeDelete ?? ProductStatusEnum.В_работе;
                    product.StatusBeforeDelete = null;
                    product.DeletedAt = null;
                    product.DeletedBy = null;

                    foreach (Document document in documents)
                    {
                        document.Status = document.StatusBeforeDelete ?? DocumentStatusEnum.В_работе;
                        document.StatusBeforeDelete = null;
                        document.DeletedAt = null;
                        document.DeletedBy = null;
                        document.IsDeletedRoot = false;
                    }

                    foreach (ProductFile file in files)
                    {
                        file.RecycleBinFilePath = null;
                    }

                    AddRestoreHistoryForProduct(db, user, product, files, nowUtc);

                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                catch
                {
                    try
                    {
                        await tx.RollbackAsync();
                    }
                    catch
                    {
                    }

                    throw;
                }
            }
            catch
            {
                RollbackExecutedMoves(executedMoves);
                throw;
            }
        }

        // Метод для восстановления документа
        private static async Task RestoreDocumentAsync(long documentId, long currentUserId)
        {
            await using AppDbContext db = new AppDbContext();

            User user = await db.Users.FirstOrDefaultAsync(x => x.IdUser == currentUserId)
                ?? throw new InvalidOperationException("Текущий пользователь не найден");

            Document rootDocument = await db.Documents.FirstOrDefaultAsync(x => x.IdDocument == documentId)
                ?? throw new InvalidOperationException("Документ не найден");

            if (rootDocument.Status != DocumentStatusEnum.В_корзине)
            {
                throw new InvalidOperationException("Документ не находится в корзине");
            }

            List<long> documentIds = await GetDocumentSubtreeIdsAsync(db, documentId);
            List<Document> documents = await db.Documents.Where(x => documentIds.Contains(x.IdDocument)).ToListAsync();
            List<ProductFile> files = await db.Files.Where(x => documentIds.Contains(x.IdDocument)).ToListAsync();
            List<ProductDocument> links = await db.ProductDocuments.ToListAsync();

            Dictionary<long, long> productIdByDocumentId = BuildProductIdByDocumentId(documents, links);
            List<long> productIds = productIdByDocumentId.Values.Distinct().ToList();

            List<Product> products = productIds.Count == 0
                ? new List<Product>()
                : await db.Products.Where(x => productIds.Contains(x.IdProduct)).ToListAsync();

            Dictionary<long, Product> productById = products.ToDictionary(x => x.IdProduct, x => x);

            List<PlannedFileMove> plannedMoves = BuildFileMovesForRestore(files, productIdByDocumentId, productById);
            List<ExecutedFileMove> executedMoves = new List<ExecutedFileMove>();
            DateTime nowUtc = DateTime.UtcNow;

            try
            {
                ExecuteRestoreFileMoves(plannedMoves, executedMoves);

                Product? productForCleanup = null;
                if (productIdByDocumentId.TryGetValue(rootDocument.IdDocument, out long productIdForCleanup))
                {
                    productById.TryGetValue(productIdForCleanup, out productForCleanup);
                }

                TryDeleteEmptyRecycleFoldersForDocument(productForCleanup, rootDocument);

                await using var tx = await db.Database.BeginTransactionAsync();

                try
                {
                    foreach (Document document in documents)
                    {
                        document.Status = document.StatusBeforeDelete ?? DocumentStatusEnum.В_работе;
                        document.StatusBeforeDelete = null;
                        document.DeletedAt = null;
                        document.DeletedBy = null;
                        document.IsDeletedRoot = false;
                    }

                    foreach (ProductFile file in files)
                    {
                        file.RecycleBinFilePath = null;
                    }

                    Product? product = null;
                    if (productIdByDocumentId.TryGetValue(rootDocument.IdDocument, out long productId))
                    {
                        productById.TryGetValue(productId, out product);
                    }

                    AddRestoreHistoryForDocument(db, user, product, rootDocument, files, nowUtc);

                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                catch
                {
                    try
                    {
                        await tx.RollbackAsync();
                    }
                    catch
                    {
                    }

                    throw;
                }
            }
            catch
            {
                RollbackExecutedMoves(executedMoves);
                throw;
            }
        }

        // Метод для окончательного удаления изделия
        private static async Task DeleteProductPermanentlyAsync(long productId, long currentUserId)
        {
            await using AppDbContext db = new AppDbContext();

            User user = await db.Users.FirstOrDefaultAsync(x => x.IdUser == currentUserId)
                ?? throw new InvalidOperationException("Текущий пользователь не найден");

            Product product = await db.Products.FirstOrDefaultAsync(x => x.IdProduct == productId)
                ?? throw new InvalidOperationException("Изделие не найдено");

            if (product.Status != ProductStatusEnum.В_корзине)
            {
                throw new InvalidOperationException("Изделие не находится в корзине");
            }

            List<long> documentIds = await GetProductDocumentClosureIdsAsync(db, productId);
            List<Document> documents = documentIds.Count == 0
                ? new List<Document>()
                : await db.Documents.Where(x => documentIds.Contains(x.IdDocument)).ToListAsync();

            List<ProductFile> files = documentIds.Count == 0
                ? new List<ProductFile>()
                : await db.Files.Where(x => documentIds.Contains(x.IdDocument)).ToListAsync();

            List<ProductDocument> links = documentIds.Count == 0
                ? new List<ProductDocument>()
                : await db.ProductDocuments.Where(x => x.IdProduct == productId || documentIds.Contains(x.IdDocument)).ToListAsync();

            DateTime nowUtc = DateTime.UtcNow;

            AddPermanentDeleteHistoryForProduct(db, user, product, files, nowUtc);

            DeletePhysicalFilesFromRecycle(files);
            TryDeleteEmptyRecycleFoldersForProduct(product);

            await using var tx = await db.Database.BeginTransactionAsync();

            try
            {
                db.Files.RemoveRange(files);
                db.ProductDocuments.RemoveRange(links);

                foreach (Document document in OrderDocumentsForDelete(documents))
                {
                    db.Documents.Remove(document);
                }

                db.Products.Remove(product);

                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                try
                {
                    await tx.RollbackAsync();
                }
                catch
                {
                }

                throw;
            }
        }

        // Метод для окончательного удаления документа
        private static async Task DeleteDocumentPermanentlyAsync(long documentId, long currentUserId)
        {
            await using AppDbContext db = new AppDbContext();

            User user = await db.Users.FirstOrDefaultAsync(x => x.IdUser == currentUserId)
                ?? throw new InvalidOperationException("Текущий пользователь не найден");

            Document rootDocument = await db.Documents.FirstOrDefaultAsync(x => x.IdDocument == documentId)
                ?? throw new InvalidOperationException("Документ не найден");

            if (rootDocument.Status != DocumentStatusEnum.В_корзине)
            {
                throw new InvalidOperationException("Документ не находится в корзине");
            }

            List<long> documentIds = await GetDocumentSubtreeIdsAsync(db, documentId);
            List<Document> documents = await db.Documents.Where(x => documentIds.Contains(x.IdDocument)).ToListAsync();
            List<ProductFile> files = await db.Files.Where(x => documentIds.Contains(x.IdDocument)).ToListAsync();
            List<ProductDocument> links = await db.ProductDocuments.Where(x => documentIds.Contains(x.IdDocument)).ToListAsync();

            Product? product = await TryGetProductForDocumentAsync(db, rootDocument.IdDocument);
            DateTime nowUtc = DateTime.UtcNow;

            AddPermanentDeleteHistoryForDocument(db, user, product, rootDocument, files, nowUtc);

            DeletePhysicalFilesFromRecycle(files);
            TryDeleteEmptyRecycleFoldersForDocument(product, rootDocument);

            await using var tx = await db.Database.BeginTransactionAsync();

            try
            {
                db.Files.RemoveRange(files);
                db.ProductDocuments.RemoveRange(links);

                foreach (Document document in OrderDocumentsForDelete(documents))
                {
                    db.Documents.Remove(document);
                }

                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                try
                {
                    await tx.RollbackAsync();
                }
                catch
                {
                }

                throw;
            }
        }

        // Метод для построения отображаемого имени пользователя
        private static string BuildUserDisplayName(long? userId, IReadOnlyDictionary<long, User> userById)
        {
            if (!userId.HasValue || !userById.TryGetValue(userId.Value, out User? user))
            {
                return "—";
            }

            if (string.IsNullOrWhiteSpace(user.Patronymic))
            {
                return $"{user.Surname} {user.Name}";
            }

            return $"{user.Surname} {user.Name} {user.Patronymic}";
        }

        // Метод для построения сопоставления документ -> изделие с учётом дочерних документов
        private static Dictionary<long, long> BuildProductIdByDocumentId(
            List<Document> documents,
            List<ProductDocument> allLinks)
        {
            Dictionary<long, long> directMap = allLinks
                .GroupBy(x => x.IdDocument)
                .ToDictionary(x => x.Key, x => x.First().IdProduct);

            Dictionary<long, Document> documentById = documents.ToDictionary(x => x.IdDocument, x => x);
            Dictionary<long, long> result = new Dictionary<long, long>();

            foreach (Document document in documents)
            {
                long? currentId = document.IdDocument;
                long? resolvedProductId = null;

                while (currentId.HasValue)
                {
                    if (directMap.TryGetValue(currentId.Value, out long productId))
                    {
                        resolvedProductId = productId;
                        break;
                    }

                    if (!documentById.TryGetValue(currentId.Value, out Document? currentDocument))
                    {
                        break;
                    }

                    currentId = currentDocument.IdParentDocument;
                }

                if (resolvedProductId.HasValue)
                {
                    result[document.IdDocument] = resolvedProductId.Value;
                }
            }

            return result;
        }

        // Метод для планирования перемещения файлов изделия в корзину
        private static List<PlannedFileMove> BuildFileMovesForProductDelete(
            List<ProductFile> files,
            IReadOnlyDictionary<long, long> productIdByDocumentId,
            IReadOnlyDictionary<long, Product> productById)
        {
            List<PlannedFileMove> result = new List<PlannedFileMove>();

            foreach (ProductFile file in files)
            {
                Product? product = null;

                if (productIdByDocumentId.TryGetValue(file.IdDocument, out long productId))
                {
                    productById.TryGetValue(productId, out product);
                }

                string? sourceAbsolutePath = GetCurrentPhysicalFilePath(file);
                string? targetAbsolutePath = null;
                string? targetPathForDb = null;

                if (!string.IsNullOrWhiteSpace(sourceAbsolutePath) && File.Exists(sourceAbsolutePath))
                {
                    if (RecycleBinPathService.IsPathInsideRecycleBin(sourceAbsolutePath) &&
                        !string.IsNullOrWhiteSpace(file.RecycleBinFilePath))
                    {
                        targetAbsolutePath = sourceAbsolutePath;
                        targetPathForDb = file.RecycleBinFilePath;
                    }
                    else
                    {
                        targetAbsolutePath = RecycleBinPathService.BuildRecycleProductFileAbsolutePath(sourceAbsolutePath, product);
                        targetPathForDb = RecycleBinPathService.PreparePathForDb(targetAbsolutePath);
                    }
                }

                result.Add(new PlannedFileMove
                {
                    File = file,
                    SourceAbsolutePath = sourceAbsolutePath,
                    TargetAbsolutePath = targetAbsolutePath,
                    TargetPathForDb = targetPathForDb
                });
            }

            return result;
        }

        // Метод для планирования перемещения файлов документа в корзину
        private static List<PlannedFileMove> BuildFileMovesForDocumentDelete(
            List<ProductFile> files,
            IReadOnlyDictionary<long, long> productIdByDocumentId,
            IReadOnlyDictionary<long, Product> productById,
            Document rootDocument)
        {
            List<PlannedFileMove> result = new List<PlannedFileMove>();

            foreach (ProductFile file in files)
            {
                Product? product = null;

                if (productIdByDocumentId.TryGetValue(file.IdDocument, out long productId))
                {
                    productById.TryGetValue(productId, out product);
                }

                string? sourceAbsolutePath = GetCurrentPhysicalFilePath(file);
                string? targetAbsolutePath = null;
                string? targetPathForDb = null;

                if (!string.IsNullOrWhiteSpace(sourceAbsolutePath) && File.Exists(sourceAbsolutePath))
                {
                    if (RecycleBinPathService.IsPathInsideRecycleBin(sourceAbsolutePath) &&
                        !string.IsNullOrWhiteSpace(file.RecycleBinFilePath))
                    {
                        targetAbsolutePath = sourceAbsolutePath;
                        targetPathForDb = file.RecycleBinFilePath;
                    }
                    else
                    {
                        targetAbsolutePath = RecycleBinPathService.BuildRecycleDocumentFileAbsolutePath(sourceAbsolutePath, product, rootDocument);
                        targetPathForDb = RecycleBinPathService.PreparePathForDb(targetAbsolutePath);
                    }
                }

                result.Add(new PlannedFileMove
                {
                    File = file,
                    SourceAbsolutePath = sourceAbsolutePath,
                    TargetAbsolutePath = targetAbsolutePath,
                    TargetPathForDb = targetPathForDb
                });
            }

            return result;
        }

        // Метод для планирования восстановления файлов из корзины
        private static List<PlannedFileMove> BuildFileMovesForRestore(
            List<ProductFile> files,
            IReadOnlyDictionary<long, long> productIdByDocumentId,
            IReadOnlyDictionary<long, Product> productById)
        {
            List<PlannedFileMove> result = new List<PlannedFileMove>();

            foreach (ProductFile file in files)
            {
                Product? product = null;

                if (productIdByDocumentId.TryGetValue(file.IdDocument, out long productId))
                {
                    productById.TryGetValue(productId, out product);
                }

                string? sourceAbsolutePath = string.IsNullOrWhiteSpace(file.RecycleBinFilePath)
                    ? null
                    : RecycleBinPathService.ResolveAbsolutePath(file.RecycleBinFilePath);

                string? targetAbsolutePath = null;

                if (!string.IsNullOrWhiteSpace(file.FilePath))
                {
                    targetAbsolutePath = DossierService.ResolveAbsolutePath(file.FilePath);
                }
                else if (product != null)
                {
                    targetAbsolutePath = Path.Combine(BuildProductFolder(product), file.FileName);
                }

                result.Add(new PlannedFileMove
                {
                    File = file,
                    SourceAbsolutePath = sourceAbsolutePath,
                    TargetAbsolutePath = targetAbsolutePath,
                    TargetPathForDb = null
                });
            }

            return result;
        }

        // Метод для выполнения файловых перемещений в корзину
        private static void ExecuteDeleteFileMoves(List<PlannedFileMove> plannedMoves, List<ExecutedFileMove> executedMoves)
        {
            foreach (PlannedFileMove plannedMove in plannedMoves)
            {
                if (string.IsNullOrWhiteSpace(plannedMove.SourceAbsolutePath) ||
                    string.IsNullOrWhiteSpace(plannedMove.TargetAbsolutePath) ||
                    !File.Exists(plannedMove.SourceAbsolutePath))
                {
                    continue;
                }

                if (string.Equals(plannedMove.SourceAbsolutePath, plannedMove.TargetAbsolutePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? targetDirectory = Path.GetDirectoryName(plannedMove.TargetAbsolutePath);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                if (File.Exists(plannedMove.TargetAbsolutePath))
                {
                    File.Delete(plannedMove.TargetAbsolutePath);
                }

                File.Move(plannedMove.SourceAbsolutePath, plannedMove.TargetAbsolutePath);

                executedMoves.Add(new ExecutedFileMove
                {
                    SourceAbsolutePath = plannedMove.SourceAbsolutePath,
                    TargetAbsolutePath = plannedMove.TargetAbsolutePath
                });
            }
        }

        // Метод для выполнения файловых перемещений при восстановлении
        private static void ExecuteRestoreFileMoves(List<PlannedFileMove> plannedMoves, List<ExecutedFileMove> executedMoves)
        {
            foreach (PlannedFileMove plannedMove in plannedMoves)
            {
                if (string.IsNullOrWhiteSpace(plannedMove.SourceAbsolutePath) ||
                    string.IsNullOrWhiteSpace(plannedMove.TargetAbsolutePath) ||
                    !File.Exists(plannedMove.SourceAbsolutePath))
                {
                    continue;
                }

                if (string.Equals(plannedMove.SourceAbsolutePath, plannedMove.TargetAbsolutePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? targetDirectory = Path.GetDirectoryName(plannedMove.TargetAbsolutePath);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                if (File.Exists(plannedMove.TargetAbsolutePath))
                {
                    File.Delete(plannedMove.TargetAbsolutePath);
                }

                File.Move(plannedMove.SourceAbsolutePath, plannedMove.TargetAbsolutePath);

                executedMoves.Add(new ExecutedFileMove
                {
                    SourceAbsolutePath = plannedMove.SourceAbsolutePath,
                    TargetAbsolutePath = plannedMove.TargetAbsolutePath
                });
            }
        }

        // Метод для отката уже выполненных файловых перемещений
        private static void RollbackExecutedMoves(List<ExecutedFileMove> executedMoves)
        {
            for (int i = executedMoves.Count - 1; i >= 0; i--)
            {
                ExecutedFileMove move = executedMoves[i];

                try
                {
                    if (!File.Exists(move.TargetAbsolutePath))
                    {
                        continue;
                    }

                    string? sourceDirectory = Path.GetDirectoryName(move.SourceAbsolutePath);
                    if (!string.IsNullOrWhiteSpace(sourceDirectory))
                    {
                        Directory.CreateDirectory(sourceDirectory);
                    }

                    if (File.Exists(move.SourceAbsolutePath))
                    {
                        File.Delete(move.SourceAbsolutePath);
                    }

                    File.Move(move.TargetAbsolutePath, move.SourceAbsolutePath);
                }
                catch
                {
                }
            }
        }

        // Метод для удаления физических файлов из корзины
        private static void DeletePhysicalFilesFromRecycle(List<ProductFile> files)
        {
            foreach (ProductFile file in files)
            {
                string path = string.IsNullOrWhiteSpace(file.RecycleBinFilePath)
                    ? string.Empty
                    : RecycleBinPathService.ResolveAbsolutePath(file.RecycleBinFilePath);

                TryDeletePhysicalFile(path);
            }
        }

        // Метод для получения текущего физического пути файла
        private static string? GetCurrentPhysicalFilePath(ProductFile file)
        {
            if (!string.IsNullOrWhiteSpace(file.RecycleBinFilePath))
            {
                string recyclePath = RecycleBinPathService.ResolveAbsolutePath(file.RecycleBinFilePath);
                if (File.Exists(recyclePath))
                {
                    return recyclePath;
                }
            }

            if (!string.IsNullOrWhiteSpace(file.FilePath))
            {
                string mainPath = DossierService.ResolveAbsolutePath(file.FilePath);
                if (File.Exists(mainPath))
                {
                    return mainPath;
                }

                return mainPath;
            }

            return null;
        }

        // Метод для удаления пустых папок изделия в основном хранилище
        private static void TryDeleteEmptyMainProductFolders(Product product)
        {
            foreach (string folder in RecycleBinPathService.GetMainProductFolderCandidates(product))
            {
                try
                {
                    if (!Directory.Exists(folder))
                    {
                        continue;
                    }

                    if (!Directory.EnumerateFileSystemEntries(folder).Any())
                    {
                        Directory.Delete(folder, false);
                    }
                }
                catch
                {
                }
            }
        }

        // Метод для удаления пустых папок изделия в корзине
        private static void TryDeleteEmptyRecycleFoldersForProduct(Product product)
        {
            RecycleBinPathService.TryDeleteEmptyRecycleProductFolder(product);
        }

        // Метод для удаления пустых папок документа и изделия в корзине
        private static void TryDeleteEmptyRecycleFoldersForDocument(Product? product, Document rootDocument)
        {
            RecycleBinPathService.TryDeleteEmptyDeletedDocumentFolders(product, rootDocument);
        }

        // Метод для построения папки изделия в основном хранилище
        private static string BuildProductFolder(Product product)
        {
            string? existingFolder = RecycleBinPathService.TryGetExistingMainProductFolderAbsolutePath(product);
            if (!string.IsNullOrWhiteSpace(existingFolder))
            {
                Directory.CreateDirectory(existingFolder);
                return existingFolder;
            }

            string root = ProductsRootPathService.GetRootFolder();

            string folderName = SanitizeFolderName(product.NameProduct);
            if (string.IsNullOrWhiteSpace(folderName) || folderName == "Без_названия")
            {
                folderName = SanitizeFolderName(product.ProductNumber);
            }

            string productFolder = Path.Combine(root, folderName);
            Directory.CreateDirectory(productFolder);
            return productFolder;
        }

        // Метод для подготовки безопасного имени папки
        private static string SanitizeFolderName(string? value)
        {
            string safeValue = (value ?? string.Empty).Trim();

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                safeValue = safeValue.Replace(invalidChar, '_');
            }

            return string.IsNullOrWhiteSpace(safeValue) ? "Без_названия" : safeValue;
        }

        // Метод для получения изделия по документу
        private static async Task<Product?> TryGetProductForDocumentAsync(AppDbContext db, long documentId)
        {
            long? currentDocumentId = documentId;

            while (currentDocumentId.HasValue)
            {
                ProductDocument? link = await db.ProductDocuments.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.IdDocument == currentDocumentId.Value);

                if (link != null)
                {
                    return await db.Products.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.IdProduct == link.IdProduct);
                }

                Document? currentDocument = await db.Documents.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.IdDocument == currentDocumentId.Value);

                currentDocumentId = currentDocument?.IdParentDocument;
            }

            return null;
        }

        // Метод для получения идентификаторов поддерева документов
        private static async Task<List<long>> GetDocumentSubtreeIdsAsync(AppDbContext db, long rootDocumentId)
        {
            List<Document> allDocuments = await db.Documents.AsNoTracking().ToListAsync();

            Dictionary<long, List<long>> childrenByParentId = allDocuments
                .Where(x => x.IdParentDocument.HasValue)
                .GroupBy(x => x.IdParentDocument!.Value)
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(y => y.IdDocument).ToList());

            List<long> result = new List<long>();
            Queue<long> queue = new Queue<long>();
            queue.Enqueue(rootDocumentId);

            while (queue.Count > 0)
            {
                long currentId = queue.Dequeue();
                if (result.Contains(currentId))
                {
                    continue;
                }

                result.Add(currentId);

                if (childrenByParentId.TryGetValue(currentId, out List<long>? children))
                {
                    foreach (long childId in children)
                    {
                        queue.Enqueue(childId);
                    }
                }
            }

            return result;
        }

        // Метод для получения всех документов изделия включая дочерние
        private static async Task<List<long>> GetProductDocumentClosureIdsAsync(AppDbContext db, long productId)
        {
            List<long> rootDocumentIds = await db.ProductDocuments.AsNoTracking()
                .Where(x => x.IdProduct == productId)
                .Select(x => x.IdDocument)
                .ToListAsync();

            List<Document> allDocuments = await db.Documents.AsNoTracking().ToListAsync();

            Dictionary<long, List<long>> childrenByParentId = allDocuments
                .Where(x => x.IdParentDocument.HasValue)
                .GroupBy(x => x.IdParentDocument!.Value)
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(y => y.IdDocument).ToList());

            List<long> result = new List<long>();
            Queue<long> queue = new Queue<long>(rootDocumentIds);

            while (queue.Count > 0)
            {
                long currentId = queue.Dequeue();
                if (result.Contains(currentId))
                {
                    continue;
                }

                result.Add(currentId);

                if (childrenByParentId.TryGetValue(currentId, out List<long>? children))
                {
                    foreach (long childId in children)
                    {
                        queue.Enqueue(childId);
                    }
                }
            }

            return result;
        }

        // Метод для упорядочивания документов перед физическим удалением из БД
        private static List<Document> OrderDocumentsForDelete(List<Document> documents)
        {
            Dictionary<long, Document> documentById = documents.ToDictionary(x => x.IdDocument, x => x);

            int GetDepth(Document document)
            {
                int depth = 0;
                long? currentParentId = document.IdParentDocument;

                while (currentParentId.HasValue && documentById.TryGetValue(currentParentId.Value, out Document? parentDocument))
                {
                    depth++;
                    currentParentId = parentDocument.IdParentDocument;
                }

                return depth;
            }

            return documents
                .OrderByDescending(GetDepth)
                .ToList();
        }

        // Метод для добавления записи истории о перемещении документа в корзину
        private static void AddMoveToRecycleHistoryForDocument(
            AppDbContext db,
            User user,
            Product? product,
            Document rootDocument,
            IReadOnlyList<ProductFile> files,
            DateTime changedAtUtc)
        {
            db.DocumentChangeHistory.Add(new DocumentChangeHistory
            {
                UserSurname = user.Surname,
                UserName = user.Name,
                UserPatronymic = user.Patronymic,
                Operation = HistoryOperationEnum.Перемещение_в_корзину,
                ChangedAt = changedAtUtc,
                FileName = files.Count == 1 ? files[0].FileName : $"({files.Count} файлов)",
                FilePath = files.Count == 1 ? (files[0].RecycleBinFilePath ?? "—") : "—",
                ProductNumber = product?.ProductNumber ?? "—",
                ProductName = product?.NameProduct ?? "—",
                DocumentNumber = rootDocument.DocumentNumber,
                DocumentName = rootDocument.NameDocument
            });
        }

        // Метод для добавления записи истории о перемещении изделия в корзину
        private static void AddMoveToRecycleHistoryForProduct(
            AppDbContext db,
            User user,
            Product product,
            IReadOnlyList<ProductFile> files,
            DateTime changedAtUtc)
        {
            db.DocumentChangeHistory.Add(new DocumentChangeHistory
            {
                UserSurname = user.Surname,
                UserName = user.Name,
                UserPatronymic = user.Patronymic,
                Operation = HistoryOperationEnum.Перемещение_в_корзину,
                ChangedAt = changedAtUtc,
                FileName = files.Count == 1 ? files[0].FileName : $"({files.Count} файлов)",
                FilePath = files.Count == 1 ? (files[0].RecycleBinFilePath ?? "—") : "—",
                ProductNumber = product.ProductNumber,
                ProductName = product.NameProduct,
                DocumentNumber = "—",
                DocumentName = "—"
            });
        }

        // Метод для добавления записи истории о восстановлении документа
        private static void AddRestoreHistoryForDocument(
            AppDbContext db,
            User user,
            Product? product,
            Document rootDocument,
            IReadOnlyList<ProductFile> files,
            DateTime changedAtUtc)
        {
            db.DocumentChangeHistory.Add(new DocumentChangeHistory
            {
                UserSurname = user.Surname,
                UserName = user.Name,
                UserPatronymic = user.Patronymic,
                Operation = HistoryOperationEnum.Восстановление,
                ChangedAt = changedAtUtc,
                FileName = files.Count == 1 ? files[0].FileName : $"({files.Count} файлов)",
                FilePath = files.Count == 1 ? files[0].FilePath : "—",
                ProductNumber = product?.ProductNumber ?? "—",
                ProductName = product?.NameProduct ?? "—",
                DocumentNumber = rootDocument.DocumentNumber,
                DocumentName = rootDocument.NameDocument
            });
        }

        // Метод для добавления записи истории о восстановлении изделия
        private static void AddRestoreHistoryForProduct(
            AppDbContext db,
            User user,
            Product product,
            IReadOnlyList<ProductFile> files,
            DateTime changedAtUtc)
        {
            db.DocumentChangeHistory.Add(new DocumentChangeHistory
            {
                UserSurname = user.Surname,
                UserName = user.Name,
                UserPatronymic = user.Patronymic,
                Operation = HistoryOperationEnum.Восстановление,
                ChangedAt = changedAtUtc,
                FileName = files.Count == 1 ? files[0].FileName : $"({files.Count} файлов)",
                FilePath = files.Count == 1 ? files[0].FilePath : "—",
                ProductNumber = product.ProductNumber,
                ProductName = product.NameProduct,
                DocumentNumber = "—",
                DocumentName = "—"
            });
        }

        // Метод для добавления записи истории об окончательном удалении документа
        private static void AddPermanentDeleteHistoryForDocument(
            AppDbContext db,
            User user,
            Product? product,
            Document rootDocument,
            IReadOnlyList<ProductFile> files,
            DateTime changedAtUtc)
        {
            db.DocumentChangeHistory.Add(new DocumentChangeHistory
            {
                UserSurname = user.Surname,
                UserName = user.Name,
                UserPatronymic = user.Patronymic,
                Operation = HistoryOperationEnum.Окончательное_удаление,
                ChangedAt = changedAtUtc,
                FileName = files.Count == 1 ? files[0].FileName : $"({files.Count} файлов)",
                FilePath = files.Count == 1 ? (files[0].RecycleBinFilePath ?? "—") : "—",
                ProductNumber = product?.ProductNumber ?? "—",
                ProductName = product?.NameProduct ?? "—",
                DocumentNumber = rootDocument.DocumentNumber,
                DocumentName = rootDocument.NameDocument
            });
        }

        // Метод для добавления записи истории об окончательном удалении изделия
        private static void AddPermanentDeleteHistoryForProduct(
            AppDbContext db,
            User user,
            Product product,
            IReadOnlyList<ProductFile> files,
            DateTime changedAtUtc)
        {
            db.DocumentChangeHistory.Add(new DocumentChangeHistory
            {
                UserSurname = user.Surname,
                UserName = user.Name,
                UserPatronymic = user.Patronymic,
                Operation = HistoryOperationEnum.Окончательное_удаление,
                ChangedAt = changedAtUtc,
                FileName = files.Count == 1 ? files[0].FileName : $"({files.Count} файлов)",
                FilePath = files.Count == 1 ? (files[0].RecycleBinFilePath ?? "—") : "—",
                ProductNumber = product.ProductNumber,
                ProductName = product.NameProduct,
                DocumentNumber = "—",
                DocumentName = "—"
            });
        }

        // Метод для безопасного удаления файла с диска
        private static void TryDeletePhysicalFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        // Метод для получения иерархии объектов корзины
        public static async Task<List<TreeNode>> GetTreeItemsAsync()
        {
            await using AppDbContext db = new AppDbContext();

            List<Product> deletedProducts = await db.Products.AsNoTracking()
                .Where(x => x.Status == ProductStatusEnum.В_корзине)
                .OrderBy(x => x.NameProduct)
                .ThenBy(x => x.ProductNumber)
                .ToListAsync();

            List<Document> deletedDocuments = await db.Documents.AsNoTracking()
                .Where(x => x.Status == DocumentStatusEnum.В_корзине)
                .ToListAsync();

            List<long> deletedDocumentIds = deletedDocuments
                .Select(x => x.IdDocument)
                .ToList();

            List<ProductDocument> links = deletedDocumentIds.Count == 0
                ? new List<ProductDocument>()
                : await db.ProductDocuments.AsNoTracking()
                    .Where(x => deletedDocumentIds.Contains(x.IdDocument))
                    .ToListAsync();

            Dictionary<long, long> productIdByDocumentId = BuildProductIdByDocumentId(deletedDocuments, links);

            HashSet<long> referencedProductIds = productIdByDocumentId.Values.ToHashSet();
            foreach (Product product in deletedProducts)
            {
                referencedProductIds.Add(product.IdProduct);
            }

            List<Product> referencedProducts = referencedProductIds.Count == 0
                ? new List<Product>()
                : await db.Products.AsNoTracking()
                    .Where(x => referencedProductIds.Contains(x.IdProduct))
                    .ToListAsync();

            Dictionary<long, Product> productById = referencedProducts
                .GroupBy(x => x.IdProduct)
                .ToDictionary(x => x.Key, x => x.First());

            List<DocumentCategory> categories = await db.DocumentCategories.AsNoTracking()
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.NameDocumentCategory)
                .ToListAsync();

            Dictionary<long, DocumentCategory> categoryById = categories
                .ToDictionary(x => x.IdDocumentCategory, x => x);

            List<User> users = await db.Users.AsNoTracking().ToListAsync();
            Dictionary<long, User> userById = users.ToDictionary(x => x.IdUser, x => x);

            List<ProductFile> files = deletedDocumentIds.Count == 0
                ? new List<ProductFile>()
                : await db.Files.AsNoTracking()
                    .Where(x => deletedDocumentIds.Contains(x.IdDocument))
                    .ToListAsync();

            Dictionary<long, List<ProductFile>> filesByDocumentId = files
                .GroupBy(x => x.IdDocument)
                .ToDictionary(
                    x => x.Key,
                    x => x.OrderBy(f => f.FileName).ToList());

            Dictionary<long, List<Document>> childrenByParentId = deletedDocuments
                .Where(x => x.IdParentDocument.HasValue)
                .GroupBy(x => x.IdParentDocument!.Value)
                .ToDictionary(
                    x => x.Key,
                    x => x.OrderBy(d => d.DocumentNumber)
                          .ThenBy(d => d.NameDocument)
                          .ToList());

            HashSet<long> deletedProductIds = deletedProducts
                .Select(x => x.IdProduct)
                .ToHashSet();

            List<TreeNode> result = new List<TreeNode>();
            Dictionary<string, TreeNode> productNodeByKey = new Dictionary<string, TreeNode>();

            foreach (Product deletedProduct in deletedProducts)
            {
                string deletedByDisplayName = BuildUserDisplayName(
                    deletedProduct.DeletedBy.HasValue && userById.TryGetValue(deletedProduct.DeletedBy.Value, out User? productUser)
                        ? productUser
                        : null);

                TreeNode productNode = CreateRecycleProductNode(
                    deletedProduct,
                    isDeletedProduct: true,
                    deletedByDisplayName,
                    deletedProduct.DeletedAt);

                result.Add(productNode);
                productNodeByKey[BuildProductNodeKey(deletedProduct.IdProduct)] = productNode;

                List<Document> rootDocuments = deletedDocuments
                    .Where(x => x.IdParentDocument == null)
                    .Where(x => productIdByDocumentId.TryGetValue(x.IdDocument, out long productId) && productId == deletedProduct.IdProduct)
                    .OrderBy(x => x.DocumentNumber)
                    .ThenBy(x => x.NameDocument)
                    .ToList();

                AddRootDocumentsToProductNode(
                    productNode,
                    deletedProduct,
                    rootDocuments,
                    childrenByParentId,
                    filesByDocumentId,
                    categoryById,
                    userById,
                    RecycleBinObjectType.Product,
                    deletedProduct.IdProduct,
                    BuildProductDisplayName(deletedProduct),
                    deletedProduct.DeletedAt,
                    deletedByDisplayName,
                    applyTargetToCategoryNodes: true);
            }

            List<Document> individuallyDeletedRoots = deletedDocuments
                .Where(x => x.IsDeletedRoot)
                .OrderBy(x => x.DocumentNumber)
                .ThenBy(x => x.NameDocument)
                .ToList();

            foreach (Document rootDocument in individuallyDeletedRoots)
            {
                Product? product = null;

                if (productIdByDocumentId.TryGetValue(rootDocument.IdDocument, out long productId) &&
                    productById.TryGetValue(productId, out Product? foundProduct))
                {
                    if (deletedProductIds.Contains(productId))
                    {
                        continue;
                    }

                    product = foundProduct;
                }

                TreeNode productNode = GetOrCreateRecycleContainerProductNode(
                    result,
                    productNodeByKey,
                    product);

                string deletedByDisplayName = BuildUserDisplayName(
                    rootDocument.DeletedBy.HasValue && userById.TryGetValue(rootDocument.DeletedBy.Value, out User? documentUser)
                        ? documentUser
                        : null);

                AddRootDocumentsToProductNode(
                    productNode,
                    product,
                    new List<Document> { rootDocument },
                    childrenByParentId,
                    filesByDocumentId,
                    categoryById,
                    userById,
                    RecycleBinObjectType.Document,
                    rootDocument.IdDocument,
                    BuildDocumentDisplayName(rootDocument),
                    rootDocument.DeletedAt,
                    deletedByDisplayName,
                    applyTargetToCategoryNodes: false);
            }

            return result
                .OrderBy(x => x.DisplayName)
                .ToList();
        }

        // Метод для добавления корневых документов в узел изделия корзины
        private static void AddRootDocumentsToProductNode(
            TreeNode productNode,
            Product? product,
            List<Document> rootDocuments,
            IReadOnlyDictionary<long, List<Document>> childrenByParentId,
            IReadOnlyDictionary<long, List<ProductFile>> filesByDocumentId,
            IReadOnlyDictionary<long, DocumentCategory> categoryById,
            IReadOnlyDictionary<long, User> userById,
            RecycleBinObjectType targetObjectType,
            long targetObjectId,
            string targetObjectDisplayName,
            DateTime? deletedAtUtc,
            string deletedByDisplayName,
            bool applyTargetToCategoryNodes)
        {
            foreach (Document rootDocument in rootDocuments)
            {
                TreeNode categoryNode = GetOrCreateRecycleCategoryNode(productNode, rootDocument, categoryById);

                if (applyTargetToCategoryNodes)
                {
                    categoryNode.RecycleBinObjectType = targetObjectType;
                    categoryNode.RecycleBinObjectId = targetObjectId;
                    categoryNode.RecycleBinObjectDisplayName = targetObjectDisplayName;
                    categoryNode.DeletedAtUtc = deletedAtUtc;
                    categoryNode.DeletedByDisplayName = deletedByDisplayName;
                    categoryNode.SearchText = $"{categoryNode.SearchText} {targetObjectDisplayName} {deletedByDisplayName}".Trim();
                }

                TreeNode documentNode = BuildRecycleDocumentNodeRecursive(
                    product,
                    rootDocument,
                    childrenByParentId,
                    filesByDocumentId,
                    userById);

                // Для документа и его файлов целевым объектом делаем сам документ,
                // чтобы можно было удалять дочерний документ отдельно от родительского.
                categoryNode.Children.Add(documentNode);
            }
        }

        // Метод для получения или создания узла изделия-контейнера для корзины
        private static TreeNode GetOrCreateRecycleContainerProductNode(
            List<TreeNode> roots,
            IDictionary<string, TreeNode> productNodeByKey,
            Product? product)
        {
            string key = BuildProductNodeKey(product?.IdProduct);

            if (productNodeByKey.TryGetValue(key, out TreeNode? existingNode))
            {
                return existingNode;
            }

            TreeNode productNode = CreateRecycleProductNode(
                product,
                isDeletedProduct: false,
                deletedByDisplayName: string.Empty,
                deletedAtUtc: null);

            roots.Add(productNode);
            productNodeByKey[key] = productNode;

            return productNode;
        }

        // Метод для получения или создания узла категории внутри узла изделия корзины
        private static TreeNode GetOrCreateRecycleCategoryNode(
            TreeNode productNode,
            Document document,
            IReadOnlyDictionary<long, DocumentCategory> categoryById)
        {
            foreach (TreeNode child in productNode.Children)
            {
                if (child.NodeType == NodeType.Category &&
                    child.CategoryId == document.IdDocumentCategory)
                {
                    return child;
                }
            }

            string categoryName = categoryById.TryGetValue(document.IdDocumentCategory, out DocumentCategory? category)
                ? category.NameDocumentCategory
                : "Без категории";

            TreeNode categoryNode = new TreeNode
            {
                NodeType = NodeType.Category,
                DisplayName = categoryName,
                ProductId = productNode.ProductId,
                CategoryId = document.IdDocumentCategory,
                SearchText = $"{categoryName} {productNode.DisplayName}".Trim(),
                IsRecycleContainerOnly = true
            };

            productNode.Children.Add(categoryNode);
            return categoryNode;
        }

        // Метод для построения узла документа корзины с рекурсией
        private static TreeNode BuildRecycleDocumentNodeRecursive(
            Product? product,
            Document document,
            IReadOnlyDictionary<long, List<Document>> childrenByParentId,
            IReadOnlyDictionary<long, List<ProductFile>> filesByDocumentId,
            IReadOnlyDictionary<long, User> userById)
        {
            string documentDisplayName = BuildDocumentDisplayName(document);
            string deletedByDisplayName = BuildUserDisplayName(document.DeletedBy, userById);

            TreeNode documentNode = new TreeNode
            {
                NodeType = NodeType.Document,
                DisplayName = documentDisplayName,
                ProductId = product?.IdProduct,
                CategoryId = document.IdDocumentCategory,
                DocumentId = document.IdDocument,
                ParentDocumentId = document.IdParentDocument,
                RecycleBinObjectType = RecycleBinObjectType.Document,
                RecycleBinObjectId = document.IdDocument,
                RecycleBinObjectDisplayName = documentDisplayName,
                DeletedAtUtc = document.DeletedAt,
                DeletedByDisplayName = deletedByDisplayName,
                SearchText = $"{documentDisplayName} {deletedByDisplayName}".Trim()
            };

            if (filesByDocumentId.TryGetValue(document.IdDocument, out List<ProductFile>? files))
            {
                foreach (ProductFile file in files)
                {
                    string filePath = string.IsNullOrWhiteSpace(file.RecycleBinFilePath)
                        ? file.FilePath
                        : RecycleBinPathService.ResolveAbsolutePath(file.RecycleBinFilePath);

                    documentNode.Children.Add(new TreeNode
                    {
                        NodeType = NodeType.File,
                        DisplayName = file.FileName,
                        ProductId = product?.IdProduct,
                        CategoryId = document.IdDocumentCategory,
                        DocumentId = document.IdDocument,
                        FileId = file.IdFile,
                        FilePath = filePath,
                        ParentDocumentId = document.IdDocument,
                        RecycleBinObjectType = RecycleBinObjectType.Document,
                        RecycleBinObjectId = document.IdDocument,
                        RecycleBinObjectDisplayName = documentDisplayName,
                        DeletedAtUtc = document.DeletedAt,
                        DeletedByDisplayName = deletedByDisplayName,
                        SearchText = $"{file.FileName} {documentDisplayName} {deletedByDisplayName}".Trim()
                    });
                }
            }

            if (childrenByParentId.TryGetValue(document.IdDocument, out List<Document>? children))
            {
                foreach (Document child in children)
                {
                    documentNode.Children.Add(BuildRecycleDocumentNodeRecursive(
                        product,
                        child,
                        childrenByParentId,
                        filesByDocumentId,
                        userById));
                }
            }

            return documentNode;
        }

        // Метод для создания узла изделия корзины
        private static TreeNode CreateRecycleProductNode(
            Product? product,
            bool isDeletedProduct,
            string deletedByDisplayName,
            DateTime? deletedAtUtc)
        {
            TreeNode productNode = new TreeNode
            {
                NodeType = NodeType.Product,
                DisplayName = BuildProductDisplayName(product),
                ProductId = product?.IdProduct,
                DeletedAtUtc = deletedAtUtc,
                DeletedByDisplayName = deletedByDisplayName,
                SearchText = $"{BuildProductDisplayName(product)} {deletedByDisplayName}".Trim(),
                IsRecycleContainerOnly = !isDeletedProduct
            };

            if (isDeletedProduct && product != null)
            {
                productNode.RecycleBinObjectType = RecycleBinObjectType.Product;
                productNode.RecycleBinObjectId = product.IdProduct;
                productNode.RecycleBinObjectDisplayName = BuildProductDisplayName(product);
            }

            return productNode;
        }

        // Метод для построения ключа узла изделия корзины
        private static string BuildProductNodeKey(long? productId)
        {
            return productId.HasValue
                ? $"product:{productId.Value}"
                : "product:none";
        }

        // Метод для формирования отображаемого имени изделия
        private static string BuildProductDisplayName(Product? product)
        {
            if (product == null)
            {
                return "Без изделия";
            }

            if (string.IsNullOrWhiteSpace(product.ProductNumber))
            {
                return product.NameProduct;
            }

            if (string.IsNullOrWhiteSpace(product.NameProduct))
            {
                return product.ProductNumber;
            }

            return $"{product.ProductNumber} — {product.NameProduct}";
        }

        // Метод для формирования отображаемого имени документа
        private static string BuildDocumentDisplayName(Document document)
        {
            if (string.IsNullOrWhiteSpace(document.DocumentNumber))
            {
                return document.NameDocument;
            }

            if (string.IsNullOrWhiteSpace(document.NameDocument))
            {
                return document.DocumentNumber;
            }

            return $"{document.DocumentNumber} — {document.NameDocument}";
        }

        // Метод для формирования отображаемого ФИО пользователя
        private static string BuildUserDisplayName(User? user)
        {
            if (user == null)
            {
                return "—";
            }

            string patronymic = string.IsNullOrWhiteSpace(user.Patronymic)
                ? string.Empty
                : $" {user.Patronymic}";

            return $"{user.Surname} {user.Name}{patronymic}".Trim();
        }
    }
}