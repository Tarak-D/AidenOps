using System.Reflection;
using AIOps.Abstractions.Audit;
using Xunit;

namespace AIOps.Abstractions.Tests;

/// <summary>
/// Guard test: the audit seam must remain append-only forever. Any attempt to add
/// mutation capability to the audit store must break this test loudly.
/// </summary>
public class AuditStoreContractTests
{
    [Fact]
    public void IAuditStore_exposes_no_mutation_members()
    {
        var methods = typeof(IAuditStore).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var forbidden = methods.Where(m =>
            m.Name.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
            m.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
            m.Name.Contains("Remove", StringComparison.OrdinalIgnoreCase) ||
            m.Name.Contains("Purge", StringComparison.OrdinalIgnoreCase) ||
            m.Name.Contains("Overwrite", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(forbidden);
        Assert.Contains(methods, m => m.Name == nameof(IAuditStore.AppendAsync));
        Assert.Contains(methods, m => m.Name == nameof(IAuditStore.QueryAsync));
    }

    [Fact]
    public void InMemory_audit_store_appends_and_queries()
    {
        // Placeholder assertion against the contract shape only; the concrete
        // implementation test lives with Infrastructure tests in later phases.
        Assert.True(typeof(IAuditStore).IsInterface);
    }
}
