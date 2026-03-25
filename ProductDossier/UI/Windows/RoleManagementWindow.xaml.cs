using ProductDossier.Data.Services;
using System.Windows;

namespace ProductDossier
{
    public partial class RoleManagementWindow : Window
    {
        private readonly string _currentLogin;

        public bool IsRoleChanged { get; private set; }

        private sealed class RoleItem
        {
            public string Display { get; }
            public string DbName { get; }

            public RoleItem(string display, string dbName)
            {
                Display = display;
                DbName = dbName;
            }

            public override string ToString() => Display;
        }

        private readonly List<RoleItem> _roles = new()
        {
            new RoleItem("Сотрудник", UserRoleService.RoleEmployee),
            new RoleItem("Администратор", UserRoleService.RoleAdmin),
            new RoleItem("Супер-администратор", UserRoleService.RoleSuperAdmin)
        };

        public RoleManagementWindow(string currentLogin)
        {
            InitializeComponent();

            _currentLogin = currentLogin ?? string.Empty;

            cbRole.ItemsSource = _roles;
            cbRole.SelectedIndex = 0;

            IsRoleChanged = false;
            tbCurrentRoleInfo.Text = string.Empty;
        }

        // Метод для загрузки и отображения текущей роли пользователя при потере фокуса поля логина
        private async void tbLogin_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                string login = (tbLogin.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(login))
                {
                    tbCurrentRoleInfo.Text = string.Empty;
                    cbRole.IsEnabled = true;
                    return;
                }

                string roleLabel = await UserRoleService.GetRoleLabelForLoginAsync(login);
                tbCurrentRoleInfo.Text = $"Текущая роль: {roleLabel}";

                // Если это НЕ текущий пользователь и он уже супер-админ — не давать выбрать понижение (только супер-админ)
                bool isTargetSuper = roleLabel.Contains("Супер", StringComparison.OrdinalIgnoreCase);
                bool isSelf = string.Equals(login, _currentLogin, StringComparison.OrdinalIgnoreCase);

                if (isTargetSuper && !isSelf)
                {
                    cbRole.SelectedItem = _roles.First(r => r.DbName == UserRoleService.RoleSuperAdmin);
                    cbRole.IsEnabled = false;
                }
                else
                {
                    cbRole.IsEnabled = true;
                }
            }
            catch
            {
                tbCurrentRoleInfo.Text = string.Empty;
                cbRole.IsEnabled = true;
            }
        }

        // Метод для применения выбранной роли пользователю и закрытия окна при успехе
        private async void btnApply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string targetLogin = (tbLogin.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(targetLogin))
                {
                    MessageBox.Show("Введите логин пользователя.", "Роли",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (cbRole.SelectedItem is not RoleItem role)
                {
                    MessageBox.Show("Выберите роль.", "Роли",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                await UserRoleService.SetUserDbRoleAsync(_currentLogin, targetLogin, role.DbName);

                IsRoleChanged = true;

                MessageBox.Show("Роль успешно изменена.", "Роли",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Метод для отмены изменений и закрытия окна
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}