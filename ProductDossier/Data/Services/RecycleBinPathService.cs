using ProductDossier.Data.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProductDossier.Data.Services
{
    // Сервис для работы с путями файлового хранилища корзины
    public static class RecycleBinPathService
    {
        // Метод для получения корневой папки корзины
        public static string GetRecycleBinRootFolder()
        {
            string recycleRoot = ProductsRootPathService.GetRecycleBinFolder();
            Directory.CreateDirectory(recycleRoot);
            return recycleRoot;
        }

        // Метод для получения абсолютного пути файла из значения корзины, сохранённого в БД
        public static string ResolveAbsolutePath(string pathFromDb)
        {
            if (string.IsNullOrWhiteSpace(pathFromDb))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(pathFromDb))
            {
                return pathFromDb;
            }

            return Path.Combine(GetRecycleBinRootFolder(), pathFromDb);
        }

        // Метод для подготовки пути корзины к сохранению в БД
        public static string PreparePathForDb(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return string.Empty;
            }

            try
            {
                string recycleRoot = GetRecycleBinRootFolder();
                string relative = Path.GetRelativePath(recycleRoot, absolutePath);

                if (!relative.StartsWith("..", StringComparison.Ordinal))
                {
                    return relative;
                }
            }
            catch
            {
            }

            return absolutePath;
        }

        // Метод для получения абсолютного пути папки изделия внутри корзины
        public static string GetRecycleProductFolderAbsolutePath(Product? product)
        {
            return Path.Combine(GetRecycleBinRootFolder(), BuildProductFolderName(product));
        }

        // Метод для получения абсолютного пути папки документов внутри папки изделия
        public static string GetRecycleDeletedDocumentsFolderAbsolutePath(Product? product)
        {
            return GetRecycleProductFolderAbsolutePath(product);
        }

        // Метод для получения абсолютного пути папки удалённого документа внутри корзины
        public static string GetRecycleDeletedDocumentFolderAbsolutePath(Product? product, Document rootDocument)
        {
            return Path.Combine(
                GetRecycleProductFolderAbsolutePath(product),
                BuildDocumentFolderName(rootDocument));
        }

        // Метод для построения целевого пути файла изделия внутри корзины
        public static string BuildRecycleProductFileAbsolutePath(string sourceAbsolutePath, Product? product)
        {
            string productFolder = GetRecycleProductFolderAbsolutePath(product);
            string relativeInsideProduct = BuildRelativePathInsideProduct(sourceAbsolutePath, product);
            return Path.Combine(productFolder, relativeInsideProduct);
        }

        // Метод для построения целевого пути файла документа внутри корзины
        public static string BuildRecycleDocumentFileAbsolutePath(
            string sourceAbsolutePath,
            Product? product,
            Document rootDocument)
        {
            string documentFolder = GetRecycleDeletedDocumentFolderAbsolutePath(product, rootDocument);
            string relativeInsideProduct = BuildRelativePathInsideProduct(sourceAbsolutePath, product);
            string fileName = Path.GetFileName(relativeInsideProduct);

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = Path.GetFileName(sourceAbsolutePath);
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "Файл";
            }

            return Path.Combine(documentFolder, fileName);
        }

        // Метод для поиска существующей папки изделия в основном хранилище
        public static string? TryGetExistingMainProductFolderAbsolutePath(Product product)
        {
            foreach (string candidate in GetMainProductFolderCandidates(product))
            {
                try
                {
                    if (Directory.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        // Метод для получения всех вариантов путей папки изделия в основном хранилище
        public static List<string> GetMainProductFolderCandidates(Product product)
        {
            string root = ProductsRootPathService.GetRootFolder();

            string newFolder = SanitizeFolderName(product.NameProduct);
            string oldFolder = SanitizeFolderName($"{product.ProductNumber} - {product.NameProduct}");
            string fallbackFolder = SanitizeFolderName(string.IsNullOrWhiteSpace(product.NameProduct)
                ? product.ProductNumber
                : product.NameProduct);

            return new List<string>
            {
                Path.Combine(root, newFolder),
                Path.Combine(root, oldFolder),
                Path.Combine(root, fallbackFolder)
            }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        }

        // Метод для удаления пустой папки удалённого документа и родительской папки изделия
        public static void TryDeleteEmptyDeletedDocumentFolders(Product? product, Document rootDocument)
        {
            TryDeleteEmptyDirectory(GetRecycleDeletedDocumentFolderAbsolutePath(product, rootDocument));
            TryDeleteEmptyDirectory(GetRecycleProductFolderAbsolutePath(product));
        }

        // Метод для удаления пустой папки изделия в корзине
        public static void TryDeleteEmptyRecycleProductFolder(Product? product)
        {
            TryDeleteEmptyDirectory(GetRecycleProductFolderAbsolutePath(product));
        }

        // Метод для определения принадлежности пути корзине
        public static bool IsPathInsideRecycleBin(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return false;
            }

            try
            {
                string recycleRoot = GetRecycleBinRootFolder()
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                string normalizedPath = Path.GetFullPath(absolutePath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                return normalizedPath.StartsWith(recycleRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalizedPath, recycleRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // Метод для формирования имени папки изделия
        private static string BuildProductFolderName(Product? product)
        {
            if (product == null)
            {
                return "Без_изделия";
            }

            string rawName = string.IsNullOrWhiteSpace(product.NameProduct)
                ? product.ProductNumber
                : product.NameProduct;

            return SanitizeFolderName(rawName);
        }

        // Метод для формирования имени папки документа
        private static string BuildDocumentFolderName(Document rootDocument)
        {
            string rawName = string.IsNullOrWhiteSpace(rootDocument.DocumentNumber)
                ? rootDocument.NameDocument
                : string.IsNullOrWhiteSpace(rootDocument.NameDocument)
                    ? rootDocument.DocumentNumber
                    : $"{rootDocument.DocumentNumber} - {rootDocument.NameDocument}";

            return SanitizeFolderName(rawName);
        }

        // Метод для формирования относительного пути файла внутри папки изделия
        private static string BuildRelativePathInsideProduct(string sourceAbsolutePath, Product? product)
        {
            if (string.IsNullOrWhiteSpace(sourceAbsolutePath))
            {
                return "Файл";
            }

            if (product != null)
            {
                foreach (string candidate in GetMainProductFolderCandidates(product))
                {
                    try
                    {
                        string relative = Path.GetRelativePath(candidate, sourceAbsolutePath);
                        if (!relative.StartsWith("..", StringComparison.Ordinal))
                        {
                            return relative;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            try
            {
                string rootFolder = ProductsRootPathService.GetRootFolder();
                string relative = Path.GetRelativePath(rootFolder, sourceAbsolutePath);

                if (relative.StartsWith("..", StringComparison.Ordinal))
                {
                    return Path.GetFileName(sourceAbsolutePath);
                }

                string[] parts = relative.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length <= 1)
                {
                    return Path.GetFileName(sourceAbsolutePath);
                }

                string[] insideProductParts = new string[parts.Length - 1];
                Array.Copy(parts, 1, insideProductParts, 0, insideProductParts.Length);

                return Path.Combine(insideProductParts);
            }
            catch
            {
                return Path.GetFileName(sourceAbsolutePath);
            }
        }

        // Метод для подготовки безопасного имени папки
        private static string SanitizeFolderName(string? value)
        {
            string safeValue = (value ?? string.Empty).Trim();
            string invalid = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            safeValue = Regex.Replace(safeValue, $"[{invalid}]", "_");
            return string.IsNullOrWhiteSpace(safeValue) ? "Без_названия" : safeValue;
        }

        // Метод для удаления директории, если она пуста
        private static void TryDeleteEmptyDirectory(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    return;
                }

                if (!Directory.EnumerateFileSystemEntries(path).Any())
                {
                    Directory.Delete(path, false);
                }
            }
            catch
            {
            }
        }
    }
}