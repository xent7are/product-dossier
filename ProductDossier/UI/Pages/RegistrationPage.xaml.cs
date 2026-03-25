using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProductDossier.Data.Services;

namespace ProductDossier
{
    // Логика взаимодействия для RegistrationPage.xaml
    public partial class RegistrationPage : Page
    {
        public RegistrationPage()
        {
            InitializeComponent();
        }

        // Регистрация пользователя и переход на страницу авторизации
        private async void buttonRegistration_Click(object sender, RoutedEventArgs e)
        {
            string surname = surnameTextBlock.Text;
            string name = nameTextBlock.Text;
            string patronymic = patronymicTextBlock.Text;
            string login = loginTextBlock.Text;
            string password = passwordTextBlock.Password;

            if (string.IsNullOrEmpty(surname) || string.IsNullOrEmpty(name) ||
                string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Все обязательные поля должны быть заполнены!", "Ошибка регистрации",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!IsRussianLettersOnly(surname))
            {
                MessageBox.Show("Фамилия должна содержать только русские буквы!", "Ошибка регистрации",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!IsRussianLettersOnly(name))
            {
                MessageBox.Show("Имя должно содержать только русские буквы!", "Ошибка регистрации",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!string.IsNullOrEmpty(patronymic) && !IsRussianLettersOnly(patronymic))
            {
                MessageBox.Show("Отчество должно содержать только русские буквы!", "Ошибка регистрации",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!IsLoginValid(login, out string loginError))
            {
                MessageBox.Show(loginError, "Ошибка регистрации",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!PasswordValidationService.IsPasswordValid(password, login, surname, name, patronymic, out string passwordError))
            {
                MessageBox.Show(passwordError, "Ошибка регистрации",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var result = await AuthService.RegisterAsync(surname, name, patronymic, login, password);

            if (!result.IsSuccess)
            {
                MessageBox.Show(result.ErrorMessage, "Ошибка регистрации",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MessageBox.Show("Регистрация выполнена успешно!", "Регистрация",
                MessageBoxButton.OK, MessageBoxImage.Information);

            NavigationService.Navigate(new Uri("/UI/Pages/AuthorizationPage.xaml", UriKind.Relative));
        }

        // Возврат на страницу авторизации
        private void buttonBack_MouseDown(object sender, MouseButtonEventArgs e)
        {
            NavigationService.Navigate(new Uri("/UI/Pages/AuthorizationPage.xaml", UriKind.Relative));
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

        // Ограничение ввода в полях ФИО
        private void FioTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex(@"^[А-Яа-яЁё]+$");
            if (!regex.IsMatch(e.Text))
            {
                e.Handled = true;
            }
        }

        // Ограничение ввода в поле логина
        private void LoginTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex(@"^[a-zA-Z0-9_.]+$");
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

        // Проверка, что строка состоит только из русских букв
        private bool IsRussianLettersOnly(string value)
        {
            return Regex.IsMatch(value, @"^[А-Яа-яЁё]+$");
        }

        // Проверка логина по правилам
        private bool IsLoginValid(string login, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (login.Length < 3)
            {
                errorMessage = "Логин должен содержать минимум 3 символа!";
                return false;
            }

            if (!Regex.IsMatch(login, @"^[a-zA-Z0-9_.]+$"))
            {
                errorMessage = "Логин может содержать только латинские буквы, цифры, символы '_' и '.'!";
                return false;
            }

            int lettersCount = login.Count(char.IsLetter);
            if (lettersCount < 3)
            {
                errorMessage = "Логин должен содержать минимум 3 буквы (цифры допускаются, но букв должно быть не меньше 3)!";
                return false;
            }

            return true;
        }
    }
}