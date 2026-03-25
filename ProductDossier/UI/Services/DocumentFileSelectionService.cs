using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace ProductDossier.UI.Services
{
    // Сервис выбора и базовой проверки файла для окон добавления документа
    public static class DocumentFileSelectionService
    {
        // Открытие диалога выбора файла
        public static bool TrySelectFromDialog(out SelectedDocumentFile? selectedFile, out string? errorMessage)
        {
            selectedFile = null;
            errorMessage = null;

            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Выберите файл для загрузки",
                Multiselect = false,
                CheckFileExists = true,
                CheckPathExists = true,
                Filter = "Все файлы (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return false;
            }

            return TryCreateSelection(dialog.FileName, out selectedFile, out errorMessage);
        }

        // Проверка возможности обработки перетаскивания
        public static bool CanProcessDrop(IDataObject? data)
        {
            return TryGetSingleDroppedFilePath(data, out _);
        }

        // Получение выбранного файла из Drag & Drop
        public static bool TrySelectFromDrop(IDataObject? data, out SelectedDocumentFile? selectedFile, out string? errorMessage)
        {
            selectedFile = null;
            errorMessage = null;

            if (!TryGetSingleDroppedFilePath(data, out string? filePath))
            {
                errorMessage = "Перетащите один файл из проводника Windows.";
                return false;
            }

            return TryCreateSelection(filePath, out selectedFile, out errorMessage);
        }

        // Извлечение пути одного файла из данных Drag & Drop
        private static bool TryGetSingleDroppedFilePath(IDataObject? data, out string? filePath)
        {
            filePath = null;

            if (data == null || !data.GetDataPresent(DataFormats.FileDrop))
            {
                return false;
            }

            if (data.GetData(DataFormats.FileDrop) is not string[] droppedFiles || droppedFiles.Length != 1)
            {
                return false;
            }

            string candidate = droppedFiles[0];
            if (string.IsNullOrWhiteSpace(candidate) || Directory.Exists(candidate))
            {
                return false;
            }

            filePath = candidate;
            return true;
        }

        // Общая проверка выбранного файла и подготовка результата
        private static bool TryCreateSelection(string? filePath, out SelectedDocumentFile? selectedFile, out string? errorMessage)
        {
            selectedFile = null;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                errorMessage = "Файл не выбран.";
                return false;
            }

            if (!File.Exists(filePath))
            {
                errorMessage = "Выбранный файл не найден.";
                return false;
            }

            try
            {
                FileInfo fileInfo = new FileInfo(filePath);

                using FileStream stream = File.Open(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (!stream.CanRead)
                {
                    errorMessage = "Выбранный файл недоступен для чтения.";
                    return false;
                }

                selectedFile = new SelectedDocumentFile(fileInfo.FullName);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = "Не удалось подготовить файл к загрузке: " + ex.Message;
                return false;
            }
        }
    }
}