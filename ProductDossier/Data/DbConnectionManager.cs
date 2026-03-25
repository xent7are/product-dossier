namespace ProductDossier.Data
{
    // Менеджер строки подключения приложения
    public static class DbConnectionManager
    {
        // Строка подключения сервисного пользователя (используется только для регистрации)
        // 192.168.0.100
        // localhost
        public static string ServiceConnectionString { get; set; }
            = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=123456;Search Path=product_dossier";

        // Текущая строка подключения приложения (после авторизации переключается на пользователя)
        public static string ConnectionString { get; set; } = ServiceConnectionString;

        // Метод для формирования строки подключения под пользователя
        public static string BuildUserConnectionString(string login, string password)
        {
            return $"Host=localhost;Port=5432;Database=postgres;Username={login};Password={password};Search Path=product_dossier";
        }
    }
}