using ProductDossier.Data.Services;

namespace TestProductDossier
{
    // Тестирование модуля проверки пароля PasswordValidationService
    [TestClass]
    public class PasswordValidationServiceTests
    {
        [TestMethod]
        // Проверка обработки пустого пароля
        public void IsPasswordValidEmptyPasswordReturnsFalseAndInputPasswordMessage()
        {
            bool result = PasswordValidationService.IsPasswordValid(
                string.Empty,
                "userlogin",
                "Иванов",
                "Петр",
                "Сергеевич",
                out string errorMessage);

            Assert.AreEqual(false, result);
            Assert.AreEqual("Введите пароль.", errorMessage);
        }

        [TestMethod]
        // Проверка обработки пароля короче восьми символов
        public void IsPasswordValidShortPasswordReturnsFalseAndMinimumLengthMessage()
        {
            bool result = PasswordValidationService.IsPasswordValid(
                "Abc1234",
                "userlogin",
                "Иванов",
                "Петр",
                "Сергеевич",
                out string errorMessage);

            Assert.AreEqual(false, result);
            Assert.AreEqual("Пароль должен содержать минимум 8 символов!", errorMessage);
        }

        [TestMethod]
        // Проверка обработки пароля без букв
        public void IsPasswordValidPasswordWithoutLettersReturnsFalseAndLettersAndDigitsMessage()
        {
            bool result = PasswordValidationService.IsPasswordValid(
                "12345678",
                "userlogin",
                "Иванов",
                "Петр",
                "Сергеевич",
                out string errorMessage);

            Assert.AreEqual(false, result);
            Assert.AreEqual("Пароль должен содержать и буквы, и цифры!", errorMessage);
        }

        [TestMethod]
        // Проверка обработки пароля без цифр
        public void IsPasswordValidPasswordWithoutDigitsReturnsFalseAndLettersAndDigitsMessage()
        {
            bool result = PasswordValidationService.IsPasswordValid(
                "Password",
                "userlogin",
                "Иванов",
                "Петр",
                "Сергеевич",
                out string errorMessage);

            Assert.AreEqual(false, result);
            Assert.AreEqual("Пароль должен содержать и буквы, и цифры!", errorMessage);
        }

        [TestMethod]
        // Проверка обработки пароля с цифровой клавиатурной последовательностью
        public void IsPasswordValidPasswordWithDigitKeyboardSequenceReturnsFalseAndKeyboardSequenceMessage()
        {
            bool result = PasswordValidationService.IsPasswordValid(
                "T9$123ab",
                "userlogin",
                "Иванов",
                "Петр",
                "Сергеевич",
                out string errorMessage);

            Assert.AreEqual(false, result);
            Assert.AreEqual("Пароль слишком простой: содержит 3 или более подряд идущих символов на клавиатуре (например: 123, qwe, йцу)!", errorMessage);
        }

        [TestMethod]
        // Проверка обработки пароля с английской клавиатурной последовательностью
        public void IsPasswordValidPasswordWithEnglishKeyboardSequenceReturnsFalseAndKeyboardSequenceMessage()
        {
            bool result = PasswordValidationService.IsPasswordValid(
                "R4$bvcTy",
                "userlogin",
                "Иванов",
                "Петр",
                "Сергеевич",
                out string errorMessage);

            Assert.AreEqual(false, result);
            Assert.AreEqual("Пароль слишком простой: содержит 3 или более подряд идущих символов на клавиатуре (например: 123, qwe, йцу)!", errorMessage);
        }

        [TestMethod]
        // Проверка обработки пароля с русской клавиатурной последовательностью
        public void IsPasswordValidPasswordWithRussianKeyboardSequenceReturnsFalseAndKeyboardSequenceMessage()
        {
            bool result = PasswordValidationService.IsPasswordValid(
                "Q7$апрЖ9",
                "userlogin",
                "Иванов",
                "Петр",
                "Сергеевич",
                out string errorMessage);

            Assert.AreEqual(false, result);
            Assert.AreEqual("Пароль слишком простой: содержит 3 или более подряд идущих символов на клавиатуре (например: 123, qwe, йцу)!", errorMessage);
        }

        [TestMethod]
        // Проверка обработки пароля с фрагментом логина
        public void IsPasswordValidPasswordWithLoginFragmentReturnsFalseAndPersonalDataFragmentMessage()
        {
            bool result = PasswordValidationService.IsPasswordValid(
                "T9_alEx%",
                "alexroot",
                "Иванов",
                "Петр",
                "Сергеевич",
                out string errorMessage);

            Assert.AreEqual(false, result);
            Assert.AreEqual("Пароль слишком простой: содержит фрагмент (3 и более подряд) из логина или ФИО!", errorMessage);
        }

        [TestMethod]
        // Проверка обработки пароля с фрагментом фамилии
        public void IsPasswordValidPasswordWithSurnameFragmentReturnsFalseAndPersonalDataFragmentMessage()
        {
            bool result = PasswordValidationService.IsPasswordValid(
                "Q7_ИваХ!",
                "userlogin",
                "Иванов",
                "Петр",
                null,
                out string errorMessage);

            Assert.AreEqual(false, result);
            Assert.AreEqual("Пароль слишком простой: содержит фрагмент (3 и более подряд) из логина или ФИО!", errorMessage);
        }

        [TestMethod]
        // Проверка обработки пароля с фрагментом имени
        public void IsPasswordValidPasswordWithNameFragmentReturnsFalseAndPersonalDataFragmentMessage()
        {
            bool result = PasswordValidationService.IsPasswordValid(
                "Z5Пет$Q!",
                "userlogin",
                "Иванов",
                "Петр",
                null,
                out string errorMessage);

            Assert.AreEqual(false, result);
            Assert.AreEqual("Пароль слишком простой: содержит фрагмент (3 и более подряд) из логина или ФИО!", errorMessage);
        }

        [TestMethod]
        // Проверка обработки пароля с фрагментом отчества
        public void IsPasswordValidPasswordWithPatronymicFragmentReturnsFalseAndPersonalDataFragmentMessage()
        {
            bool result = PasswordValidationService.IsPasswordValid(
                "M8Сер*W!",
                "userlogin",
                "Иванов",
                "Петр",
                "Сергеевич",
                out string errorMessage);

            Assert.AreEqual(false, result);
            Assert.AreEqual("Пароль слишком простой: содержит фрагмент (3 и более подряд) из логина или ФИО!", errorMessage);
        }

        [TestMethod]
        // Проверка успешной обработки корректного пароля без отчества
        public void IsPasswordValidValidPasswordWithoutPatronymicReturnsTrueAndEmptyMessage()
        {
            bool result = PasswordValidationService.IsPasswordValid(
                "Q9$nijT7",
                "userlogin",
                "Иванов",
                "Петр",
                null,
                out string errorMessage);

            Assert.AreEqual(true, result);
            Assert.AreEqual(string.Empty, errorMessage);
        }
    }
}