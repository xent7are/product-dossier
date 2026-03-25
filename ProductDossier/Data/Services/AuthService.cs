using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProductDossier.Data.Entities;

namespace ProductDossier.Data.Services
{
    // Сервис для регистрации и авторизации пользователей
    public static class AuthService
    {
        // Метод для регистрации пользователя через процедуру БД с созданием DB-пользователя
        public static async Task<(bool IsSuccess, string ErrorMessage)> RegisterAsync(
            string surname,
            string name,
            string? patronymic,
            string login,
            string password)
        {
            try
            {
                // Выполнения регистрации через сервисное подключение
                DbConnectionManager.ConnectionString = DbConnectionManager.ServiceConnectionString;
                await using AppDbContext db = new AppDbContext();

                // Вызов процедуры регистрации в БД (создаёт DB-логин и профиль в таблице)
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    CALL product_dossier.register_user(
                        {login}::varchar,
                        {password}::varchar,
                        {surname}::varchar,
                        {name}::varchar,
                        {patronymic}::varchar
                    );
                ");

                return (true, string.Empty);
            }
            catch (PostgresException ex)
            {
                // Обработка ошибок PostgreSQL
                return (false, BuildDbErrorMessage(ex));
            }
            catch (Exception)
            {
                // Обработка непредвиденных ошибок
                return (false, "Непредвиденная ошибка при регистрации пользователя!");
            }
        }

        // Метод для авторизации пользователя через подключение под DB-логином
        public static async Task<(bool IsSuccess, string ErrorMessage, User? User)> LoginAsync(
            string login,
            string password)
        {
            try
            {
                // Формирование строки подключения под пользователя
                string userConnectionString = DbConnectionManager.BuildUserConnectionString(login, password);

                // Проверка подключения под пользователем
                var connectCheck = await TryOpenConnectionAsync(userConnectionString);
                if (!connectCheck.IsSuccess)
                {
                    return (false, connectCheck.ErrorMessage, null);
                }

                // Переключение приложения на подключение под пользователем
                DbConnectionManager.ConnectionString = userConnectionString;

                // Получения профиля пользователя из таблицы users
                await using AppDbContext userDb = new AppDbContext();
                User? user = await userDb.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Login == login);

                if (user == null)
                {
                    return (false, "Профиль пользователя не найден в таблице users!", null);
                }

                return (true, string.Empty, user);
            }
            catch (PostgresException ex)
            {
                // Обработка ошибок PostgreSQL
                return (false, BuildDbErrorMessage(ex), null);
            }
            catch (Exception)
            {
                // Обработка непредвиденных ошибок
                return (false, "Непредвиденная ошибка при авторизации!", null);
            }
        }

        // Метод для выхода из учетной записи и возврата на сервисное подключение
        public static void Logout()
        {
            // Переключение приложения на сервисное подключение
            DbConnectionManager.ConnectionString = DbConnectionManager.ServiceConnectionString;

            // Очистка DataSource и пулов соединений
            AppDbContext.ClearDataSourceCache();

            // Разрыв активных соединений через очистку пула
            NpgsqlConnection.ClearAllPools();
        }

        // Метод для попытки открытия подключения по строке подключения
        private static async Task<(bool IsSuccess, string ErrorMessage)> TryOpenConnectionAsync(string connectionString)
        {
            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1;";
                await cmd.ExecuteScalarAsync();

                return (true, string.Empty);
            }
            catch (PostgresException ex)
            {
                // Обработка ошибок PostgreSQL
                return (false, BuildDbErrorMessage(ex));
            }
            catch
            {
                // Обработка непредвиденных ошибок
                return (false, "Не удалось подключиться к БД! Проверьте логин/пароль.");
            }
        }

        // Метод для формирования текста ошибки БД в понятном виде
        private static string BuildDbErrorMessage(PostgresException ex)
        {
            if (ex.SqlState == "28P01")
            {
                return "Неверный логин или пароль!";
            }

            if (ex.SqlState == "3D000")
            {
                return "База данных не найдена!";
            }

            if (ex.SqlState == "08001")
            {
                return "Не удалось подключиться к серверу БД!";
            }

            return "Ошибка БД: " + ex.MessageText;
        }
    }
}