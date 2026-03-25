using System.IO;

namespace ProductDossier.UI.Services
{
    // Результат выбора файла для добавления документа
    public sealed class SelectedDocumentFile
    {
        public SelectedDocumentFile(string fullPath)
        {
            FullPath = fullPath;
            FileName = Path.GetFileName(fullPath);
            FileExtension = Path.GetExtension(fullPath);
            SuggestedDocumentName = Path.GetFileNameWithoutExtension(fullPath);
        }

        public string FullPath { get; }
        public string FileName { get; }
        public string FileExtension { get; }
        public string SuggestedDocumentName { get; }
    }
}