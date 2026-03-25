namespace ProductDossier.Data.Enums
{
    // Роли для операций удаления (используются для проверки прав в UI и сервисах)
    public enum DeletePermissionRole
    {
        Employee,
        Admin,
        SuperAdmin
    }
}
