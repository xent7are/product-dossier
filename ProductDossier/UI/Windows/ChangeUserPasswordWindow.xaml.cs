using ProductDossier.Data.Services;
using System.Windows;
using System.Windows.Input;

namespace ProductDossier
{
    // Окно смены пароля пользователя для супер-администратора
    public partial class ChangeUserPasswordWindow : Window
    {
        private readonly string _currentLogin;

        public bool IsPasswordChanged { get; private set; }
        public bool IsCurrentUserPasswordChanged { get; private set; }

        public ChangeUserPasswordWindow(string currentLogin)
        {
            InitializeComponent();

            _currentLogin = currentLogin ?? string.Empty;
            IsPasswordChanged = false;
            IsCurrentUserPasswordChanged = false;
        }

        // Применение нового пароля
        private async void btnApply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string targetLogin = (tbLogin.Text ?? string.Empty).Trim();
                string newPassword = pbNewPassword.Password ?? string.Empty;
                string confirmPassword = pbConfirmPassword.Password ?? string.Empty;

                if (string.IsNullOrWhiteSpace(targetLogin))
                {
                    MessageBox.Show(
                        "Введите логин пользователя.",
                        "Смена пароля",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var info = await UserPasswordService.GetPasswordChangeTargetInfoAsync(_currentLogin, targetLogin);

                if (!info.Exists)
                {
                    MessageBox.Show(
                        "Пользователь с указанным логином не найден.",
                        "Смена пароля",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (!info.CanChange)
                {
                    MessageBox.Show(
                        "Смена пароля другого супер-администратора запрещена.",
                        "Смена пароля",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(newPassword) || string.IsNullOrWhiteSpace(confirmPassword))
                {
                    MessageBox.Show(
                        "Все поля пароля должны быть заполнены.",
                        "Смена пароля",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
                {
                    MessageBox.Show(
                        "Введённые пароли не совпадают.",
                        "Смена пароля",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                IsCurrentUserPasswordChanged = await UserPasswordService.ChangeUserPasswordAsync(_currentLogin, targetLogin, newPassword);
                IsPasswordChanged = true;

                MessageBox.Show(
                    IsCurrentUserPasswordChanged
                        ? "Пароль успешно изменён. Текущая сессия переведена на новый пароль."
                        : "Пароль успешно изменён.",
                    "Смена пароля",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // Отмена и закрытие окна
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Запрет пробела в полях пароля
        private void PasswordBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                e.Handled = true;
            }
        }
    }
}