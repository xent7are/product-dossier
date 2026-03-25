namespace ProductDossier.Data.Entities
{
    // Модель для таблицы files
    public class FileItem
    {
        public long IdFile { get; set; }

        public long IdDocument { get; set; }
        public Document? Document { get; set; }

        public long IdUploadedBy { get; set; }
        public User? UploadedBy { get; set; }

        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string? RecycleBinFilePath { get; set; }
        public string FileExtension { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public DateTime UploadedAt { get; set; }
        public DateTime LastModifiedAt { get; set; }
    }
}
