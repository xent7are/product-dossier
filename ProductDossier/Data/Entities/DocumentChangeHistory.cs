using ProductDossier.Data.Enums;

namespace ProductDossier.Data.Entities
{
    // Модель для таблицы document_change_history
    public class DocumentChangeHistory
    {
        public long IdChange { get; set; }

        public string UserSurname { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string? UserPatronymic { get; set; }

        public HistoryOperationEnum Operation { get; set; }
        public DateTime ChangedAt { get; set; }

        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;

        public string ProductNumber { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;

        public string DocumentNumber { get; set; } = string.Empty;
        public string DocumentName { get; set; } = string.Empty;
    }
}