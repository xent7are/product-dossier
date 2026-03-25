using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ProductDossier.Data.Services
{
    // Сервис для получения и изменения ролей пользователей в БД
    public static class UserRoleService
    {
        public const string RoleEmployee = "pd_employee";
        public const string RoleAdmin = "pd_admin";
        public const string RoleSuperAdmin = "pd_superadmin";

        // Метод для получения отображаемой роли по логину (Сотрудник/Администратор/Супер-администратор)
        public static async Task<string> GetRoleLabelForLoginAsync(string login)
        {
            login = (login ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(login))
                return "—";

            await using var conn = new NpgsqlConnection(DbConnectionManager.ConnectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT CASE
                    WHEN pg_has_role(@login::text, 'pd_superadmin', 'member') THEN 'Супер-администратор'
                    WHEN pg_has_role(@login::text, 'pd_admin', 'member') THEN 'Администратор'
                    WHEN pg_has_role(@login::text, 'pd_employee', 'member') THEN 'Сотрудник'
                    ELSE 'Сотрудник'
                END;
            ";
            cmd.Parameters.AddWithValue("login", login);

            object? result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? "Сотрудник";
        }

        // Метод для проверки: текущий пользователь (current_user) является Супер-администратором
        private static async Task<bool> CurrentUserIsSuperAdminAsync()
        {
            await using var conn = new NpgsqlConnection(DbConnectionManager.ConnectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pg_has_role(current_user, 'pd_superadmin', 'member');";
            object? result = await cmd.ExecuteScalarAsync();
            return result is bool b && b;
        }

        // Метод для проверки: указанный логин является Супер-администратором
        private static async Task<bool> LoginIsSuperAdminAsync(string login)
        {
            await using var conn = new NpgsqlConnection(DbConnectionManager.ConnectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pg_has_role(@login::text, 'pd_superadmin', 'member');";
            cmd.Parameters.AddWithValue("login", login);

            object? result = await cmd.ExecuteScalarAsync();
            return result is bool b && b;
        }

        // Метод для установки роли пользователя в БД через хранимую процедуру (с проверками прав)
        public static async Task SetUserDbRoleAsync(string currentLogin, string targetLogin, string newRoleDbName)
        {
            currentLogin = (currentLogin ?? string.Empty).Trim();
            targetLogin = (targetLogin ?? string.Empty).Trim();
            newRoleDbName = (newRoleDbName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(targetLogin))
                throw new ArgumentException("Введите логин пользователя.");

            if (newRoleDbName != RoleEmployee && newRoleDbName != RoleAdmin && newRoleDbName != RoleSuperAdmin)
                throw new ArgumentException("Некорректная роль.");

            if (!await CurrentUserIsSuperAdminAsync())
                throw new UnauthorizedAccessException("Недостаточно прав: требуется роль Супер-администратор.");

            await using var db = new AppDbContext();

            bool exists = await db.Users.AsNoTracking().AnyAsync(u => u.Login == targetLogin);
            if (!exists)
                throw new InvalidOperationException("Пользователь с таким логином не найден.");

            bool targetIsSuperAdmin = await LoginIsSuperAdminAsync(targetLogin);
            if (targetIsSuperAdmin &&
                newRoleDbName != RoleSuperAdmin &&
                !string.Equals(targetLogin, currentLogin, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Нельзя забрать роль Супер-администратора у другого пользователя. " +
                    "Пользователь может снять эту роль только сам у себя.");
            }

            await db.Database.ExecuteSqlInterpolatedAsync($@"
                CALL product_dossier.set_user_db_role(
                    {targetLogin}::varchar,
                    {newRoleDbName}::varchar
                );
            ");
        }
    }
}