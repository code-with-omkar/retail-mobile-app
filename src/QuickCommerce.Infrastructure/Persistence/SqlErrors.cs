using Microsoft.Data.SqlClient;

namespace QuickCommerce.Infrastructure.Persistence;

internal static class SqlErrors
{
    /// <summary>SQL Server chose this request as the deadlock victim (error 1205): nothing was changed, and asking again is the right answer.</summary>
    public static bool IsDeadlock(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is SqlException { Number: 1205 })
            {
                return true;
            }

            if (current.InnerException is null)
            {
                break;
            }
        }

        return false;
    }
}
