using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProductDossier.Data.Entities;
using ProductDossier.Data.Enums;
using ProductDossier.UI.Tree;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;

namespace ProductDossier.Data.Services
{
    // Сервис для работы с досье изделий, документами и файлами
    public static class DossierService
    {
        // Предпросмотр удаления документа (для окна подтверждения)
        public sealed record DocumentDeletionPreview(
            long DocumentId,
            string DocumentNumber,
            string DocumentName,
            string ProductNumber,
            string ProductName,
            int DocumentsToDeleteCount,
            int FilesToDeleteCount);

        // Предпросмотр удаления изделия (для окна подтверждения)
        public sealed record ProductDeletionPreview(
            long ProductId,
            string ProductNumber,
            string ProductName,
            int DocumentsToDeleteCount,
            int FilesToDeleteCount);

        // Метод для получения корневой папки хранения документов на диске
        public static string GetProductsRootFolder()
        {
            return ProductsRootPathService.GetRootFolder();
        }

        // Метод для преобразования относительного пути в абсолютный
        public static string ResolveAbsolutePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return string.Empty;

            string realPath = filePath;

            if (!Path.IsPathRooted(realPath))
            {
                try
                {
                    realPath = Path.Combine(GetProductsRootFolder(), realPath);
                }
                catch
                {
                }
            }

            return realPath;
        }

