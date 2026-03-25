using ProductDossier.UI.Tree;
using System.Diagnostics;
using System.IO;

namespace ProductDossier.Data.Services
{
    /// Отслеживает изменения файлов, которые пользователь открыл из приложения.
    /// Если файл был изменён/сохранён во внешней программе — пишет запись в document_change_history.
    public static class FileEditTrackingService
    {
        private sealed class TrackedFile : IDisposable
        {
            public string FullPath { get; }
            public long FileId { get; }
            public long UserId { get; set; }

            public DateTime OpenedAtUtc { get; set; }
            public DateTime LastLoggedWriteUtc { get; set; }

            private readonly FileSystemWatcher _watcher;
            private readonly Timer _debounceTimer;
            private readonly object _localGate = new object();
            private bool _disposed;

            public TrackedFile(string fullPath, long fileId, long userId)
            {
                FullPath = fullPath;
                FileId = fileId;
                UserId = userId;

                OpenedAtUtc = DateTime.UtcNow;

                try
                {
                    LastLoggedWriteUtc = File.GetLastWriteTimeUtc(FullPath);
                }
                catch
                {
                    LastLoggedWriteUtc = DateTime.MinValue;
                }

                _debounceTimer = new Timer(_ => _ = Task.Run(FlushAsync), null, Timeout.Infinite, Timeout.Infinite);

                string dir = Path.GetDirectoryName(FullPath) ?? string.Empty;
                string name = Path.GetFileName(FullPath);

                _watcher = new FileSystemWatcher(dir, name)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };

                _watcher.Changed += (_, __) => OnAnyChange();
                _watcher.Created += (_, __) => OnAnyChange();
                _watcher.Renamed += (_, __) => OnAnyChange();
                _watcher.Deleted += (_, __) => OnDeleted();
            }

            // Метод для обработки любого изменения файла и запуска debounce-таймера
            private void OnAnyChange()
            {
                lock (_localGate)
                {
                    if (_disposed) return;

                    _debounceTimer.Change(800, Timeout.Infinite);
                }
            }

            // Метод для обработки удаления/перемещения файла и остановки отслеживания
            private void OnDeleted()
            {
                Dispose();
            }

            // Метод для выполнения отложенной фиксации изменений файла и записи в историю
            private async Task FlushAsync()
            {
                try
                {
                    if (_disposed) return;

                    DateTime writeUtc = DateTime.MinValue;
                    bool ok = false;

                    for (int i = 0; i < 3; i++)
                    {
                        try
                        {
                            if (!File.Exists(FullPath)) return;
                            writeUtc = File.GetLastWriteTimeUtc(FullPath);
                            ok = true;
                            break;
                        }
                        catch
                        {
                            await Task.Delay(200);
                        }
                    }

                    if (!ok) return;

                    if (DateTime.UtcNow - OpenedAtUtc < TimeSpan.FromSeconds(1))
                        return;

                    if (writeUtc <= LastLoggedWriteUtc.AddMilliseconds(150))
                        return;

                    LastLoggedWriteUtc = writeUtc;

                    await DossierService.LogExternalFileEditAsync(FileId, UserId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("FileEditTrackingService FlushAsync error: " + ex);
                }
            }

            // Метод для освобождения ресурсов отслеживания (FileSystemWatcher/Timer)
            public void Dispose()
            {
                lock (_localGate)
                {
                    if (_disposed) return;
                    _disposed = true;
                }

                try { _watcher.EnableRaisingEvents = false; } catch { }
                try { _watcher.Dispose(); } catch { }
                try { _debounceTimer.Dispose(); } catch { }
            }
        }

        private static readonly object _gate = new object();
        private static readonly Dictionary<string, TrackedFile> _tracked =
            new Dictionary<string, TrackedFile>(StringComparer.OrdinalIgnoreCase);

        // Метод для постановки файла на отслеживание при открытии из приложения
        public static void TrackOpenedFile(TreeNode node, long currentUserId)
        {
            if (node == null || node.NodeType != NodeType.File || node.FileId == null)
                return;

            string fullPath = DossierService.ResolveAbsolutePath(node.FilePath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(fullPath))
                return;

            if (!File.Exists(fullPath))
                return;

            lock (_gate)
            {
                if (_tracked.TryGetValue(fullPath, out var existing))
                {
                    existing.UserId = currentUserId;
                    existing.OpenedAtUtc = DateTime.UtcNow;
                    return;
                }

                var tf = new TrackedFile(fullPath, node.FileId.Value, currentUserId);
                _tracked[fullPath] = tf;
            }
        }

        // Метод для остановки отслеживания всех файлов и очистки списка
        public static void StopAll()
        {
            lock (_gate)
            {
                foreach (var kv in _tracked)
                {
                    try { kv.Value.Dispose(); } catch { }
                }
                _tracked.Clear();
            }
        }
    }
}