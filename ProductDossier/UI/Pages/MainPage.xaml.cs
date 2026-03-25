using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProductDossier.Data;
using ProductDossier.Data.Entities;
using ProductDossier.Data.Enums;
using ProductDossier.Data.Services;
using ProductDossier.UI.Tree;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProductDossier
{
    // Страница для отображения главной страницы работы с досье
    public partial class MainPage : Page
    {
        private readonly string _login;
        private long _currentUserId;

        private List<DocumentCategory> _categories = new List<DocumentCategory>();

        private User? _currentUser;
        private string _currentUserRole = string.Empty;

        // Роль для проверок прав удаления
        private DeletePermissionRole _deleteRole = DeletePermissionRole.Employee;

        public string CurrentUserLabel { get; set; } = string.Empty;

        public ObservableCollection<TreeNode> TreeItems { get; set; } = new ObservableCollection<TreeNode>();

        // Защита от параллельных перезагрузок дерева
        private int _treeReloadToken = 0;

        // Метод для инициализации главной страницы
        public MainPage(string login)
        {
            InitializeComponent();
            _login = login;

            // Метод для установки контекста данных страницы
            DataContext = this;

            // Метод для инициализации комбобоксов
            InitComboboxes();

            Loaded += MainPage_Loaded;
        }

        // Метод для первичной загрузки данных страницы
        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _ = ProductsRootPathService.GetRootFolder();

                await LoadCurrentUserAsync();
                await LoadCategoriesAsync();
                await ReloadTreeAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка загрузки главной страницы: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для инициализации ComboBox значениями enum
        private void InitComboboxes()
        {
            cbProductStatus.Items.Clear();
            cbProductStatus.Items.Add("Все");
            foreach (ProductStatusEnum v in Enum.GetValues(typeof(ProductStatusEnum)))
            {
                if (v == ProductStatusEnum.В_корзине)
                {
                    continue;
                }

                cbProductStatus.Items.Add(v);
            }
            cbProductStatus.SelectedIndex = 0;

            cbDocumentStatus.Items.Clear();
            cbDocumentStatus.Items.Add("Все");
            foreach (DocumentStatusEnum v in Enum.GetValues(typeof(DocumentStatusEnum)))
            {
                if (v == DocumentStatusEnum.В_корзине)
                {
                    continue;
                }

                cbDocumentStatus.Items.Add(v);
            }
            cbDocumentStatus.SelectedIndex = 0;
        }

        // Метод для загрузки текущего пользователя по логину
        private async Task LoadCurrentUserAsync()
        {
            await using AppDbContext db = new AppDbContext();

            User? user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Login == _login);
            if (user == null)
            {
                throw new InvalidOperationException("Пользователь не найден в таблице users");
            }

            _currentUser = user;
            _currentUserId = user.IdUser;

            _currentUserRole = await LoadCurrentDbRoleAsync();
            _deleteRole = ParseDeleteRole(_currentUserRole);

            btnRoles.Visibility = _deleteRole == DeletePermissionRole.SuperAdmin
                ? Visibility.Visible
                : Visibility.Collapsed;

            btnChangePassword.Visibility = _deleteRole == DeletePermissionRole.SuperAdmin
                ? Visibility.Visible
                : Visibility.Collapsed;

            btnRecycleBin.Visibility =
                _deleteRole == DeletePermissionRole.Admin || _deleteRole == DeletePermissionRole.SuperAdmin
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            CurrentUserLabel = $"Вы вошли как: {_login} ({user.Surname} {user.Name}) — {_currentUserRole}";

            DataContext = null;
            DataContext = this;
        }

        // Метод для открытия окна смены пароля пользователя
        private async void btnChangePassword_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_deleteRole != DeletePermissionRole.SuperAdmin)
                {
                    MessageBox.Show("Недостаточно прав. Смена пароля доступна только Супер-администратору",
                        "Смена пароля", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var w = new ChangeUserPasswordWindow(_login)
                {
                    Owner = Window.GetWindow(this)
                };

                bool? result = w.ShowDialog();

                if (result == true && w.IsCurrentUserPasswordChanged)
                {
                    await LoadCurrentUserAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть окно смены пароля: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для открытия окна управления ролями пользователей
        private async void btnRoles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_deleteRole != DeletePermissionRole.SuperAdmin)
                {
                    MessageBox.Show("Недостаточно прав. Управление ролями доступно только Супер-администратору",
                        "Роли", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var w = new RoleManagementWindow(_login)
                {
                    Owner = Window.GetWindow(this)
                };

                bool? result = w.ShowDialog();

                // Если супер-админ снял роль с себя, нужно сразу обновить отображение роли и видимость кнопок
                if (result == true && w.IsRoleChanged)
                {
                    await LoadCurrentUserAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть управление ролями: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для открытия окна истории изменений
        private void btnHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var w = new HistoryWindow();
                var owner = Window.GetWindow(this);
                if (owner != null)
                {
                    w.Owner = owner;
                }

                w.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть историю: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для открытия окна корзины
        private async void btnRecycleBin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_deleteRole == DeletePermissionRole.Employee)
                {
                    MessageBox.Show("Недостаточно прав. Корзина доступна только Администратору и Супер-администратору",
                        "Корзина", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var window = new RecycleBinWindow(_currentUserId, _deleteRole)
                {
                    Owner = Window.GetWindow(this)
                };

                bool? result = window.ShowDialog();
                if (result == true)
                {
                    await ReloadTreeAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть корзину: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для определения роли удаления по текстовой метке роли
        private static DeletePermissionRole ParseDeleteRole(string roleLabel)
        {
            if (string.IsNullOrWhiteSpace(roleLabel))
            {
                return DeletePermissionRole.Employee;
            }

            if (roleLabel.Contains("Супер", StringComparison.OrdinalIgnoreCase))
            {
                return DeletePermissionRole.SuperAdmin;
            }

            if (roleLabel.Contains("Администратор", StringComparison.OrdinalIgnoreCase))
            {
                return DeletePermissionRole.Admin;
            }

            return DeletePermissionRole.Employee;
        }

        // Метод для получения роли текущего DB-подключения пользователя
        private async Task<string> LoadCurrentDbRoleAsync()
        {
            try
            {
                await using var conn = new NpgsqlConnection(DbConnectionManager.ConnectionString);
                await conn.OpenAsync();

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT CASE
                        WHEN pg_has_role(current_user, 'pd_superadmin', 'member') THEN 'Супер-администратор'
                        WHEN pg_has_role(current_user, 'pd_admin', 'member') THEN 'Администратор'
                        WHEN pg_has_role(current_user, 'pd_employee', 'member') THEN 'Сотрудник'
                        ELSE 'Сотрудник'
                    END;
                ";

                object? result = await cmd.ExecuteScalarAsync();
                return result?.ToString() ?? "Сотрудник";
            }
            catch
            {
                return "—";
            }
        }

        // Метод для открытия окна профиля пользователя
        private void btnProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentUser == null)
                {
                    MessageBox.Show("Данные пользователя не загружены", "Профиль",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                UserProfileWindow w = new UserProfileWindow(_currentUser, _currentUserRole)
                {
                    Owner = Window.GetWindow(this)
                };

                bool? result = w.ShowDialog();
                if (result == true && w.IsLogoutRequested)
                {
                    PerformLogout();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть профиль: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для открытия файла конфигурации ProductsRootPath.txt
        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string configPath = ProductsRootPathService.GetConfigFilePath();

                if (string.IsNullOrWhiteSpace(configPath))
                {
                    MessageBox.Show("Не удалось определить путь к файлу конфигурации",
                        "Настройки", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // На всякий случай создаётся файл, если его нет
                if (!File.Exists(configPath))
                {
                    _ = ProductsRootPathService.GetRootFolder();
                }

                // Открывается сам файл конфигурации
                Process.Start(new ProcessStartInfo
                {
                    FileName = configPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть файл конфигурации:\n" + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для выхода из учетной записи и перехода на страницу авторизации
        private void PerformLogout()
        {
            FileEditTrackingService.StopAll();

            AuthService.Logout();

            NavigationService.Navigate(new AuthorizationPage());
        }

        // Метод для загрузки категорий документов в ComboBox
        private async Task LoadCategoriesAsync()
        {
            await using AppDbContext db = new AppDbContext();

            _categories = await db.DocumentCategories.AsNoTracking()
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.NameDocumentCategory)
                .ToListAsync();

            cbCategory.Items.Clear();
            cbCategory.Items.Add("Все");

            foreach (var c in _categories)
            {
                cbCategory.Items.Add(c);
            }

            cbCategory.SelectedIndex = 0;
        }

        // Метод для получения выбранного статуса изделия из фильтра
        private ProductStatusEnum? GetSelectedProductStatus()
        {
            if (cbProductStatus.SelectedItem == null || cbProductStatus.SelectedItem.ToString() == "Все")
            {
                return null;
            }

            if (cbProductStatus.SelectedItem is ProductStatusEnum st)
            {
                return st;
            }

            return null;
        }

        // Метод для получения выбранного статуса документа из фильтра
        private DocumentStatusEnum? GetSelectedDocumentStatus()
        {
            if (cbDocumentStatus.SelectedItem == null || cbDocumentStatus.SelectedItem.ToString() == "Все")
            {
                return null;
            }

            if (cbDocumentStatus.SelectedItem is DocumentStatusEnum st)
            {
                return st;
            }

            return null;
        }

        // Метод для получения выбранной категории документа из фильтра
        private long? GetSelectedCategoryId()
        {
            if (cbCategory.SelectedItem == null || cbCategory.SelectedItem.ToString() == "Все")
            {
                return null;
            }

            if (cbCategory.SelectedItem is DocumentCategory cat)
            {
                return cat.IdDocumentCategory;
            }

            return null;
        }

        // Метод для обработки изменения фильтров и перезагрузки дерева
        private async void Filters_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                await ReloadTreeAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка применения фильтров: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для сброса фильтров
        private async void btnReset_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                tbProductSearch.Text = string.Empty;
                tbDocumentSearch.Text = string.Empty;

                cbProductStatus.SelectedIndex = 0;
                cbDocumentStatus.SelectedIndex = 0;
                cbCategory.SelectedIndex = 0;

                await ReloadTreeAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка сброса фильтров: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для перезагрузки дерева досье по фильтрам
        private async Task ReloadTreeAsync()
        {
            int token = Interlocked.Increment(ref _treeReloadToken);

            string? productSearch = tbProductSearch.Text?.Trim();
            string? documentSearch = tbDocumentSearch.Text?.Trim();

            long? categoryId = GetSelectedCategoryId();
            ProductStatusEnum? productStatus = GetSelectedProductStatus();
            DocumentStatusEnum? docStatus = GetSelectedDocumentStatus();

            List<TreeNode> nodes = await DossierService.LoadTreeAsync(
                productSearch: productSearch,
                productStatus: productStatus,
                categoryId: categoryId,
                documentSearch: documentSearch,
                documentStatus: docStatus);

            if (token != _treeReloadToken)
            {
                return;
            }

            TreeItems.Clear();
            foreach (TreeNode n in nodes)
            {
                TreeItems.Add(n);
            }
        }

        // Метод для обработки двойного клика по элементу дерева
        private void tvDossier_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (tvDossier.SelectedItem is not TreeNode node)
            {
                return;
            }

            if (node.NodeType != NodeType.File)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(node.FilePath))
            {
                MessageBox.Show("Путь к файлу не задан", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                FileEditTrackingService.TrackOpenedFile(node, _currentUserId);
                DossierService.OpenFile(node.FilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть файл: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для получения узла дерева из параметра команды или из SelectedItem
        private bool TryGetNodeFromCommand(object? parameter, out TreeNode? node)
        {
            node = parameter as TreeNode;
            if (node != null)
            {
                return true;
            }

            node = tvDossier.SelectedItem as TreeNode;
            return node != null;
        }

        // Метод для добавления файла в категорию через команду контекстного меню
        private async void AddFileToCategory_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (!TryGetNodeFromCommand(e.Parameter, out TreeNode? node) || node == null)
            {
                return;
            }

            if (node.NodeType != NodeType.Category || node.CategoryId == null || node.ProductId == null)
            {
                return;
            }

            try
            {
                AddFileWindow w = new AddFileWindow
                {
                    Owner = Window.GetWindow(this)
                };

                if (w.ShowDialog() != true)
                {
                    return;
                }

                await DossierService.AddFileToCategoryAsync(
                    productId: node.ProductId.Value,
                    categoryId: node.CategoryId.Value,
                    currentUserId: _currentUserId,
                    parentDocumentId: null,
                    documentNumber: w.DocumentNumber,
                    documentName: w.DocumentName,
                    status: w.DocumentStatus,
                    sourceFilePath: w.SourceFilePath);

                await ReloadTreeAsync();
            }
            catch (Exception ex)
            {
                Exception baseEx = ex.GetBaseException();
                string msg = baseEx.Message;

                if (baseEx is PostgresException pg)
                {
                    msg = $"{pg.MessageText} (SQLSTATE: {pg.SqlState})";
                }

                MessageBox.Show("Не удалось добавить файл: " + msg,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для добавления дочернего файла через команду контекстного меню
        private async void AddChildFile_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (!TryGetNodeFromCommand(e.Parameter, out TreeNode? node) || node == null)
            {
                return;
            }

            // Дочерний файл добавляется из контекстного меню файла
            if (node.NodeType != NodeType.File || node.ProductId == null || node.CategoryId == null || node.DocumentId == null)
            {
                return;
            }

            try
            {
                AddChildFileWindow w = new AddChildFileWindow(parentCaption: node.DisplayName)
                {
                    Owner = Window.GetWindow(this)
                };

                if (w.ShowDialog() != true)
                {
                    return;
                }

                // parentDocumentId — документ, к которому привязан выбранный файл
                await DossierService.AddFileToCategoryAsync(
                    productId: node.ProductId.Value,
                    categoryId: node.CategoryId.Value,
                    currentUserId: _currentUserId,
                    parentDocumentId: node.DocumentId.Value,
                    documentNumber: w.DocumentNumber,
                    documentName: w.DocumentName,
                    status: w.DocumentStatus,
                    sourceFilePath: w.SourceFilePath);

                await ReloadTreeAsync();
            }
            catch (Exception ex)
            {
                Exception baseEx = ex.GetBaseException();
                MessageBox.Show("Не удалось добавить дочерний файл: " + baseEx.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для открытия файла через команду контекстного меню
        private void OpenFile_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (!TryGetNodeFromCommand(e.Parameter, out TreeNode? node) || node == null)
            {
                return;
            }

            if (node.NodeType != NodeType.File)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(node.FilePath))
            {
                MessageBox.Show("Путь к файлу не задан", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                DossierService.OpenFile(node.FilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть файл: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для перемещения файла в корзину через команду контекстного меню
        private async void DeleteFile_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (!TryGetNodeFromCommand(e.Parameter, out TreeNode? node) || node == null)
            {
                return;
            }

            if (node.NodeType != NodeType.File || node.FileId == null)
            {
                return;
            }

            // Сотрудник не может удалять
            if (_deleteRole == DeletePermissionRole.Employee)
            {
                MessageBox.Show("У вас нет прав на выполнение данной операции",
                    "Доступ запрещён", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (MessageBox.Show("Переместить файл в корзину", "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }

                await DossierService.DeleteFileAsync(node.FileId.Value, _currentUserId);
                await ReloadTreeAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось переместить файл в корзину: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для перемещения документа в корзину через команду контекстного меню
        private async void DeleteDocument_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (!TryGetNodeFromCommand(e.Parameter, out TreeNode? node) || node == null)
            {
                return;
            }

            if (_deleteRole == DeletePermissionRole.Employee)
            {
                MessageBox.Show("У вас нет прав на выполнение данной операции",
                    "Доступ запрещён", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            long? documentId = null;

            if (node.NodeType == NodeType.Document)
            {
                documentId = node.DocumentId;
            }

            if (node.NodeType == NodeType.File)
            {
                documentId = node.DocumentId;
            }

            if (documentId == null)
            {
                return;
            }

            try
            {
                var preview = await DossierService.GetDocumentDeletionPreviewAsync(documentId.Value);

                string text =
                    $"Переместить документ в корзину:\n{preview.DocumentNumber} — {preview.DocumentName}\n\n" +
                    $"Будут перемещены связанные данные:\n" +
                    $"• Документов: {preview.DocumentsToDeleteCount}\n" +
                    $"• Файлов: {preview.FilesToDeleteCount}\n\n" +
                    "Продолжить";

                if (MessageBox.Show(text, "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }

                await DossierService.DeleteDocumentAsync(documentId.Value, _currentUserId, _deleteRole);
                await ReloadTreeAsync();
            }
            catch (Exception ex)
            {
                Exception baseEx = ex.GetBaseException();
                string msg = baseEx.Message;

                if (baseEx is PostgresException pg)
                {
                    msg = $"{pg.MessageText} (SQLSTATE: {pg.SqlState})";
                }

                MessageBox.Show("Не удалось переместить документ в корзину: " + msg,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для перемещения изделия в корзину через команду контекстного меню
        private async void DeleteProduct_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (!TryGetNodeFromCommand(e.Parameter, out TreeNode? node) || node == null)
            {
                return;
            }

            if (node.NodeType != NodeType.Product || node.ProductId == null)
            {
                return;
            }

            if (_deleteRole == DeletePermissionRole.Employee)
            {
                MessageBox.Show("У вас нет прав на выполнение данной операции",
                    "Доступ запрещён", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var preview = await DossierService.GetProductDeletionPreviewAsync(node.ProductId.Value);

                string text =
                    $"Переместить изделие в корзину:\n{preview.ProductNumber} — {preview.ProductName}\n\n" +
                    $"Будут перемещены связанные данные:\n" +
                    $"• Документов: {preview.DocumentsToDeleteCount}\n" +
                    $"• Файлов: {preview.FilesToDeleteCount}\n\n" +
                    "Продолжить";

                if (MessageBox.Show(text, "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }

                await DossierService.DeleteProductAsync(node.ProductId.Value, _currentUserId, _deleteRole);
                await ReloadTreeAsync();
            }
            catch (Exception ex)
            {
                Exception baseEx = ex.GetBaseException();
                string msg = baseEx.Message;

                if (baseEx is PostgresException pg)
                {
                    msg = $"{pg.MessageText} (SQLSTATE: {pg.SqlState})";
                }

                MessageBox.Show("Не удалось переместить изделие в корзину: " + msg,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для добавления категории документов через кнопку +
        private async void btnAddCategory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddCategoryWindow w = new AddCategoryWindow
                {
                    Owner = Window.GetWindow(this)
                };

                if (w.ShowDialog() != true)
                {
                    return;
                }

                await DossierService.AddCategoryAsync(
                    name: w.CategoryName,
                    description: w.CategoryDescription,
                    sortOrder: w.SortOrder);

                await LoadCategoriesAsync();
                await ReloadTreeAsync();
            }
            catch (Exception ex)
            {
                Exception baseEx = ex.GetBaseException();
                MessageBox.Show("Не удалось добавить категорию: " + baseEx.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для добавления изделия через кнопку +
        private async void btnAddProduct_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddProductWindow w = new AddProductWindow
                {
                    Owner = Window.GetWindow(this)
                };

                if (w.ShowDialog() != true)
                {
                    return;
                }

                await DossierService.AddProductAsync(
                    productNumber: w.ProductNumber,
                    productName: w.ProductName,
                    description: w.ProductDescription,
                    status: w.ProductStatus);

                await ReloadTreeAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось добавить изделие: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для смены категории документа через контекстное меню
        private async void ChangeDocumentCategory_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (!TryGetNodeFromCommand(e.Parameter, out TreeNode? node) || node == null)
            {
                return;
            }

            if (node.NodeType != NodeType.File || node.FileId == null)
            {
                MessageBox.Show("Выберите файл в дереве", "Изменение категории",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (node.ParentDocumentId != null)
            {
                MessageBox.Show("Изменение категории доступно только для главного корневого документа",
                    "Изменение категории", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var details = await DossierService.GetFileDetailsAsync(node.FileId.Value);

                var win = new ChangeDocumentCategoryWindow(
                    documentId: details.Document.IdDocument,
                    currentCategoryId: details.Document.IdDocumentCategory,
                    categories: _categories);

                win.Owner = Window.GetWindow(this);

                if (win.ShowDialog() == true && win.SelectedCategoryId.HasValue)
                {
                    await DossierService.ChangeDocumentCategoryAsync(
                        documentId: details.Document.IdDocument,
                        newCategoryId: win.SelectedCategoryId.Value,
                        currentUserId: _currentUserId);

                    await ReloadTreeAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для редактирования файла или дочернего файла
        private async void EditFile_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (!TryGetNodeFromCommand(e.Parameter, out TreeNode? node) || node == null)
            {
                return;
            }

            if (node.NodeType != NodeType.File || node.FileId == null)
            {
                MessageBox.Show("Выберите файл в дереве", "Редактирование",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var details = await DossierService.GetFileDetailsAsync(node.FileId.Value);

                bool canReplaceFile =
                    _deleteRole == DeletePermissionRole.Admin ||
                    _deleteRole == DeletePermissionRole.SuperAdmin;

                var win = new EditFileWindow(details, canReplaceFile)
                {
                    Owner = Window.GetWindow(this)
                };

                if (win.ShowDialog() == true)
                {
                    await DossierService.UpdateFileAsync(
                        fileId: details.File.IdFile,
                        currentUserId: _currentUserId,
                        documentNumber: win.DocumentNumber,
                        documentName: win.DocumentName,
                        status: win.SelectedStatus,
                        newSourceFilePath: win.NewSourceFilePath);

                    await ReloadTreeAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для просмотра свойств файла или дочернего файла
        private async void FileProperties_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (!TryGetNodeFromCommand(e.Parameter, out TreeNode? node) || node == null)
            {
                return;
            }

            if (node.NodeType != NodeType.File || node.FileId == null)
            {
                MessageBox.Show("Выберите файл в дереве", "Свойства",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var details = await DossierService.GetFileDetailsAsync(node.FileId.Value);

                var win = new FilePropertiesWindow(details)
                {
                    Owner = Window.GetWindow(this)
                };

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}