        // Метод для открытия файла в стандартной программе Windows
        public static void OpenFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                MessageBox.Show("Путь к файлу пустой.", "Открытие файла", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string realPath = ResolveAbsolutePath(filePath);

            if (!Path.IsPathRooted(realPath))
            {
                try
                {
                    realPath = Path.Combine(GetProductsRootFolder(), realPath);
                }
                catch
                {
                }
            }

            if (!File.Exists(realPath))
            {
                MessageBox.Show($"Файл не найден:\n{realPath}", "Открытие файла", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(realPath)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть файл:\n{ex.Message}", "Открытие файла", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для логирования внешнего редактирования файла
        public static async Task LogExternalFileEditAsync(long fileId, long currentUserId)
        {
            await using AppDbContext db = new AppDbContext();

            FileItem? file = await db.Files.FirstOrDefaultAsync(f => f.IdFile == fileId);
            if (file == null)
                return;

            Document? doc = await db.Documents.FirstOrDefaultAsync(d => d.IdDocument == file.IdDocument);
            if (doc == null)
                return;

            User? user = await db.Users.FirstOrDefaultAsync(u => u.IdUser == currentUserId);
            if (user == null)
                return;

            var link = await db.ProductDocuments.FirstOrDefaultAsync(pd => pd.IdDocument == doc.IdDocument);
            Product? product = link == null
                ? null
                : await db.Products.FirstOrDefaultAsync(p => p.IdProduct == link.IdProduct);

            // Обновление метаданных файла (размер/last_modified)
            try
            {
                string realPath = ResolveFilePathFromDb(file.FilePath);
                if (product != null)
                    realPath = ResolveFilePathSmart(product, file);

                if (!string.IsNullOrWhiteSpace(realPath) && File.Exists(realPath))
                {
                    FileInfo fi = new FileInfo(realPath);

                    file.FileSizeBytes = fi.Length;
                    file.LastModifiedAt = DateTime.UtcNow;
                }
                else
                {
                    file.LastModifiedAt = DateTime.UtcNow;
                }
            }
            catch
            {
                file.LastModifiedAt = DateTime.UtcNow;
            }

            DateTime nowUtc = DateTime.UtcNow;

            db.DocumentChangeHistory.Add(new DocumentChangeHistory
            {
                UserSurname = user.Surname,
                UserName = user.Name,
                UserPatronymic = user.Patronymic,

                Operation = HistoryOperationEnum.Редактирование,
                ChangedAt = nowUtc,

                FileName = file.FileName,
                FilePath = file.FilePath,

                ProductNumber = product?.ProductNumber ?? "—",
                ProductName = product?.NameProduct ?? "—",
                DocumentNumber = doc.DocumentNumber,
                DocumentName = doc.NameDocument
            });

            await db.SaveChangesAsync();
        }

        // Метод для открытия файла по узлу дерева
        public static void OpenFile(TreeNode node)
        {
            if (node == null)
            {
                MessageBox.Show("Узел файла не выбран.", "Открытие файла", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            OpenFile(node.FilePath);
        }

        // Метод для безопасного имени папки/файла
        private static string SanitizeFolderName(string name)
        {
            string invalid = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            string pattern = $"[{invalid}]";
            string cleaned = Regex.Replace(name, pattern, "_");
            cleaned = cleaned.Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "Без_названия" : cleaned;
        }

        // Метод для получения набора вариантов папки изделия
        private static List<string> GetProductFolderCandidates(Product product)
        {
            string root = GetProductsRootFolder();

            string newFolder = SanitizeFolderName(product.NameProduct);

            string oldFolder = SanitizeFolderName($"{product.ProductNumber} - {product.NameProduct}");

            string fallbackFolder = SanitizeFolderName(string.IsNullOrWhiteSpace(product.NameProduct)
                ? product.ProductNumber
                : product.NameProduct);

            var result = new List<string>
            {
                Path.Combine(root, newFolder),
                Path.Combine(root, oldFolder),
                Path.Combine(root, fallbackFolder)
            };

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Метод для формирования папки изделия
        private static string BuildProductFolder(Product product)
        {
            string root = GetProductsRootFolder();

            string folderName = SanitizeFolderName(product.NameProduct);

            if (string.IsNullOrWhiteSpace(folderName) || folderName == "Без_названия")
            {
                folderName = SanitizeFolderName(product.ProductNumber);
            }

            string productFolder = Path.Combine(root, folderName);
            Directory.CreateDirectory(productFolder);
            return productFolder;
        }

        // Метод для преобразования пути из БД в абсолютный путь на текущем ПК
        private static string ResolveFilePathFromDb(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                return dbPath;
            }

            if (Path.IsPathRooted(dbPath))
            {
                return dbPath;
            }

            string root = GetProductsRootFolder();
            return Path.Combine(root, dbPath);
        }

        // Метод для получения данных о файле для записи в историю на уровне документа
        private static (string FileName, string FilePath) GetHistoryFileInfoForDocument(List<FileItem> docFiles)
        {
            if (docFiles == null || docFiles.Count == 0)
                return ("—", "—");

            if (docFiles.Count == 1)
                return (docFiles[0].FileName, docFiles[0].FilePath);

            var first = docFiles
                .OrderBy(f => f.FileName)
                .First();

            return ($"({docFiles.Count} файлов)", first.FilePath);
        }

        // Метод для подбора реального пути к файлу с учётом структуры хранения
        private static string ResolveFilePathSmart(Product product, FileItem file)
        {
            string real = ResolveFilePathFromDb(file.FilePath);
            if (!string.IsNullOrWhiteSpace(real) && File.Exists(real))
            {
                return real;
            }

            List<string> candidates = GetProductFolderCandidates(product);

            foreach (string folder in candidates)
            {
                try
                {
                    string candidatePath = Path.Combine(folder, file.FileName);
                    if (File.Exists(candidatePath))
                    {
                        return candidatePath;
                    }
                }
                catch
                {
                }
            }

            return real;
        }

        // Метод для подготовки пути для сохранения в БД
        private static string PreparePathForDb(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return absolutePath;
            }

            try
            {
                string root = GetProductsRootFolder();
                string rel = Path.GetRelativePath(root, absolutePath);

                if (!rel.StartsWith(".."))
                {
                    return rel;
                }
            }
            catch
            {
            }

            return absolutePath;
        }

        // Метод для проверки прав на замену файла
        private static async Task<bool> CanReplaceFileAsync()
        {
            await using var conn = new Npgsql.NpgsqlConnection(DbConnectionManager.ConnectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        SELECT
            pg_has_role(current_user, 'pd_admin', 'member')
            OR pg_has_role(current_user, 'pd_superadmin', 'member');
    ";

            object? result = await cmd.ExecuteScalarAsync();
            return result is bool b && b;
        }

        // Метод для загрузки дерева документов по фильтрам
        public static async Task<List<TreeNode>> LoadTreeAsync(
            string? productSearch,
            ProductStatusEnum? productStatus,
            long? categoryId,
            string? documentSearch,
            DocumentStatusEnum? documentStatus)
        {
            await using AppDbContext db = new AppDbContext();

            bool hasDocumentFilters =
                categoryId != null ||
                documentStatus != null ||
                !string.IsNullOrWhiteSpace(documentSearch);

            IQueryable<Product> productsQuery = db.Products.AsNoTracking()
                .Where(p => p.Status != ProductStatusEnum.В_корзине);

            if (!string.IsNullOrWhiteSpace(productSearch))
            {
                string s = productSearch.Trim();
                productsQuery = productsQuery.Where(p =>
                    p.ProductNumber.Contains(s) ||
                    p.NameProduct.Contains(s));
            }

            if (productStatus != null)
            {
                productsQuery = productsQuery.Where(p => p.Status == productStatus);
            }

            List<Product> products = await productsQuery
                .OrderBy(p => p.ProductNumber)
                .ThenBy(p => p.NameProduct)
                .ToListAsync();

            List<DocumentCategory> categories = await db.DocumentCategories.AsNoTracking()
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.NameDocumentCategory)
                .ToListAsync();

            List<ProductDocument> links = await db.ProductDocuments.AsNoTracking().ToListAsync();
            List<Document> allDocs = await db.Documents.AsNoTracking()
                .Where(d => d.Status != DocumentStatusEnum.В_корзине)
                .ToListAsync();
            List<FileItem> allFiles = await db.Files.AsNoTracking().ToListAsync();

            List<Document> filteredDocs = allDocs;

            if (categoryId != null)
            {
                filteredDocs = filteredDocs.Where(d => d.IdDocumentCategory == categoryId.Value).ToList();
            }

            if (documentStatus != null)
            {
                filteredDocs = filteredDocs.Where(d => d.Status == documentStatus.Value).ToList();
            }

            if (!string.IsNullOrWhiteSpace(documentSearch))
            {
                string s = documentSearch.Trim();
                filteredDocs = filteredDocs.Where(d =>
                    d.DocumentNumber.Contains(s) ||
                    d.NameDocument.Contains(s)).ToList();
            }

            List<TreeNode> tree = new List<TreeNode>();

            foreach (Product p in products)
            {
                List<long> docIdsForProduct = links
                    .Where(l => l.IdProduct == p.IdProduct)
                    .Select(l => l.IdDocument)
                    .ToList();

                List<Document> docsForProduct = filteredDocs
                    .Where(d => docIdsForProduct.Contains(d.IdDocument))
                    .ToList();

                if (hasDocumentFilters && docsForProduct.Count == 0)
                {
                    continue;
                }

                string productTitle = string.IsNullOrWhiteSpace(p.NameProduct) ? p.ProductNumber : p.NameProduct;

                var productNode = new TreeNode
                {
                    NodeType = NodeType.Product,
                    DisplayName = productTitle,
                    ProductId = p.IdProduct
                };

                foreach (DocumentCategory cat in categories)
                {
                    var docsInCategory = docsForProduct
                        .Where(d => d.IdDocumentCategory == cat.IdDocumentCategory)
                        .ToList();

                    if (hasDocumentFilters && docsInCategory.Count == 0)
                    {
                        continue;
                    }

                    var catNode = new TreeNode
                    {
                        NodeType = NodeType.Category,
                        DisplayName = cat.NameDocumentCategory,
                        ProductId = p.IdProduct,
                        CategoryId = cat.IdDocumentCategory
                    };

                    var roots = docsInCategory
                        .Where(d => d.IdParentDocument == null)
                        .OrderBy(d => d.DocumentNumber)
                        .ThenBy(d => d.NameDocument)
                        .ToList();

                    foreach (var doc in roots)
                    {
                        catNode.Children.Add(BuildDocumentNodeRecursive(p, doc, docsInCategory, allFiles));
                    }

                    if (catNode.Children.Count == 0)
                    {
                        catNode.Children.Add(new TreeNode
                        {
                            NodeType = NodeType.Document,
                            DisplayName = "Нет документов",
                            ProductId = p.IdProduct,
                            CategoryId = cat.IdDocumentCategory
                        });
                    }

                    productNode.Children.Add(catNode);
                }

                tree.Add(productNode);
            }

            return tree;
        }

        // Метод для построения узла документа с рекурсией по дочерним документам
        private static TreeNode BuildDocumentNodeRecursive(
            Product product,
            Document doc,
            List<Document> scope,
            List<FileItem> allFiles)
        {
            var docNode = new TreeNode
            {
                NodeType = NodeType.Document,
                DisplayName = $"{doc.DocumentNumber} — {doc.NameDocument}",
                ProductId = product.IdProduct,
                CategoryId = doc.IdDocumentCategory,
                DocumentId = doc.IdDocument,

                ParentDocumentId = doc.IdParentDocument
            };

            var files = allFiles
                .Where(f => f.IdDocument == doc.IdDocument)
                .OrderBy(f => f.FileName)
                .ToList();

            foreach (var f in files)
            {
                docNode.Children.Add(new TreeNode
                {
                    NodeType = NodeType.File,
                    DisplayName = f.FileName,
                    ProductId = product.IdProduct,
                    CategoryId = doc.IdDocumentCategory,
                    DocumentId = doc.IdDocument,
                    FileId = f.IdFile,
                    FilePath = ResolveFilePathSmart(product, f),

                    ParentDocumentId = doc.IdParentDocument
                });
            }

            var children = scope
                .Where(d => d.IdParentDocument == doc.IdDocument)
                .OrderBy(d => d.DocumentNumber)
                .ThenBy(d => d.NameDocument)
                .ToList();

            foreach (var ch in children)
            {
                docNode.Children.Add(BuildDocumentNodeRecursive(product, ch, scope, allFiles));
            }

            return docNode;
        }

        // Метод для добавления категории документов
        public static async Task AddCategoryAsync(string name, string? description, int sortOrder)
        {
            string trimmedName = (name ?? string.Empty).Trim();
            string? trimmedDesc = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

            if (string.IsNullOrWhiteSpace(trimmedName))
                throw new ArgumentException("Название категории должно быть заполнено.");

            if (sortOrder < 0)
                throw new ArgumentException("Порядок сортировки не может быть отрицательным.");

            await using AppDbContext db = new AppDbContext();

            bool nameExists = await db.DocumentCategories.AsNoTracking()
                .AnyAsync(c => c.NameDocumentCategory == trimmedName);

            if (nameExists)
                throw new InvalidOperationException("Категория с таким названием уже существует.");

            bool sortOrderExists = await db.DocumentCategories.AsNoTracking()
                .AnyAsync(c => c.SortOrder == sortOrder);

            if (sortOrderExists)
                throw new InvalidOperationException("Категория с таким порядком сортировки уже существует.");

            DocumentCategory cat = new DocumentCategory
            {
                NameDocumentCategory = trimmedName,
                DescriptionDocumentCategory = trimmedDesc,
                SortOrder = sortOrder
            };

            db.DocumentCategories.Add(cat);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.GetBaseException() is PostgresException pg && pg.SqlState == "23505")
            {
                string detail = pg.Detail ?? string.Empty;

                if (detail.Contains("(sort_order)", StringComparison.OrdinalIgnoreCase) ||
                    (pg.ConstraintName?.Contains("sort_order", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    throw new InvalidOperationException("Категория с таким порядком сортировки уже существует.");
                }

                if (detail.Contains("(name_document_category)", StringComparison.OrdinalIgnoreCase) ||
                    (pg.ConstraintName?.Contains("name_document_category", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    throw new InvalidOperationException("Категория с таким названием уже существует.");
                }
                throw new InvalidOperationException("Категория с такими данными уже существует.");
            }
        }

        // Метод для добавления изделия (БД + создание папки на диске)
        public static async Task AddProductAsync(
            string productNumber,
            string productName,
            string? description,
            ProductStatusEnum status)
        {
            await using AppDbContext db = new AppDbContext();

            string number = productNumber?.Trim() ?? string.Empty;
            string name = productName?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(number))
                throw new ArgumentException("Номер изделия должен быть заполнен.");

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Название изделия должно быть заполнено.");

            bool exists = await db.Products.AnyAsync(p =>
                p.ProductNumber == number || p.NameProduct == name);

            if (exists)
                throw new InvalidOperationException("Изделие с таким номером или названием уже существует.");

            Product product = new Product
            {
                ProductNumber = number,
                NameProduct = name,
                DescriptionProduct = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                Status = status,

                CreatedAt = DateTime.UtcNow
            };

            db.Products.Add(product);
            await db.SaveChangesAsync();

            try
            {
                _ = BuildProductFolder(product);
            }
            catch
            {
                db.Products.Remove(product);
                await db.SaveChangesAsync();
                throw;
            }
        }

        // Метод для проверки уникальности номера и названия документа
        private static async Task EnsureDocumentNumberAndNameUniqueAsync(AppDbContext db, string documentNumber, string documentName)
        {
            string num = (documentNumber ?? string.Empty).Trim();
            string name = (documentName ?? string.Empty).Trim();

            bool numberExists = await db.Documents.AsNoTracking()
                .AnyAsync(d => d.DocumentNumber == num);

            if (numberExists)
                throw new InvalidOperationException("Документ с таким номером уже существует.");

            bool nameExists = await db.Documents.AsNoTracking()
                .AnyAsync(d => d.NameDocument == name);

            if (nameExists)
                throw new InvalidOperationException("Документ с таким названием уже существует.");
        }

        // Метод для проверки уникальности имени файла в рамках изделия
        private static async Task EnsureFileUniqueInProductAsync(AppDbContext db, long productId, string fullFileName)
        {
            string fn = (fullFileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fn))
                throw new ArgumentException("Имя файла пустое.");

            bool exists = await db.ProductDocuments.AsNoTracking()
                .Where(pd => pd.IdProduct == productId)
                .Join(db.Files.AsNoTracking(),
                      pd => pd.IdDocument,
                      f => f.IdDocument,
                      (pd, f) => f)
                .AnyAsync(f => EF.Functions.ILike(f.FileName, fn));

            if (exists)
                throw new InvalidOperationException($"Файл \"{fn}\" уже существует в этом изделии.");
        }

        // Метод для добавления файла в категорию
        public static async Task AddFileToCategoryAsync(
            long productId,
            long categoryId,
            long currentUserId,
            long? parentDocumentId,
            string documentNumber,
            string documentName,
            DocumentStatusEnum status,
            string sourceFilePath)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
                throw new FileNotFoundException("Выбранный файл не найден.", sourceFilePath);

            string docNumber = documentNumber?.Trim() ?? string.Empty;
            string docName = documentName?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(docNumber))
                throw new ArgumentException("Номер документа должен быть заполнен.", nameof(documentNumber));

            if (string.IsNullOrWhiteSpace(docName))
                throw new ArgumentException("Название документа должно быть заполнено.", nameof(documentName));

            string pickedFileName = Path.GetFileName(sourceFilePath);
            if (string.IsNullOrWhiteSpace(pickedFileName))
                throw new InvalidOperationException("Не удалось определить имя выбранного файла.");

            await using AppDbContext db = new AppDbContext();

            Product? product = await db.Products.FirstOrDefaultAsync(p => p.IdProduct == productId);
            if (product == null)
                throw new InvalidOperationException("Изделие не найдено.");

            DocumentCategory? cat = await db.DocumentCategories.FirstOrDefaultAsync(c => c.IdDocumentCategory == categoryId);
            if (cat == null)
                throw new InvalidOperationException("Категория документов не найдена.");

            User? user = await db.Users.FirstOrDefaultAsync(u => u.IdUser == currentUserId);
            if (user == null)
                throw new InvalidOperationException("Текущий пользователь не найден в таблице users.");

            await EnsureDocumentNumberAndNameUniqueAsync(db, docNumber, docName);

            await EnsureFileUniqueInProductAsync(db, productId, pickedFileName);

            string productFolder = BuildProductFolder(product);

            string destPath = Path.Combine(productFolder, pickedFileName);

            if (File.Exists(destPath))
            {
                throw new InvalidOperationException($"Файл \"{pickedFileName}\" уже существует в папке изделия. Выберите другой файл или переименуйте его.");
            }

            bool copied = false;

            await using var tx = await db.Database.BeginTransactionAsync();

            try
            {
                File.Copy(sourceFilePath, destPath);
                copied = true;

                FileInfo fi = new FileInfo(destPath);
                string savedFileName = Path.GetFileName(destPath);
                string pathForDb = PreparePathForDb(destPath);

                DateTime nowUtc = DateTime.UtcNow;

                Document doc = new Document
                {
                    IdDocumentCategory = categoryId,
                    IdParentDocument = parentDocumentId,
                    IdResponsibleUser = currentUserId,
                    DocumentNumber = docNumber,
                    NameDocument = docName,
                    Status = status
                };

                ProductDocument link = new ProductDocument
                {
                    IdProduct = productId,
                    Document = doc
                };

                FileItem file = new FileItem
                {
                    Document = doc,
                    IdUploadedBy = currentUserId,

                    FileName = savedFileName,
                    FilePath = pathForDb,
                    FileExtension = fi.Extension,
                    FileSizeBytes = fi.Length,

                    UploadedAt = nowUtc,
                    LastModifiedAt = nowUtc
                };

                DocumentChangeHistory history = new DocumentChangeHistory
                {
                    UserSurname = user.Surname,
                    UserName = user.Name,
                    UserPatronymic = user.Patronymic,
                    Operation = HistoryOperationEnum.Добавление,
                    ChangedAt = nowUtc,

                    FileName = file.FileName,
                    FilePath = file.FilePath,
                    ProductNumber = product.ProductNumber,
                    ProductName = product.NameProduct,
                    DocumentNumber = doc.DocumentNumber,
                    DocumentName = doc.NameDocument
                };

                db.Documents.Add(doc);
                db.ProductDocuments.Add(link);
                db.Files.Add(file);
                db.DocumentChangeHistory.Add(history);

                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (ex.GetBaseException() is PostgresException pg && pg.SqlState == "23505")
                {
                    string detail = pg.Detail ?? string.Empty;
                    string constraint = pg.ConstraintName ?? string.Empty;

                    if (detail.Contains("(document_number)", StringComparison.OrdinalIgnoreCase) ||
                        constraint.Contains("document_number", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Документ с таким номером уже существует.");
                    }

                    if (detail.Contains("(name_document)", StringComparison.OrdinalIgnoreCase) ||
                        constraint.Contains("name_document", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Документ с таким названием уже существует.");
                    }

                    throw new InvalidOperationException("Запись с такими данными уже существует.");
                }

                await tx.CommitAsync();
            }
            catch
            {
                try { await tx.RollbackAsync(); } catch { }

                if (copied)
                {
                    try
                    {
                        if (File.Exists(destPath))
                            File.Delete(destPath);
                    }
                    catch
                    {
                    }
                }

                throw;
            }
        }

        // Метод для перемещения файла в Recycle bin через связанный документ
        public static async Task DeleteFileAsync(long fileId, long currentUserId)
        {
            await RecycleBinService.MoveFileToRecycleBinAsync(fileId, currentUserId);
        }

        // DTO для окна "Свойства"
        public sealed class FileDetailsDto
        {
            public required Product Product { get; init; }
            public required Document Document { get; init; }
            public required FileItem File { get; init; }

            public DocumentCategory? Category { get; init; }
            public User? ResponsibleUser { get; init; }
            public User? UploadedByUser { get; init; }
            public Document? ParentDocument { get; init; }

            public required string ResolvedFilePath { get; init; }
        }

        // Метод для получения всех данных о файле/документе для окна "Свойства"
        public static async Task<FileDetailsDto> GetFileDetailsAsync(long fileId)
        {
            await using AppDbContext db = new AppDbContext();

            var file = await db.Files.FirstOrDefaultAsync(f => f.IdFile == fileId);
            if (file == null)
                throw new InvalidOperationException("Файл не найден в БД.");

            var doc = await db.Documents.FirstOrDefaultAsync(d => d.IdDocument == file.IdDocument);
            if (doc == null)
                throw new InvalidOperationException("Документ не найден в БД.");

            var link = await db.ProductDocuments.FirstOrDefaultAsync(pd => pd.IdDocument == doc.IdDocument);
            if (link == null)
                throw new InvalidOperationException("Не найдена связь документа с изделием (product_documents).");

            var product = await db.Products.FirstOrDefaultAsync(p => p.IdProduct == link.IdProduct);
            if (product == null)
                throw new InvalidOperationException("Изделие не найдено в БД.");

            DocumentCategory? cat = await db.DocumentCategories
                .FirstOrDefaultAsync(c => c.IdDocumentCategory == doc.IdDocumentCategory);

            User? responsible = await db.Users.FirstOrDefaultAsync(u => u.IdUser == doc.IdResponsibleUser);

            User? uploadedBy = await db.Users.FirstOrDefaultAsync(u => u.IdUser == file.IdUploadedBy);

            Document? parentDoc = null;
            if (doc.IdParentDocument.HasValue)
            {
                parentDoc = await db.Documents.FirstOrDefaultAsync(d => d.IdDocument == doc.IdParentDocument.Value);
            }

            return new FileDetailsDto
            {
                Product = product,
                Document = doc,
                File = file,
                Category = cat,
                ResponsibleUser = responsible,
                UploadedByUser = uploadedBy,
                ParentDocument = parentDoc,
                ResolvedFilePath = ResolveFilePathSmart(product, file)
            };
        }

        // Метод для смены категории документа
        public static async Task ChangeDocumentCategoryAsync(
            long documentId,
            long newCategoryId,
            long currentUserId)
        {
            await using AppDbContext db = new AppDbContext();
            await using var tx = await db.Database.BeginTransactionAsync();

            var root = await db.Documents.FirstOrDefaultAsync(d => d.IdDocument == documentId);
            if (root == null)
                throw new InvalidOperationException("Документ не найден.");

            if (root.IdParentDocument != null)
                throw new InvalidOperationException("Категорию можно менять только у корневого документа.");

            bool catExists = await db.DocumentCategories.AnyAsync(c => c.IdDocumentCategory == newCategoryId);
            if (!catExists)
                throw new InvalidOperationException("Выбранная категория не найдена.");

            if (root.IdDocumentCategory == newCategoryId)
                return;

            var user = await db.Users.FirstOrDefaultAsync(u => u.IdUser == currentUserId);
            if (user == null)
                throw new InvalidOperationException("Текущий пользователь не найден в таблице users.");

            // получаем изделие (для логов)
            var rootLink = await db.ProductDocuments.FirstOrDefaultAsync(pd => pd.IdDocument == root.IdDocument);
            if (rootLink == null)
                throw new InvalidOperationException("Не найдена связь документа с изделием (product_documents).");

            var product = await db.Products.FirstOrDefaultAsync(p => p.IdProduct == rootLink.IdProduct);
            if (product == null)
                throw new InvalidOperationException("Изделие не найдено.");

            var docIdsForProduct = await db.ProductDocuments
                .Where(pd => pd.IdProduct == rootLink.IdProduct)
                .Select(pd => pd.IdDocument)
                .ToListAsync();

            var docsForProduct = await db.Documents
                .Where(d => docIdsForProduct.Contains(d.IdDocument))
                .ToListAsync();

            var childrenMap = docsForProduct
                .Where(d => d.IdParentDocument.HasValue)
                .GroupBy(d => d.IdParentDocument!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var toUpdate = new List<Document>();
            var visited = new HashSet<long>();
            var stack = new Stack<Document>();

            var rootTracked = docsForProduct.FirstOrDefault(d => d.IdDocument == documentId) ?? root;
            stack.Push(rootTracked);

            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (!visited.Add(cur.IdDocument))
                    continue;

                toUpdate.Add(cur);

                if (childrenMap.TryGetValue(cur.IdDocument, out var kids))
                {
                    foreach (var k in kids)
                        stack.Push(k);
                }
            }

            DateTime nowUtc = DateTime.UtcNow;

            var toUpdateIds = toUpdate.Select(d => d.IdDocument).ToList();

            var filesForBranch = await db.Files
                .Where(f => toUpdateIds.Contains(f.IdDocument))
                .ToListAsync();

            var filesByDocId = filesForBranch
                .GroupBy(f => f.IdDocument)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var d in toUpdate)
            {
                d.IdDocumentCategory = newCategoryId;

                filesByDocId.TryGetValue(d.IdDocument, out var docFiles);
                docFiles ??= new List<FileItem>();

                var (fileName, filePath) = GetHistoryFileInfoForDocument(docFiles);

                db.DocumentChangeHistory.Add(new DocumentChangeHistory
                {
                    UserSurname = user.Surname,
                    UserName = user.Name,
                    UserPatronymic = user.Patronymic,

                    Operation = HistoryOperationEnum.Изменение_категории_документа,
                    ChangedAt = nowUtc,

                    FileName = fileName,
                    FilePath = filePath,

                    ProductNumber = product.ProductNumber,
                    ProductName = product.NameProduct,

                    DocumentNumber = d.DocumentNumber,
                    DocumentName = d.NameDocument
                });
            }

            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // Метод для редактирования документа и (опционально) замены физического файла
        public static async Task UpdateFileAsync(
            long fileId,
            long currentUserId,
            string documentNumber,
            string documentName,
            DocumentStatusEnum status,
            string? newSourceFilePath)
        {
            await using AppDbContext db = new AppDbContext();

            var file = await db.Files.FirstOrDefaultAsync(f => f.IdFile == fileId);
            if (file == null)
                throw new InvalidOperationException("Файл не найден.");

            var doc = await db.Documents.FirstOrDefaultAsync(d => d.IdDocument == file.IdDocument);
            if (doc == null)
                throw new InvalidOperationException("Документ не найден.");

            var link = await db.ProductDocuments.FirstOrDefaultAsync(pd => pd.IdDocument == doc.IdDocument);
            if (link == null)
                throw new InvalidOperationException("Не найдена связь документа с изделием (product_documents).");

            var product = await db.Products.FirstOrDefaultAsync(p => p.IdProduct == link.IdProduct);
            if (product == null)
                throw new InvalidOperationException("Изделие не найдено.");

            var user = await db.Users.FirstOrDefaultAsync(u => u.IdUser == currentUserId);
            if (user == null)
                throw new InvalidOperationException("Текущий пользователь не найден в таблице users.");

            string docNumber = documentNumber?.Trim() ?? string.Empty;
            string docName = documentName?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(docNumber))
                throw new ArgumentException("Номер документа должен быть заполнен.");

            if (string.IsNullOrWhiteSpace(docName))
                throw new ArgumentException("Название документа должно быть заполнено.");

            doc.DocumentNumber = docNumber;
            doc.NameDocument = docName;
            doc.Status = status;

            string? oldResolvedPath = null;
            string? tempOrNewPath = null;

            bool replaceInPlaceSameName = false;

            if (!string.IsNullOrWhiteSpace(newSourceFilePath))
            {
                if (!await CanReplaceFileAsync())
                    throw new UnauthorizedAccessException("Заменять файл может только Администратор или Супер-администратор.");

                if (!File.Exists(newSourceFilePath))
                    throw new FileNotFoundException("Выбранный файл не найден.", newSourceFilePath);

                string newFileName = Path.GetFileName(newSourceFilePath);
                if (string.IsNullOrWhiteSpace(newFileName))
                    throw new InvalidOperationException("Не удалось определить имя выбранного файла.");

                oldResolvedPath = ResolveFilePathSmart(product, file);

                string productFolder = BuildProductFolder(product);

                bool sameName = string.Equals(file.FileName, newFileName, StringComparison.OrdinalIgnoreCase);

                if (sameName)
                {
                    string tempName = $"{Guid.NewGuid():N}{Path.GetExtension(newFileName)}";
                    string tempPath = Path.Combine(productFolder, tempName);

                    File.Copy(newSourceFilePath, tempPath, overwrite: false);
                    tempOrNewPath = tempPath;
                    replaceInPlaceSameName = true;

                    FileInfo fiTemp = new FileInfo(tempPath);
                    file.FileSizeBytes = fiTemp.Length;
                    file.LastModifiedAt = DateTime.UtcNow;

                    file.FileExtension = fiTemp.Extension;
                }
                else
                {
                    string newTarget = Path.Combine(productFolder, newFileName);

                    if (File.Exists(newTarget))
                        throw new InvalidOperationException($"Файл \"{newFileName}\" уже существует в папке изделия. Переименуйте файл и попробуйте снова.");

                    File.Copy(newSourceFilePath, newTarget, overwrite: false);
                    tempOrNewPath = newTarget;

                    string pathForDb = PreparePathForDb(newTarget);
                    FileInfo fi = new FileInfo(newTarget);

                    file.FileName = newFileName;
                    file.FilePath = pathForDb;
                    file.FileExtension = fi.Extension;
                    file.FileSizeBytes = fi.Length;
                    file.LastModifiedAt = DateTime.UtcNow;
                }
            }

            var op = string.IsNullOrWhiteSpace(newSourceFilePath)
                ? HistoryOperationEnum.Изменение_данных_документа
                : HistoryOperationEnum.Редактирование;

            db.DocumentChangeHistory.Add(new DocumentChangeHistory
            {
                UserSurname = user.Surname,
                UserName = user.Name,
                UserPatronymic = user.Patronymic,

                Operation = op,
                ChangedAt = DateTime.UtcNow,

                ProductNumber = product.ProductNumber,
                ProductName = product.NameProduct,

                DocumentNumber = doc.DocumentNumber,
                DocumentName = doc.NameDocument,

                FileName = file.FileName,
                FilePath = file.FilePath
            });

            try
            {
                await db.SaveChangesAsync();


                if (!string.IsNullOrWhiteSpace(newSourceFilePath))
                {
                    if (replaceInPlaceSameName)
                    {
                        if (string.IsNullOrWhiteSpace(oldResolvedPath))
                            throw new InvalidOperationException("Не удалось определить путь к текущему файлу на диске.");

                        File.Copy(tempOrNewPath!, oldResolvedPath, overwrite: true);

                        TryDeletePhysicalFile(tempOrNewPath!);
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(oldResolvedPath))
                            TryDeletePhysicalFile(oldResolvedPath);
                    }
                }
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(tempOrNewPath))
                    TryDeletePhysicalFile(tempOrNewPath);

                throw;
            }
        }

        // Метод для предпросмотра удаления документа
        public static async Task<DocumentDeletionPreview> GetDocumentDeletionPreviewAsync(long documentId)
        {
            await using AppDbContext db = new AppDbContext();

            Document doc = await db.Documents.AsNoTracking()
                .FirstOrDefaultAsync(d => d.IdDocument == documentId)
                ?? throw new InvalidOperationException("Документ не найден.");

            Product? product = null;
            var link = await db.ProductDocuments.AsNoTracking().FirstOrDefaultAsync(pd => pd.IdDocument == documentId);
            if (link != null)
            {
                product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.IdProduct == link.IdProduct);
            }

            List<long> ids = await GetDocumentSubtreeIdsAsync(db, documentId);

            int filesCount = await db.Files.AsNoTracking()
                .CountAsync(f => ids.Contains(f.IdDocument));

            return new DocumentDeletionPreview(
                DocumentId: doc.IdDocument,
                DocumentNumber: doc.DocumentNumber,
                DocumentName: doc.NameDocument,
                ProductNumber: product?.ProductNumber ?? "—",
                ProductName: product?.NameProduct ?? "—",
                DocumentsToDeleteCount: ids.Count,
                FilesToDeleteCount: filesCount);
        }

        // Метод для предпросмотра удаления изделия
        public static async Task<ProductDeletionPreview> GetProductDeletionPreviewAsync(long productId)
        {
            await using AppDbContext db = new AppDbContext();

            Product product = await db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.IdProduct == productId)
                ?? throw new InvalidOperationException("Изделие не найдено.");

            List<long> docIds = await GetProductDocumentClosureIdsAsync(db, productId);

            int filesCount = await db.Files.AsNoTracking()
                .CountAsync(f => docIds.Contains(f.IdDocument));

            return new ProductDeletionPreview(
                ProductId: product.IdProduct,
                ProductNumber: product.ProductNumber,
                ProductName: product.NameProduct,
                DocumentsToDeleteCount: docIds.Count,
                FilesToDeleteCount: filesCount);
        }

