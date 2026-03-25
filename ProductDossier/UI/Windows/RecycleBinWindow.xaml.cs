using ProductDossier.Data.Enums;
using ProductDossier.Data.Services;
using ProductDossier.UI.Tree;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace ProductDossier
{
    // Окно для просмотра содержимого корзины и выполнения операций восстановления и окончательного удаления
    public partial class RecycleBinWindow : Window, INotifyPropertyChanged
    {
        // Модель для элемента фильтра по типу объекта
        private sealed class ObjectTypeFilterItem
        {
            public string DisplayName { get; }
            public RecycleBinObjectType? Value { get; }

            // Метод для инициализации элемента фильтра по типу объекта
            public ObjectTypeFilterItem(string displayName, RecycleBinObjectType? value)
            {
                DisplayName = displayName;
                Value = value;
            }

            // Метод для возврата отображаемого имени элемента фильтра
            public override string ToString()
            {
                return DisplayName;
            }
        }

        private readonly long _currentUserId;
        private readonly DeletePermissionRole _currentUserRole;

        private readonly List<TreeNode> _allTreeItems = new List<TreeNode>();
        private bool _hasChanges;

        private TreeNode? _selectedNode;
        private string _selectedNodeTitle = "Ничего не выбрано";
        private string _selectedNodeType = "—";
        private string _selectedActionTarget = "Выберите изделие, документ или файл";
        private string _selectedDeletedAt = "—";
        private string _selectedDeletedBy = "—";
        private string _selectedFilePath = "—";

        public ObservableCollection<TreeNode> TreeItems { get; } = new ObservableCollection<TreeNode>();

        public string SelectedNodeTitle
        {
            get => _selectedNodeTitle;
            set
            {
                _selectedNodeTitle = value;
                OnPropertyChanged();
            }
        }

        public string SelectedNodeType
        {
            get => _selectedNodeType;
            set
            {
                _selectedNodeType = value;
                OnPropertyChanged();
            }
        }

        public string SelectedActionTarget
        {
            get => _selectedActionTarget;
            set
            {
                _selectedActionTarget = value;
                OnPropertyChanged();
            }
        }

        public string SelectedDeletedAt
        {
            get => _selectedDeletedAt;
            set
            {
                _selectedDeletedAt = value;
                OnPropertyChanged();
            }
        }

        public string SelectedDeletedBy
        {
            get => _selectedDeletedBy;
            set
            {
                _selectedDeletedBy = value;
                OnPropertyChanged();
            }
        }

        public string SelectedFilePath
        {
            get => _selectedFilePath;
            set
            {
                _selectedFilePath = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        // Метод для инициализации окна корзины
        public RecycleBinWindow(long currentUserId, DeletePermissionRole currentUserRole)
        {
            InitializeComponent();

            _currentUserId = currentUserId;
            _currentUserRole = currentUserRole;

            DataContext = this;

            cbObjectType.ItemsSource = new[]
            {
                new ObjectTypeFilterItem("Все", null),
                new ObjectTypeFilterItem("Изделие", RecycleBinObjectType.Product),
                new ObjectTypeFilterItem("Документ", RecycleBinObjectType.Document)
            };

            cbObjectType.SelectedIndex = 0;

            btnDeletePermanently.Visibility = _currentUserRole == DeletePermissionRole.SuperAdmin
                ? Visibility.Visible
                : Visibility.Collapsed;

            Loaded += RecycleBinWindow_Loaded;
        }

        // Метод для первичной загрузки данных окна корзины
        private async void RecycleBinWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await ReloadAsync();
        }

        // Метод для обновления дерева корзины
        private async Task ReloadAsync()
        {
            try
            {
                List<TreeNode> items = await RecycleBinService.GetTreeItemsAsync();

                _allTreeItems.Clear();
                _allTreeItems.AddRange(items);

                ApplyFilters();
                UpdateSelectedNodeInfo(null);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось загрузить содержимое корзины: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для применения фильтров к дереву корзины
        private void ApplyFilters()
        {
            string search = tbSearch.Text?.Trim() ?? string.Empty;
            string deletedBy = tbDeletedBy.Text?.Trim() ?? string.Empty;
            RecycleBinObjectType? selectedType = (cbObjectType.SelectedItem as ObjectTypeFilterItem)?.Value;

            TreeItems.Clear();

            foreach (TreeNode root in _allTreeItems
                         .Select(x => FilterNodeRecursive(x, search, deletedBy, selectedType))
                         .Where(x => x != null)
                         .Cast<TreeNode>()
                         .OrderBy(x => x.DisplayName))
            {
                TreeItems.Add(root);
            }
        }

        // Метод для фильтрации узла дерева корзины с рекурсией
        private TreeNode? FilterNodeRecursive(
            TreeNode source,
            string search,
            string deletedBy,
            RecycleBinObjectType? selectedType)
        {
            List<TreeNode> filteredChildren = source.Children
                .Select(x => FilterNodeRecursive(x, search, deletedBy, selectedType))
                .Where(x => x != null)
                .Cast<TreeNode>()
                .ToList();

            bool searchOk = string.IsNullOrWhiteSpace(search) ||
                            (source.SearchText ?? source.DisplayName).Contains(search, StringComparison.OrdinalIgnoreCase);

            bool deletedByOk = string.IsNullOrWhiteSpace(deletedBy) ||
                               (source.DeletedByDisplayName ?? string.Empty).Contains(deletedBy, StringComparison.OrdinalIgnoreCase);

            bool typeOk = selectedType == null ||
                          source.RecycleBinObjectType == selectedType.Value;

            bool ownActionMatch = source.RecycleBinObjectId.HasValue &&
                                  searchOk &&
                                  deletedByOk &&
                                  typeOk;

            bool ownContainerMatch = !source.RecycleBinObjectId.HasValue &&
                                     string.IsNullOrWhiteSpace(deletedBy) &&
                                     selectedType == null &&
                                     searchOk;

            if (!ownActionMatch && !ownContainerMatch && filteredChildren.Count == 0)
            {
                return null;
            }

            return CloneNode(source, filteredChildren);
        }

        // Метод для клонирования узла дерева корзины
        private TreeNode CloneNode(TreeNode source, List<TreeNode> children)
        {
            TreeNode clone = new TreeNode
            {
                NodeType = source.NodeType,
                DisplayName = source.DisplayName,

                ProductId = source.ProductId,
                CategoryId = source.CategoryId,
                DocumentId = source.DocumentId,
                FileId = source.FileId,

                FilePath = source.FilePath,
                ParentDocumentId = source.ParentDocumentId,

                RecycleBinObjectType = source.RecycleBinObjectType,
                RecycleBinObjectId = source.RecycleBinObjectId,
                RecycleBinObjectDisplayName = source.RecycleBinObjectDisplayName,

                DeletedAtUtc = source.DeletedAtUtc,
                DeletedByDisplayName = source.DeletedByDisplayName,
                SearchText = source.SearchText,
                IsRecycleContainerOnly = source.IsRecycleContainerOnly
            };

            foreach (TreeNode child in children)
            {
                clone.Children.Add(child);
            }

            return clone;
        }

        // Метод для обработки изменения фильтров
        private void Filters_Changed(object sender, RoutedEventArgs e)
        {
            ApplyFilters();
        }

        // Метод для сброса фильтров окна корзины
        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            tbSearch.Text = string.Empty;
            tbDeletedBy.Text = string.Empty;
            cbObjectType.SelectedIndex = 0;
            ApplyFilters();
        }

        // Метод для обработки смены выбранного узла дерева
        private void tvRecycleBin_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            UpdateSelectedNodeInfo(e.NewValue as TreeNode);
        }

        // Метод для открытия файла по двойному нажатию в корзине
        private void tvRecycleBin_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (tvRecycleBin.SelectedItem is not TreeNode node)
            {
                return;
            }

            if (node.IsRecycleContainerOnly)
            {
                return;
            }

            if (node.NodeType != NodeType.File)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(node.FilePath))
            {
                return;
            }

            try
            {
                DossierService.OpenFile(node.FilePath);
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть файл: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для обновления информации о выбранном узле
        private void UpdateSelectedNodeInfo(TreeNode? node)
        {
            _selectedNode = node;

            SelectedNodeTitle = node?.DisplayName ?? "Ничего не выбрано";
            SelectedNodeType = BuildNodeTypeDisplayName(node);
            SelectedFilePath = string.IsNullOrWhiteSpace(node?.FilePath) ? "—" : node!.FilePath!;

            if (node?.RecycleBinObjectType != null &&
                node.RecycleBinObjectId.HasValue)
            {
                string objectType = node.RecycleBinObjectType == RecycleBinObjectType.Product
                    ? "Изделие"
                    : "Документ";

                SelectedActionTarget = $"{objectType}: {node.RecycleBinObjectDisplayName}";
                // Время показываем так же, как в таблице истории, без дополнительного сдвига часового пояса
                SelectedDeletedAt = node.DeletedAtUtc.HasValue
                    ? node.DeletedAtUtc.Value.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)
                    : "—";
                SelectedDeletedBy = string.IsNullOrWhiteSpace(node.DeletedByDisplayName)
                    ? "—"
                    : node.DeletedByDisplayName;
            }
            else
            {
                SelectedActionTarget = "Выберите изделие, документ или файл";
                SelectedDeletedAt = "—";
                SelectedDeletedBy = "—";
            }
        }

        // Метод для формирования отображаемого названия типа выбранного узла
        private string BuildNodeTypeDisplayName(TreeNode? node)
        {
            if (node == null)
            {
                return "—";
            }

            return node.NodeType switch
            {
                NodeType.Product => "Изделие",
                NodeType.Category => "Категория",
                NodeType.Document => "Документ",
                NodeType.File => "Файл",
                _ => "—"
            };
        }

        // Метод для восстановления выбранного объекта
        private async void btnRestore_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNode?.RecycleBinObjectType == null || !_selectedNode.RecycleBinObjectId.HasValue)
            {
                MessageBox.Show("Выберите изделие, документ или файл, относящийся к объекту в корзине",
                    "Корзина", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_currentUserRole == DeletePermissionRole.Employee)
            {
                MessageBox.Show("Недостаточно прав для восстановления",
                    "Корзина", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string objectType = _selectedNode.RecycleBinObjectType == RecycleBinObjectType.Product
                ? "изделие"
                : "документ";

            string text =
                $"Восстановить {objectType}:\n{_selectedNode.RecycleBinObjectDisplayName}\n\n" +
                "Структура и связи объекта будут восстановлены. Продолжить";

            if (MessageBox.Show(text, "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                await RecycleBinService.RestoreAsync(
                    _selectedNode.RecycleBinObjectType.Value,
                    _selectedNode.RecycleBinObjectId.Value,
                    _currentUserId,
                    _currentUserRole);

                _hasChanges = true;
                await ReloadAsync();
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(ex.Message, "Недостаточно прав",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Корзина",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (System.IO.IOException ex)
            {
                MessageBox.Show("Ошибка файловой системы при восстановлении: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось восстановить объект: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для окончательного удаления выбранного объекта
        private async void btnDeletePermanently_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNode?.RecycleBinObjectType == null || !_selectedNode.RecycleBinObjectId.HasValue)
            {
                MessageBox.Show("Выберите изделие, документ или файл, относящийся к объекту в корзине",
                    "Корзина", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_currentUserRole != DeletePermissionRole.SuperAdmin)
            {
                MessageBox.Show("Недостаточно прав. Окончательное удаление доступно только Супер-администратору",
                    "Корзина", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string objectType = _selectedNode.RecycleBinObjectType == RecycleBinObjectType.Product
                ? "изделие"
                : "документ";

            string text =
                $"Окончательно удалить {objectType}:\n{_selectedNode.RecycleBinObjectDisplayName}\n\n" +
                "Данные будут удалены из базы данных и файловой системы без возможности восстановления. Продолжить";

            if (MessageBox.Show(text, "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                await RecycleBinService.DeletePermanentlyAsync(
                    _selectedNode.RecycleBinObjectType.Value,
                    _selectedNode.RecycleBinObjectId.Value,
                    _currentUserId,
                    _currentUserRole);

                _hasChanges = true;
                await ReloadAsync();
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(ex.Message, "Недостаточно прав",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Корзина",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (System.IO.IOException ex)
            {
                MessageBox.Show("Ошибка файловой системы при окончательном удалении: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось окончательно удалить объект: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для установки результата окна при закрытии через системную кнопку
        private void RecycleBinWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (!DialogResult.HasValue)
            {
                DialogResult = _hasChanges;
            }
        }

        // Метод для уведомления интерфейса об изменении свойства
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}