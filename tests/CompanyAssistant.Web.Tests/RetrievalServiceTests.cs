using CompanyAssistant.Web.Identity;
using CompanyAssistant.Web.Knowledge;

namespace CompanyAssistant.Web.Tests;

public sealed class RetrievalServiceTests
{
    [Fact]
    public void Employee_and_HR_map_to_distinct_policies()
    {
        Assert.Equal([AccessPolicies.Employee], AccessPolicyMapping.AllowedFor([AppPermissions.AssistantUse]));
        Assert.Equal([AccessPolicies.Hr], AccessPolicyMapping.AllowedFor([AppPermissions.HrDataRead]));
        Assert.Empty(AccessPolicyMapping.AllowedFor([AppPermissions.TechnicalAdministration]));
        Assert.Empty(AccessPolicyMapping.AllowedFor([]));
    }

    [Fact]
    public async Task Search_passes_employee_policy_for_employee_permission()
    {
        var store = new FakeRetrievalStore();
        var service = new RetrievalService(store);

        await service.SearchAsync(new float[3], new RetrievalCaller([AppPermissions.AssistantUse]), 5, CancellationToken.None);

        Assert.Single(store.SearchPolicyCalls);
        Assert.Equal([AccessPolicies.Employee], store.SearchPolicyCalls[0]);
    }

    [Fact]
    public async Task Search_passes_hr_policy_for_hr_permission()
    {
        var store = new FakeRetrievalStore();
        var service = new RetrievalService(store);

        await service.SearchAsync(new float[3], new RetrievalCaller([AppPermissions.HrDataRead]), 5, CancellationToken.None);

        Assert.Equal([AccessPolicies.Hr], store.SearchPolicyCalls[0]);
    }

    [Fact]
    public async Task HR_vs_employee_content_is_filtered_by_policy()
    {
        var store = new FakeRetrievalStore();
        store.Chunks.Add((FakeRetrievalStore.ChunkFor("leave", "Leave", "Leave body", AccessPolicies.Employee), AccessPolicies.Employee));
        store.Chunks.Add((FakeRetrievalStore.ChunkFor("hr", "HR contacts", "HR body", AccessPolicies.Hr), AccessPolicies.Hr));
        var service = new RetrievalService(store);

        var employeeResults = await service.SearchAsync(new float[3], new RetrievalCaller([AppPermissions.AssistantUse]), 5, CancellationToken.None);
        var hrResults = await service.SearchAsync(new float[3], new RetrievalCaller([AppPermissions.HrDataRead]), 5, CancellationToken.None);

        Assert.Single(employeeResults);
        Assert.Equal("Leave", employeeResults[0].Title);
        Assert.Single(hrResults);
        Assert.Equal("HR contacts", hrResults[0].Title);
    }

    [Fact]
    public async Task Revoked_permission_no_longer_returns_hr_content()
    {
        var store = new FakeRetrievalStore();
        store.Chunks.Add((FakeRetrievalStore.ChunkFor("hr", "HR contacts", "body", AccessPolicies.Hr), AccessPolicies.Hr));
        var service = new RetrievalService(store);

        var before = await service.SearchAsync(new float[3], new RetrievalCaller([AppPermissions.HrDataRead]), 5, CancellationToken.None);
        var after = await service.SearchAsync(new float[3], new RetrievalCaller([AppPermissions.AssistantUse]), 5, CancellationToken.None);

        Assert.Single(before);
        Assert.Empty(after);
    }

    [Fact]
    public void Retrieval_sql_applies_authorization_filters_before_ranking_and_limit()
    {
        var sql = NpgsqlRetrievalStore.SearchSql;

        var authorization = sql.IndexOf("d.access_policy = ANY($2)", StringComparison.Ordinal);
        var ranking = sql.IndexOf("ORDER BY", StringComparison.Ordinal);
        var limit = sql.IndexOf("LIMIT", StringComparison.Ordinal);

        Assert.True(authorization >= 0);
        Assert.True(authorization < ranking);
        Assert.True(ranking < limit);
        Assert.Contains("d.deleted_at IS NULL", sql);
        Assert.Contains("d.approval_state = 'approved'", sql);
        Assert.Contains("d.effective_date <= CURRENT_DATE", sql);
    }
}