        // Метод для формирования данных файла для записи в историю при удалении документа
        private static (string FileName, string FilePath) BuildDeleteHistoryFileInfo(IReadOnlyList<FileItem>? filesForDoc)
        {
            if (filesForDoc == null || filesForDoc.Count == 0)
                return ("—", "—");

            if (filesForDoc.Count == 1)
                return (filesForDoc[0].FileName, filesForDoc[0].FilePath);

            return ($"({filesForDoc.Count} файлов)", "—");
        }

        // Метод для добавления записи истории удаления по документу
        private static void AddDeleteHistoryPerDocument(
            AppDbContext db,
            User user,
            DateTime nowUtc,
            Product? product,
            Document doc,
            IReadOnlyList<FileItem>? filesForDoc)
        {
            var (fileName, filePath) = BuildDeleteHistoryFileInfo(filesForDoc);

            db.DocumentChangeHistory.Add(new DocumentChangeHistory
            {
                UserSurname = user.Surname,
                UserName = user.Name,
                UserPatronymic = user.Patronymic,

                Operation = HistoryOperationEnum.Окончательное_удаление,
                ChangedAt = nowUtc,

                FileName = fileName,
                FilePath = filePath,

                ProductNumber = product?.ProductNumber ?? "—",
                ProductName = product?.NameProduct ?? "—",

                DocumentNumber = doc.DocumentNumber,
                DocumentName = doc.NameDocument
            });
        }

