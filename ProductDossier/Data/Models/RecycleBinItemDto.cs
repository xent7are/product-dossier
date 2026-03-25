using ProductDossier.Data.Enums;

namespace ProductDossier.Data.Models
{
    // Модель строки списка объектов Recycle bin
    public class RecycleBinItemDto
    {
        public long ObjectId { get; set; }
        public RecycleBinObjectType ObjectType { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string TypeDisplayName { get; set; } = string.Empty;
        public DateTime DeletedAtUtc { get; set; }
        public string DeletedByDisplayName { get; set; } = string.Empty;
        public string ProductDisplayName { get; set; } = string.Empty;
        public string DocumentDisplayName { get; set; } = string.Empty;
    }
}
