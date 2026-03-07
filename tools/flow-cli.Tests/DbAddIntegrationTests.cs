using FluentAssertions;
using FlowCLI.Models;
using FlowCLI.Tests.Fixtures;

namespace FlowCLI.Tests;

/// <summary>
/// Integration tests for db-add command with time-delayed search.
/// Tests that documents added via AddDocument can be retrieved after a delay.
/// </summary>
public class DbAddIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public DbAddIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void DbAdd_FiveDocuments_WaitOneMinute_CanQueryAll()
    {
        // Arrange - Create 5 new test documents
        var testDocuments = new List<TaskRecord>
        {
            new()
            {
                Content = "Test document 1 for delayed search",
                CanonicalTags = "test,delayed,search",
                FeatureName = "delayed_search_test",
                CommitId = "test001",
                StateAtCreation = "IDLE",
                Metadata = "{}",
                PlanText = "Test plan 1",
                ResultText = "Test result 1"
            },
            new()
            {
                Content = "Test document 2 for delayed search",
                CanonicalTags = "test,delayed,search",
                FeatureName = "delayed_search_test",
                CommitId = "test002",
                StateAtCreation = "EXECUTING",
                Metadata = "{}",
                PlanText = "Test plan 2",
                ResultText = "Test result 2"
            },
            new()
            {
                Content = "Test document 3 for delayed search",
                CanonicalTags = "test,delayed,search",
                FeatureName = "delayed_search_test",
                CommitId = "test003",
                StateAtCreation = "VALIDATING",
                Metadata = "{}",
                PlanText = "Test plan 3",
                ResultText = "Test result 3"
            },
            new()
            {
                Content = "Test document 4 for delayed search",
                CanonicalTags = "test,delayed,search",
                FeatureName = "delayed_search_test",
                CommitId = "test004",
                StateAtCreation = "COMPLETED",
                Metadata = "{}",
                PlanText = "Test plan 4",
                ResultText = "Test result 4"
            },
            new()
            {
                Content = "Test document 5 for delayed search",
                CanonicalTags = "test,delayed,search",
                FeatureName = "delayed_search_test",
                CommitId = "test005",
                StateAtCreation = "BLOCKED",
                Metadata = "{}",
                PlanText = "Test plan 5",
                ResultText = "Test result 5"
            }
        };

        using var service = _fixture.CreateService();

        // Act - Add 5 documents
        var addedIds = new List<int>();
        foreach (var doc in testDocuments)
        {
            var id = service.AddDocument(doc);
            addedIds.Add(id);
        }

        // Wait for 1 minute (60 seconds)
        Thread.Sleep(TimeSpan.FromMinutes(1));

        // Query for the documents
        var results = service.Query(
            query: "delayed search",
            tags: null,
            top: 10);

        // Assert - Should find at least the 5 documents we added
        results.Should().HaveCountGreaterThanOrEqualTo(5,
            "because we added 5 documents with 'delayed search' content");

        // Verify all added IDs are in the results
        var resultIds = results.Select(r => r.Id).ToList();
        foreach (var addedId in addedIds)
        {
            resultIds.Should().Contain(addedId,
                $"because document with ID {addedId} was added");
        }

        // Verify content matches
        var resultContents = results.Select(r => r.Content).ToList();
        foreach (var doc in testDocuments)
        {
            resultContents.Should().Contain(doc.Content,
                $"because document with content '{doc.Content}' was added");
        }
    }

    [Fact]
    public void DbAdd_FiveDocuments_WaitOneMinute_CanQueryByTags()
    {
        // Arrange - Create 5 documents with specific tag
        var uniqueTag = $"integration-test-{Guid.NewGuid():N}";
        var testDocuments = Enumerable.Range(1, 5).Select(i => new TaskRecord
        {
            Content = $"Integration test document {i}",
            CanonicalTags = $"{uniqueTag},integration,test",
            FeatureName = "integration_test",
            CommitId = $"int{i:D3}",
            StateAtCreation = "IDLE",
            Metadata = "{}",
            PlanText = $"Integration plan {i}",
            ResultText = $"Integration result {i}"
        }).ToList();

        using var service = _fixture.CreateService();

        // Act - Add 5 documents
        var addedIds = new List<int>();
        foreach (var doc in testDocuments)
        {
            var id = service.AddDocument(doc);
            addedIds.Add(id);
        }

        // Wait for 1 minute
        Thread.Sleep(TimeSpan.FromMinutes(1));

        // Query by unique tag
        var results = service.Query(
            query: null,
            tags: uniqueTag,
            top: 10);

        // Assert
        results.Should().HaveCount(5,
            $"because we added exactly 5 documents with tag '{uniqueTag}'");

        var resultIds = results.Select(r => r.Id).ToList();
        foreach (var addedId in addedIds)
        {
            resultIds.Should().Contain(addedId,
                $"because document with ID {addedId} was added with tag '{uniqueTag}'");
        }
    }
}