        // Метод для перемещения документа в Recycle bin.
        public static async Task DeleteDocumentAsync(long documentId, long currentUserId, DeletePermissionRole role)
        {
            await RecycleBinService.MoveDocumentToRecycleBinAsync(documentId, currentUserId, role);
        }

        // Метод для перемещения изделия в Recycle bin.
        public static async Task DeleteProductAsync(long productId, long currentUserId, DeletePermissionRole role)
        {
            await RecycleBinService.MoveProductToRecycleBinAsync(productId, currentUserId, role);
        }

        // Метод для получения идентификаторов поддерева документов
        private static async Task<List<long>> GetDocumentSubtreeIdsAsync(AppDbContext db, long rootDocumentId)
        {
            const string sql = @"
                WITH RECURSIVE t AS (
                    SELECT id_document
                    FROM documents
                    WHERE id_document = @root

                    UNION ALL

                    SELECT d.id_document
                    FROM documents d
                    JOIN t ON d.id_parent_document = t.id_document
                )
                SELECT id_document FROM t;
            ";

            return await ExecuteLongListQueryAsync(db, sql, ("root", rootDocumentId));
        }

        // Метод для получения замыкания документов изделия (корни + все дочерние)
        private static async Task<List<long>> GetProductDocumentClosureIdsAsync(AppDbContext db, long productId)
        {
            const string sql = @"
                WITH RECURSIVE t AS (
                    SELECT d.id_document
                    FROM documents d
                    JOIN product_documents pd ON pd.id_document = d.id_document
                    WHERE pd.id_product = @pid

                    UNION

                    SELECT d2.id_document
                    FROM documents d2
                    JOIN t ON d2.id_parent_document = t.id_document
                )
                SELECT DISTINCT id_document FROM t;
            ";

            return await ExecuteLongListQueryAsync(db, sql, ("pid", productId));
        }

