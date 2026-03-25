using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using ProductDossier.Data.Entities;
using ProductDossier.Data.Enums;
using System.Collections.Concurrent;

namespace ProductDossier.Data
{
    // Контекст базы данных приложения.
    public class AppDbContext : DbContext
    {
        private readonly string? _connectionString;
        private static bool _enumsMapped;
        private static readonly ConcurrentDictionary<string, NpgsqlDataSource> _dataSourceCache = new();

        public DbSet<User> Users { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<DocumentCategory> DocumentCategories { get; set; }
        public DbSet<Document> Documents { get; set; }
        public DbSet<FileItem> Files { get; set; }
        public DbSet<ProductDocument> ProductDocuments { get; set; }
        public DbSet<DocumentChangeHistory> DocumentChangeHistory { get; set; }

        // Метод для инициализации контекста с текущей строкой подключения.
        public AppDbContext()
        {
            EnsureEnumsMapped();
        }

        // Метод для инициализации контекста с явной строкой подключения.
        public AppDbContext(string connectionString)
        {
            _connectionString = connectionString;
            EnsureEnumsMapped();
        }

        // Метод для инициализации контекста с параметрами EF.
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
            EnsureEnumsMapped();
        }

        // Метод для единоразовой регистрации enum-типов Npgsql.
        private static void EnsureEnumsMapped()
        {
            if (_enumsMapped)
            {
                return;
            }

            var translator = new NpgsqlNullNameTranslator();

            NpgsqlConnection.GlobalTypeMapper.MapEnum<ProductStatusEnum>("product_dossier.product_status_enum", translator);
            NpgsqlConnection.GlobalTypeMapper.MapEnum<DocumentStatusEnum>("product_dossier.document_status_enum", translator);
            NpgsqlConnection.GlobalTypeMapper.MapEnum<HistoryOperationEnum>("product_dossier.history_operation_enum", translator);

            _enumsMapped = true;
        }

        // Метод для получения или создания NpgsqlDataSource.
        private static NpgsqlDataSource GetOrCreateDataSource(string connectionString)
        {
            return _dataSourceCache.GetOrAdd(connectionString, static cs =>
            {
                var builder = new NpgsqlDataSourceBuilder(cs);
                var translator = new NpgsqlNullNameTranslator();

                builder.MapEnum<ProductStatusEnum>("product_dossier.product_status_enum", translator);
                builder.MapEnum<DocumentStatusEnum>("product_dossier.document_status_enum", translator);
                builder.MapEnum<HistoryOperationEnum>("product_dossier.history_operation_enum", translator);

                return builder.Build();
            });
        }

        // Метод для очистки кэша NpgsqlDataSource.
        public static void ClearDataSourceCache()
        {
            foreach (var dataSource in _dataSourceCache.Values)
            {
                try
                {
                    dataSource.Dispose();
                }
                catch
                {
                }
            }

            _dataSourceCache.Clear();
        }

        // Метод для настройки подключения EF Core.
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (optionsBuilder.IsConfigured)
            {
                return;
            }

            string connectionString = _connectionString ?? DbConnectionManager.ConnectionString;
            optionsBuilder.UseNpgsql(GetOrCreateDataSource(connectionString));
        }

        // Метод для конфигурации модели EF Core.
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("product_dossier");
            modelBuilder.HasPostgresEnum<ProductStatusEnum>("product_dossier", "product_status_enum");
            modelBuilder.HasPostgresEnum<DocumentStatusEnum>("product_dossier", "document_status_enum");
            modelBuilder.HasPostgresEnum<HistoryOperationEnum>("product_dossier", "history_operation_enum");

            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("users");
                entity.HasKey(e => e.IdUser);

                entity.Property(e => e.IdUser).HasColumnName("id_user");
                entity.Property(e => e.Login).HasColumnName("login").IsRequired();
                entity.Property(e => e.Surname).HasColumnName("surname").IsRequired();
                entity.Property(e => e.Name).HasColumnName("name").IsRequired();
                entity.Property(e => e.Patronymic).HasColumnName("patronymic");

