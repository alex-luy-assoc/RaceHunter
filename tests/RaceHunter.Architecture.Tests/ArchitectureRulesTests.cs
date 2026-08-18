using System.Reflection;
using RaceHunter.Application;
using RaceHunter.Contracts;
using RaceHunter.Domain;
using RaceHunter.Infrastructure.Persistence;
using Xunit;

namespace RaceHunter.Architecture.Tests;

public sealed class ArchitectureRulesTests
{
    [Fact]
    public void Domain_has_no_project_dependencies() =>
        Assert.Empty(ProjectReferences(typeof(DomainAssemblyMarker).Assembly));

    [Fact]
    public void Application_depends_only_on_domain() =>
        Assert.Equal(["RaceHunter.Domain"], ProjectReferences(typeof(ApplicationAssemblyMarker).Assembly));

    [Fact]
    public void Contracts_has_no_project_dependencies() =>
        Assert.Empty(ProjectReferences(typeof(ContractsAssemblyMarker).Assembly));

    [Fact]
    public void Infrastructure_does_not_expose_provider_types() =>
        Assert.DoesNotContain(
            typeof(RaceHunterDbContext).Assembly.GetExportedTypes().SelectMany(ExportedSignatureTypes),
            type => type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) is true ||
                    type.Namespace?.StartsWith("Npgsql", StringComparison.Ordinal) is true);

    [Fact]
    public void Application_has_no_inline_sql_apis()
    {
        var forbidden = new[] { "FromSql", "ExecuteSql", "SqlQuery" };
        var methodNames = typeof(ApplicationAssemblyMarker).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .Select(method => method.Name);

        Assert.DoesNotContain(methodNames, method => forbidden.Any(prefix => method.StartsWith(prefix, StringComparison.Ordinal)));
    }

    private static string[] ProjectReferences(Assembly assembly) => assembly.GetReferencedAssemblies()
        .Select(reference => reference.Name!)
        .Where(name => name.StartsWith("RaceHunter.", StringComparison.Ordinal))
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static IEnumerable<Type> ExportedSignatureTypes(Type type) => type.GetMethods()
        .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
        .Concat(type.GetProperties().Select(property => property.PropertyType));
}
