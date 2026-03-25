using ProductDossier.Data.Enums;

namespace ProductDossier.Data.Entities
{
    // Модель для таблицы documents
    public class Document
    {
        public long IdDocument { get; set; }

        public long IdDocumentCategory { get; set; }
        public DocumentCategory? DocumentCategory { get; set; }

        public long? IdParentDocument { get; set; }
        public Document? ParentDocument { get; set; }

        public long IdResponsibleUser { get; set; }
        public User? ResponsibleUser { get; set; }

        public string DocumentNumber { get; set; } = string.Empty;
        public string NameDocument { get; set; } = string.Empty;
        public string? DescriptionDocument { get; set; }
        public DocumentStatusEnum Status { get; set; }
        public DocumentStatusEnum? StatusBeforeDelete { get; set; }
        public DateTime? DeletedAt { get; set; }
        public long? DeletedBy { get; set; }
        public bool IsDeletedRoot { get; set; }

        public ICollection<Document> Children { get; set; } = new List<Document>();
        public ICollection<FileItem> Files { get; set; } = new List<FileItem>();
        public ICollection<ProductDocument> ProductDocuments { get; set; } = new List<ProductDocument>();
    }
}
