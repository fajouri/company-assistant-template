using Npgsql;
using Pgvector;

namespace CompanyAssistant.Web.Knowledge;

internal static class NpgsqlDataSources
{
    public static NpgsqlDataSource CreateVectorDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
        return builder.Build();
    }
}
