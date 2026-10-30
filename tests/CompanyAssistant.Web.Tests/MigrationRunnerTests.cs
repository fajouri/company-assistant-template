using CompanyAssistant.Web.Knowledge;

namespace CompanyAssistant.Web.Tests;

public sealed class MigrationRunnerTests
{
    [Fact]
    public void On_delete_cascade_is_not_a_destructive_statement() =>
        MigrationRunner.GuardNonDestructive(
            "CREATE TABLE IF NOT EXISTS t (id bigint, document_id text REFERENCES d (document_id) ON DELETE CASCADE);");

    [Theory]
    [InlineData("DROP TABLE documents;")]
    [InlineData("TRUNCATE TABLE documents;")]
    [InlineData("DELETE FROM documents;")]
    public void Destructive_statements_are_rejected(string sql) =>
        Assert.Throws<InvalidOperationException>(() => MigrationRunner.GuardNonDestructive(sql));
}
