using System.Text.RegularExpressions;

namespace ProductDossier.Data.Services
{
    // Сервис проверки пароля по правилам безопасности приложения
    public static class PasswordValidationService
    {
        // Разрешённые символы для ввода пароля
        private const string AllowedPasswordInputPattern = @"^[a-zA-Zа-яА-ЯЁё0-9!@#$%^&*()_\-+=\[\]{};:'"",.<>/?\\|`~]+$";

        // Проверка пароля по правилам безопасности
        public static bool IsPasswordValid(string password, string login, string surname, string name, string? patronymic, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(password))
            {
                errorMessage = "Введите пароль.";
                return false;
            }

            if (password.Length < 8)
            {
                errorMessage = "Пароль должен содержать минимум 8 символов!";
                return false;
            }

            bool hasLetter = Regex.IsMatch(password, @"[A-Za-zА-Яа-яЁё]");
            bool hasDigit = Regex.IsMatch(password, @"\d");

            if (!hasLetter || !hasDigit)
            {
                errorMessage = "Пароль должен содержать и буквы, и цифры!";
                return false;
            }

            if (HasKeyboardSequence(password, 3))
            {
                errorMessage = "Пароль слишком простой: содержит 3 или более подряд идущих символов на клавиатуре (например: 123, qwe, йцу)!";
                return false;
            }

            if (ContainsAnyConsecutiveFragment(password, login) ||
                ContainsAnyConsecutiveFragment(password, surname) ||
                ContainsAnyConsecutiveFragment(password, name) ||
                (!string.IsNullOrWhiteSpace(patronymic) && ContainsAnyConsecutiveFragment(password, patronymic!)))
            {
                errorMessage = "Пароль слишком простой: содержит фрагмент (3 и более подряд) из логина или ФИО!";
                return false;
            }

            return true;
        }

        // Проверка допустимости вводимых символов пароля
        public static bool IsPasswordInputTextAllowed(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            return Regex.IsMatch(text, AllowedPasswordInputPattern);
        }

        // Проверка наличия в пароле фрагмента длиной 3+ подряд из заданной строки
        private static bool ContainsAnyConsecutiveFragment(string password, string source)
        {
            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            string pass = password.ToLowerInvariant();
            string src = source.ToLowerInvariant();

            if (src.Length < 3)
            {
                return false;
            }

            for (int len = 3; len <= src.Length; len++)
            {
                for (int i = 0; i + len <= src.Length; i++)
                {
                    string part = src.Substring(i, len);
                    if (pass.Contains(part))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // Проверка наличия последовательностей 3+ подряд идущих символов на клавиатуре
        private static bool HasKeyboardSequence(string value, int minLen)
        {
            if (string.IsNullOrEmpty(value) || value.Length < minLen)
            {
                return false;
            }

            string text = value.ToLowerInvariant();

            string[] sequences =
            {
                "1234567890",
                "0987654321",
                "qwertyuiop",
                "poiuytrewq",
                "asdfghjkl",
                "lkjhgfdsa",
                "zxcvbnm",
                "mnbvcxz",
                "йцукенгшщзхъ",
                "ъхзщшгнекуцй",
                "фывапролджэ",
                "эждлорпавыф",
                "ячсмитьбю",
                "юбьтимсчя"
            };

            for (int i = 0; i + minLen <= text.Length; i++)
            {
                string part = text.Substring(i, minLen);

                foreach (string seq in sequences)
                {
                    if (seq.Contains(part))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}