                entity.HasIndex(e => e.Login).IsUnique();
            });

            modelBuilder.Entity<Product>(entity =>
            {
                entity.ToTable("products");
                entity.HasKey(e => e.IdProduct);

                entity.Property(e => e.IdProduct).HasColumnName("id_product");
                entity.Property(e => e.ProductNumber).HasColumnName("product_number").IsRequired();
                entity.Property(e => e.NameProduct).HasColumnName("name_product").IsRequired();
                entity.Property(e => e.DescriptionProduct).HasColumnName("description_product");
                entity.Property(e => e.Status).HasColumnName("status").HasColumnType("product_dossier.product_status_enum").IsRequired();
                entity.Property(e => e.StatusBeforeDelete).HasColumnName("status_before_delete").HasColumnType("product_dossier.product_status_enum");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
                entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
                entity.Property(e => e.DeletedBy).HasColumnName("deleted_by");

                entity.HasIndex(e => e.ProductNumber).IsUnique();
                entity.HasIndex(e => e.NameProduct).IsUnique();
            });

            modelBuilder.Entity<DocumentCategory>(entity =>
            {
                entity.ToTable("document_categories");
                entity.HasKey(e => e.IdDocumentCategory);

                entity.Property(e => e.IdDocumentCategory).HasColumnName("id_document_category");
                entity.Property(e => e.NameDocumentCategory).HasColumnName("name_document_category").IsRequired();
                entity.Property(e => e.DescriptionDocumentCategory).HasColumnName("description_document_category");
                entity.Property(e => e.SortOrder).HasColumnName("sort_order").IsRequired();

                entity.HasIndex(e => e.NameDocumentCategory).IsUnique();
                entity.HasIndex(e => e.SortOrder).IsUnique();
            });

            modelBuilder.Entity<Document>(entity =>
            {
                entity.ToTable("documents");
                entity.HasKey(e => e.IdDocument);

                entity.Property(e => e.IdDocument).HasColumnName("id_document");
                entity.Property(e => e.IdDocumentCategory).HasColumnName("id_document_category").IsRequired();
                entity.Property(e => e.IdParentDocument).HasColumnName("id_parent_document");
                entity.Property(e => e.IdResponsibleUser).HasColumnName("id_responsible_user").IsRequired();
                entity.Property(e => e.DocumentNumber).HasColumnName("document_number").IsRequired();
                entity.Property(e => e.NameDocument).HasColumnName("name_document").IsRequired();
                entity.Property(e => e.DescriptionDocument).HasColumnName("description_document");
                entity.Property(e => e.Status).HasColumnName("status").HasColumnType("product_dossier.document_status_enum").IsRequired();
                entity.Property(e => e.StatusBeforeDelete).HasColumnName("status_before_delete").HasColumnType("product_dossier.document_status_enum");
                entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
                entity.Property(e => e.DeletedBy).HasColumnName("deleted_by");
                entity.Property(e => e.IsDeletedRoot).HasColumnName("is_deleted_root").IsRequired();

                entity.HasIndex(e => e.DocumentNumber).IsUnique();
                entity.HasIndex(e => e.NameDocument).IsUnique();

                entity.HasOne(d => d.DocumentCategory)
                    .WithMany(c => c.Documents)
                    .HasForeignKey(d => d.IdDocumentCategory)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(d => d.ResponsibleUser)
                    .WithMany()
                    .HasForeignKey(d => d.IdResponsibleUser)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(d => d.ParentDocument)
                    .WithMany(p => p.Children)
                    .HasForeignKey(d => d.IdParentDocument)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<FileItem>(entity =>
            {
                entity.ToTable("files");
                entity.HasKey(e => e.IdFile);

                entity.Property(e => e.IdFile).HasColumnName("id_file");
                entity.Property(e => e.IdDocument).HasColumnName("id_document").IsRequired();
                entity.Property(e => e.IdUploadedBy).HasColumnName("id_uploaded_by").IsRequired();
                entity.Property(e => e.FileName).HasColumnName("file_name").IsRequired();
                entity.Property(e => e.FilePath).HasColumnName("file_path").IsRequired();
                entity.Property(e => e.RecycleBinFilePath).HasColumnName("recycle_bin_file_path");
                entity.Property(e => e.FileExtension).HasColumnName("file_extension").IsRequired();
                entity.Property(e => e.FileSizeBytes).HasColumnName("file_size_bytes").IsRequired();
                entity.Property(e => e.UploadedAt).HasColumnName("uploaded_at").IsRequired();
                entity.Property(e => e.LastModifiedAt).HasColumnName("last_modified_at").IsRequired();

                entity.HasOne(f => f.Document)
                    .WithMany(d => d.Files)
                    .HasForeignKey(f => f.IdDocument)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(f => f.UploadedBy)
                    .WithMany()
                    .HasForeignKey(f => f.IdUploadedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ProductDocument>(entity =>
            {
                entity.ToTable("product_documents");
                entity.HasKey(pd => new { pd.IdProduct, pd.IdDocument });

                entity.Property(pd => pd.IdProduct).HasColumnName("id_product");
                entity.Property(pd => pd.IdDocument).HasColumnName("id_document");

                entity.HasOne(pd => pd.Product)
                    .WithMany(p => p.ProductDocuments)
                    .HasForeignKey(pd => pd.IdProduct)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(pd => pd.Document)
                    .WithMany(d => d.ProductDocuments)
                    .HasForeignKey(pd => pd.IdDocument)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<DocumentChangeHistory>(entity =>
            {
                entity.ToTable("document_change_history");
                entity.HasKey(e => e.IdChange);

                entity.Property(e => e.IdChange).HasColumnName("id_change");
                entity.Property(e => e.UserSurname).HasColumnName("user_surname").IsRequired();
                entity.Property(e => e.UserName).HasColumnName("user_name").IsRequired();
                entity.Property(e => e.UserPatronymic).HasColumnName("user_patronymic");
                entity.Property(e => e.Operation).HasColumnName("operation").HasColumnType("product_dossier.history_operation_enum").IsRequired();
                entity.Property(e => e.ChangedAt).HasColumnName("changed_at").IsRequired();
                entity.Property(e => e.FileName).HasColumnName("file_name").IsRequired();
                entity.Property(e => e.FilePath).HasColumnName("file_path").IsRequired();
                entity.Property(e => e.ProductNumber).HasColumnName("product_number").IsRequired();
                entity.Property(e => e.ProductName).HasColumnName("product_name").IsRequired();
                entity.Property(e => e.DocumentNumber).HasColumnName("document_number").IsRequired();
                entity.Property(e => e.DocumentName).HasColumnName("document_name").IsRequired();
            });
        }
    }
}
