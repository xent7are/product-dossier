using System;
using System.IO;
using System.Linq;

namespace ProductDossier.Data.Services
{
    // Сервис для управления путями хранения изделий и Recycle bin
    public static class ProductsRootPathService
    {
        private const string ConfigFileName = "ProductsRootPath.txt";
        private const string ProjectFileName = "ProductDossier.csproj";
        private const string NetworkPathsComment = "Пути для сохранения файлов на сетевой диск:";
        private const string LocalPathsComment = "Резервные локальные пути для сохранения файлов на этом компьютере, если не удалось подключиться к сетевому диску:";

        // Метод для получения пути к конфигурационному файлу в папке проекта
        private static string GetConfigPathInProjectFolder()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            DirectoryInfo? dir = new DirectoryInfo(baseDir);

            while (dir != null)
            {
                string csprojPath = Path.Combine(dir.FullName, ProjectFileName);
                if (File.Exists(csprojPath))
                {
                    return Path.Combine(dir.FullName, ConfigFileName);
                }

                dir = dir.Parent;
            }

            return string.Empty;
        }

        // Метод для получения пути к конфигурационному файлу в LocalAppData
        private static string GetConfigPathInAppData()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ProductDossier");

            return Path.Combine(dir, ConfigFileName);
        }

        // Сетевой путь по умолчанию для изделий
        private static string GetDefaultNetworkRootFolder()
        {
            return @"X:\Дела изделий\Изделия";
        }

        // Сетевой путь по умолчанию для корзины
        private static string GetDefaultNetworkRecycleBinFolder()
        {
            return @"X:\Дела изделий\Корзина";
        }

        // Локальный резервный путь для изделий
        private static string GetDefaultLocalRootFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Дела изделий",
                "Изделия");
        }

        // Локальный резервный путь для корзины
        private static string GetDefaultLocalRecycleBinFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Дела изделий",
                "Корзина");
        }

        // Метод для получения корневой папки изделий
        public static string GetRootFolder()
        {
            (string rootFolder, string recycleBinFolder) = LoadConfig();
            return rootFolder;
        }

        // Метод для получения корневой папки Recycle bin
        public static string GetRecycleBinFolder()
        {
            (string rootFolder, string recycleBinFolder) = LoadConfig();
            return recycleBinFolder;
        }

        // Метод для сохранения только корневой папки изделий с сохранением пути Recycle bin
        public static void SaveRootFolder(string rootFolder)
        {
            (string currentNetworkRoot, string currentNetworkRecycleBin, string currentLocalRoot, string currentLocalRecycleBin) = LoadStoredConfig();
            SaveFolders(rootFolder, currentNetworkRecycleBin);
        }

        // Метод для сохранения только корневой папки Recycle bin с сохранением пути изделий
        public static void SaveRecycleBinFolder(string recycleBinFolder)
        {
            (string currentNetworkRoot, string currentNetworkRecycleBin, string currentLocalRoot, string currentLocalRecycleBin) = LoadStoredConfig();
            SaveFolders(currentNetworkRoot, recycleBinFolder);
        }

        // Метод для сохранения обеих папок в конфигурационный файл
        public static void SaveFolders(string rootFolder, string recycleBinFolder)
        {
            if (string.IsNullOrWhiteSpace(rootFolder))
            {
                throw new ArgumentException("Корневой путь изделий не может быть пустым");
            }

            if (string.IsNullOrWhiteSpace(recycleBinFolder))
            {
                throw new ArgumentException("Путь Recycle bin не может быть пустым");
            }

            string cleanedRoot = NormalizePath(rootFolder);
            string cleanedRecycleBin = NormalizePath(recycleBinFolder);
            string localRoot = NormalizePath(GetDefaultLocalRootFolder());
            string localRecycleBin = NormalizePath(GetDefaultLocalRecycleBinFolder());

            EnsureDirectoryIfPathAvailable(cleanedRoot);
            EnsureDirectoryIfPathAvailable(cleanedRecycleBin);
            Directory.CreateDirectory(localRoot);
            Directory.CreateDirectory(localRecycleBin);

            string projectCfg = GetConfigPathInProjectFolder();
            if (!string.IsNullOrWhiteSpace(projectCfg))
            {
                try
                {
                    EnsureDirectoryForFile(projectCfg);
                    WriteConfig(projectCfg, cleanedRoot, cleanedRecycleBin, localRoot, localRecycleBin);
                    return;
                }
                catch
                {
                }
            }

            string appDataCfg = GetConfigPathInAppData();
            EnsureDirectoryForFile(appDataCfg);
            WriteConfig(appDataCfg, cleanedRoot, cleanedRecycleBin, localRoot, localRecycleBin);
        }

        // Метод для получения фактического пути к конфигурационному файлу
        public static string GetConfigFilePath()
        {
            string projectCfg = GetConfigPathInProjectFolder();
            if (!string.IsNullOrWhiteSpace(projectCfg))
            {
                return projectCfg;
            }

            return GetConfigPathInAppData();
        }

        // Метод для загрузки конфигурации путей
        private static (string RootFolder, string RecycleBinFolder) LoadConfig()
        {
            (string networkRoot, string networkRecycleBin, string localRoot, string localRecycleBin) = LoadStoredConfig();

            bool networkAvailable =
                IsPathAvailable(networkRoot) &&
                IsPathAvailable(networkRecycleBin);

            string activeRoot = networkAvailable ? networkRoot : localRoot;
            string activeRecycleBin = networkAvailable ? networkRecycleBin : localRecycleBin;

            Directory.CreateDirectory(activeRoot);
            Directory.CreateDirectory(activeRecycleBin);

            return (activeRoot, activeRecycleBin);
        }

        // Метод для загрузки сохраненной конфигурации путей
        private static (string NetworkRootFolder, string NetworkRecycleBinFolder, string LocalRootFolder, string LocalRecycleBinFolder) LoadStoredConfig()
        {
            string defaultNetworkRoot = GetDefaultNetworkRootFolder();
            string defaultNetworkRecycleBin = GetDefaultNetworkRecycleBinFolder();
            string defaultLocalRoot = GetDefaultLocalRootFolder();
            string defaultLocalRecycleBin = GetDefaultLocalRecycleBinFolder();

            string projectCfg = GetConfigPathInProjectFolder();
            if (!string.IsNullOrWhiteSpace(projectCfg))
            {
                (string networkRoot, string networkRecycleBin, string localRoot, string localRecycleBin) =
                    TryReadOrCreateConfig(projectCfg, defaultNetworkRoot, defaultNetworkRecycleBin, defaultLocalRoot, defaultLocalRecycleBin);

                if (!string.IsNullOrWhiteSpace(networkRoot) &&
                    !string.IsNullOrWhiteSpace(networkRecycleBin) &&
                    !string.IsNullOrWhiteSpace(localRoot) &&
                    !string.IsNullOrWhiteSpace(localRecycleBin))
                {
                    return (networkRoot, networkRecycleBin, localRoot, localRecycleBin);
                }
            }

            string appDataCfg = GetConfigPathInAppData();
            return TryReadOrCreateConfig(appDataCfg, defaultNetworkRoot, defaultNetworkRecycleBin, defaultLocalRoot, defaultLocalRecycleBin);
        }

        // Метод для чтения конфигурации из файла или создания файла со значениями по умолчанию
        private static (string NetworkRootFolder, string NetworkRecycleBinFolder, string LocalRootFolder, string LocalRecycleBinFolder) TryReadOrCreateConfig(
            string configPath,
            string defaultNetworkRoot,
            string defaultNetworkRecycleBin,
            string defaultLocalRoot,
            string defaultLocalRecycleBin)
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    EnsureDirectoryForFile(configPath);
                    WriteConfig(configPath, defaultNetworkRoot, defaultNetworkRecycleBin, defaultLocalRoot, defaultLocalRecycleBin);
                }

                string[] lines = File.ReadAllLines(configPath);

                string networkRoot = defaultNetworkRoot;
                string networkRecycleBin = defaultNetworkRecycleBin;
                string localRoot = defaultLocalRoot;
                string localRecycleBin = defaultLocalRecycleBin;

                int networkCommentIndex = Array.FindIndex(lines, line => string.Equals(line?.Trim(), NetworkPathsComment, StringComparison.OrdinalIgnoreCase));
                int localCommentIndex = Array.FindIndex(lines, line => string.Equals(line?.Trim(), LocalPathsComment, StringComparison.OrdinalIgnoreCase));

                if (networkCommentIndex >= 0)
                {
                    if (lines.Length > networkCommentIndex + 1 && !string.IsNullOrWhiteSpace(lines[networkCommentIndex + 1]))
                    {
                        networkRoot = NormalizePath(lines[networkCommentIndex + 1]);
                    }

                    if (lines.Length > networkCommentIndex + 2 && !string.IsNullOrWhiteSpace(lines[networkCommentIndex + 2]))
                    {
                        networkRecycleBin = NormalizePath(lines[networkCommentIndex + 2]);
                    }
                }
                else
                {
                    // если файл старого формата — читаем первые две строки как сетевые пути
                    if (lines.Length >= 1 && !string.IsNullOrWhiteSpace(lines[0]))
                    {
                        networkRoot = NormalizePath(lines[0]);
                    }

                    if (lines.Length >= 2 && !string.IsNullOrWhiteSpace(lines[1]))
                    {
                        networkRecycleBin = NormalizePath(lines[1]);
                    }
                }

                if (localCommentIndex >= 0)
                {
                    if (lines.Length > localCommentIndex + 1 && !string.IsNullOrWhiteSpace(lines[localCommentIndex + 1]))
                    {
                        localRoot = NormalizePath(lines[localCommentIndex + 1]);
                    }

                    if (lines.Length > localCommentIndex + 2 && !string.IsNullOrWhiteSpace(lines[localCommentIndex + 2]))
                    {
                        localRecycleBin = NormalizePath(lines[localCommentIndex + 2]);
                    }
                }

                if (string.IsNullOrWhiteSpace(networkRoot))
                {
                    networkRoot = defaultNetworkRoot;
                }

                if (string.IsNullOrWhiteSpace(networkRecycleBin))
                {
                    networkRecycleBin = defaultNetworkRecycleBin;
                }

                if (string.IsNullOrWhiteSpace(localRoot))
                {
                    localRoot = defaultLocalRoot;
                }

                if (string.IsNullOrWhiteSpace(localRecycleBin))
                {
                    localRecycleBin = defaultLocalRecycleBin;
                }

                Directory.CreateDirectory(localRoot);
                Directory.CreateDirectory(localRecycleBin);

                // если файл старого формата — перезапишем в новый формат
                WriteConfig(configPath, networkRoot, networkRecycleBin, localRoot, localRecycleBin);

                return (networkRoot, networkRecycleBin, localRoot, localRecycleBin);
            }
            catch
            {
                return (defaultNetworkRoot, defaultNetworkRecycleBin, defaultLocalRoot, defaultLocalRecycleBin);
            }
        }

        // Метод для записи конфигурации в файл
        private static void WriteConfig(string configPath, string networkRootFolder, string networkRecycleBinFolder, string localRootFolder, string localRecycleBinFolder)
        {
            string[] lines =
            {
                NetworkPathsComment,
                NormalizePath(networkRootFolder),
                NormalizePath(networkRecycleBinFolder),
                string.Empty,
                LocalPathsComment,
                NormalizePath(localRootFolder),
                NormalizePath(localRecycleBinFolder)
            };

            File.WriteAllLines(configPath, lines);
        }

        // Метод для проверки доступности пути
        private static bool IsPathAvailable(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                string rootPath = Path.GetPathRoot(path) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(rootPath))
                {
                    return false;
                }

                return Directory.Exists(rootPath);
            }
            catch
            {
                return false;
            }
        }

        // Метод для создания директории, если путь доступен
        private static void EnsureDirectoryIfPathAvailable(string path)
        {
            if (IsPathAvailable(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        // Метод для нормализации пути
        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        // Метод для создания директории под файл
        private static void EnsureDirectoryForFile(string filePath)
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    }
}