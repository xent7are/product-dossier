using ProductDossier.Data.Enums;
using System;
using System.Collections.ObjectModel;

namespace ProductDossier.UI.Tree
{
    // Модель узла для отображения в TreeView
    public class TreeNode
    {
        public NodeType NodeType { get; set; }
        public string DisplayName { get; set; } = string.Empty;

        public long? ProductId { get; set; }
        public long? CategoryId { get; set; }
        public long? DocumentId { get; set; }
        public long? FileId { get; set; }

        public string? FilePath { get; set; }

        public long? ParentDocumentId { get; set; }

        public RecycleBinObjectType? RecycleBinObjectType { get; set; }
        public long? RecycleBinObjectId { get; set; }
        public string RecycleBinObjectDisplayName { get; set; } = string.Empty;

        public DateTime? DeletedAtUtc { get; set; }
        public string DeletedByDisplayName { get; set; } = string.Empty;

        public string SearchText { get; set; } = string.Empty;

        public bool IsRecycleContainerOnly { get; set; }

        public ObservableCollection<TreeNode> Children { get; set; } = new ObservableCollection<TreeNode>();
    }
}