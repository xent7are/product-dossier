using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProductDossier.Data.Services;

namespace ProductDossier
{
    // Логика взаимодействия для AuthorizationPage.xaml
    public partial class AuthorizationPage : Page
    {
        public AuthorizationPage()
        {
            InitializeComponent();
        }

        // Переход на страницу регистрации
        private void buttonRegistration_Click(object sender, RoutedEventArgs e)
        {
            NavigationService.Navigate(new Uri("/UI/Pages/RegistrationPage.xaml", UriKind.Relative));
        }

        // Авторизация пользователя и переход на главную страницу
        private async void buttonLogIn_Click(object sender, RoutedEventArgs e)
        {
            string login = loginTextBlock.Text;
            string password = passwordTextBlock.Password;

            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Все поля должны быть заполнены!", "Ошибка входа", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var result = await AuthService.LoginAsync(login, password);

            if (!result.IsSuccess)
            {
                MessageBox.Show(result.ErrorMessage, "Ошибка входа", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            NavigationService.Navigate(new MainPage(result.User!.Login));
        }

        // Запрет пробела в текстовом поле
        private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                e.Handled = true;
            }
        }

        // Запрет пробела в поле ввода пароля
        private void PasswordBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                e.Handled = true;
            }
        }

        // Ограничение ввода в текстовом поле логина
        private void TextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex(@"^[a-zA-Zа-яА-Я0-9_.]+$");
            if (!regex.IsMatch(e.Text))
            {
                e.Handled = true;
            }
        }

        // Ограничение ввода в поле пароля через сервис
        private void PasswordBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (!PasswordValidationService.IsPasswordInputTextAllowed(e.Text))
            {
                e.Handled = true;
            }
        }
    }
}