        // Метод для выполнения SQL-запроса, возвращающего список long
        private static async Task<List<long>> ExecuteLongListQueryAsync(AppDbContext db, string sql, params (string Name, object Value)[] parameters)
        {
            bool openedHere = false;

            if (db.Database.GetDbConnection().State != ConnectionState.Open)
            {
                await db.Database.OpenConnectionAsync();
                openedHere = true;
            }

            try
            {
                await using var cmd = db.Database.GetDbConnection().CreateCommand();
                cmd.CommandText = sql;

                foreach (var (Name, Value) in parameters)
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = Name;
                    p.Value = Value;
                    cmd.Parameters.Add(p);
                }

                var result = new List<long>();

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                        result.Add(reader.GetInt64(0));
                }

                return result;
            }
            finally
            {
                if (openedHere)
                {
                    await db.Database.CloseConnectionAsync();
                }
            }
        }

        // Метод для упорядочивания документов "снизу вверх" для корректного удаления по FK parent_document
        private static List<Document> OrderDocumentsForDeletion(List<Document> docs)
        {
            if (docs.Count <= 1)
                return docs;

            var byId = docs.ToDictionary(d => d.IdDocument, d => d);

            var childrenMap = docs
                .Where(d => d.IdParentDocument.HasValue && byId.ContainsKey(d.IdParentDocument.Value))
                .GroupBy(d => d.IdParentDocument!.Value)
                .ToDictionary(g => g.Key, g => g.Select(x => x.IdDocument).ToList());

            var depthMemo = new Dictionary<long, int>();

            int Depth(long id, HashSet<long> stack)
            {
                if (depthMemo.TryGetValue(id, out int cached))
                    return cached;

                if (!childrenMap.TryGetValue(id, out var children) || children.Count == 0)
                {
                    depthMemo[id] = 0;
                    return 0;
                }

                if (!stack.Add(id))
                {
                    depthMemo[id] = 0;
                    return 0;
                }

                int max = 0;
                foreach (var ch in children)
                {
                    int d = 1 + Depth(ch, stack);
                    if (d > max) max = d;
                }

                stack.Remove(id);
                depthMemo[id] = max;
                return max;
            }

            foreach (var d in docs)
            {
                _ = Depth(d.IdDocument, new HashSet<long>());
            }

            return docs
                .OrderByDescending(d => depthMemo[d.IdDocument])
                .ThenByDescending(d => d.IdDocument)
                .ToList();
        }

        // Метод для удаления папки изделия, если она пустая
        private static void TryDeleteEmptyProductFolders(Product product)
        {
            foreach (string folder in GetProductFolderCandidates(product))
            {
                try
                {
                    if (!Directory.Exists(folder))
                        continue;

                    bool hasAny = Directory.EnumerateFileSystemEntries(folder).Any();
                    if (!hasAny)
                    {
                        Directory.Delete(folder, false);
                    }
                }
                catch
                {
                }
            }
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
    }
}

