using FlowCLI.Services;
using FlowCLI.Tests.Fixtures;
using FluentAssertions;

namespace FlowCLI.Tests.Services;

/// <summary>
/// Tests for vector indexing functionality in DatabaseService.
/// Since sqlite-vec and embed.exe are not available in test environment,
/// tests verify graceful fallback, serialization helpers, and no-op behavior.
/// </summary>
public class VectorIndexingTests : IClassFixture<TestDatabaseFixture>, IDisposable
{
    private readonly TestDatabaseFixture _fixture;
    private readonly DatabaseService _service;

    public VectorIndexingTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
        _service = _fixture.CreateService();
    }

    /// <summary>
    /// VI-01: EnsureVectorIndexAsync completes without error when vector tables don't exist.
    /// </summary>
    [Fact]
    public async Task VI01_EnsureVectorIndex_NoVecTables_CompletesGracefully()
    {
        // In test env, sqlite-vec is not loaded → vec tables don't exist → should return immediately
        await _service.EnsureVectorIndexAsync();
        // No exception = success
    }

    /// <summary>
    /// VI-02: EnsureVectorIndexAsync is idempotent (second call returns immediately).
    /// </summary>
    [Fact]
    public async Task VI02_EnsureVectorIndex_CalledTwice_SecondCallIsNoop()
    {
        await _service.EnsureVectorIndexAsync();
        await _service.EnsureVectorIndexAsync(); // Should return immediately due to _vectorIndexChecked flag
        // No exception = success
    }

    /// <summary>
    /// VI-03: SerializeVector converts float[] to byte[] correctly.
    /// </summary>
    [Fact]
    public void VI03_SerializeVector_ProducesCorrectBytes()
    {
        var vector = new float[] { 1.0f, 2.0f, 3.0f };
        var bytes = DatabaseService.SerializeVector(vector);

        bytes.Should().HaveCount(3 * sizeof(float)); // 12 bytes
        // Verify round-trip
        var restored = DatabaseService.DeserializeVector(bytes);
        restored.Should().BeEquivalentTo(vector);
    }

    /// <summary>
    /// VI-04: SerializeVector handles empty vector.
    /// </summary>
    [Fact]
    public void VI04_SerializeVector_EmptyVector_ReturnsEmptyBytes()
    {
        var vector = Array.Empty<float>();
        var bytes = DatabaseService.SerializeVector(vector);

        bytes.Should().BeEmpty();
    }

    /// <summary>
    /// VI-05: SerializeVector handles large vector (1024 dimensions like BGE-M3).
    /// </summary>
    [Fact]
    public void VI05_SerializeVector_LargeVector_RoundTrips()
    {
        var vector = new float[1024];
        var rng = new Random(42);
        for (int i = 0; i < vector.Length; i++)
            vector[i] = (float)(rng.NextDouble() * 2 - 1); // [-1, 1]

        var bytes = DatabaseService.SerializeVector(vector);
        bytes.Should().HaveCount(1024 * sizeof(float)); // 4096 bytes

        var restored = DatabaseService.DeserializeVector(bytes);
        restored.Should().BeEquivalentTo(vector);
    }

    /// <summary>
    /// VI-06: DeserializeVector handles special float values (NaN, Infinity).
    /// </summary>
    [Fact]
    public void VI06_SerializeVector_SpecialValues_RoundTrips()
    {
        var vector = new float[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, float.Epsilon };
        var bytes = DatabaseService.SerializeVector(vector);
        var restored = DatabaseService.DeserializeVector(bytes);

        // NaN equality is special — compare element by element
        restored.Length.Should().Be(5);
        float.IsNaN(restored[0]).Should().BeTrue();
        float.IsPositiveInfinity(restored[1]).Should().BeTrue();
        float.IsNegativeInfinity(restored[2]).Should().BeTrue();
        restored[3].Should().Be(0f);
        restored[4].Should().Be(float.Epsilon);
    }

    /// <summary>
    /// VI-07: Query still works after EnsureVectorIndexAsync (no side effects).
    /// </summary>
    [Fact]
    public async Task VI07_Query_AfterEnsureVectorIndex_StillWorks()
    {
        await _service.EnsureVectorIndexAsync();
        var results = _service.Query(query: "CLI", top: 5);
        results.Should().NotBeEmpty();
    }

    /// <summary>
    /// VI-08: VectorTablesExist returns false when vec tables don't exist (test env).
    /// Query auto-indexing via EnsureVectorIndexAsync does not interfere with text search.
    /// </summary>
    [Fact]
    public void VI08_Query_AutoIndex_DoesNotInterfereWithTextSearch()
    {
        // First query triggers EnsureVectorIndexAsync → should be no-op
        var results1 = _service.Query(query: "CLI");
        // Second query → _vectorIndexChecked is true → immediate return
        var results2 = _service.Query(query: "database");

        results1.Should().NotBeEmpty();
        results2.Should().NotBeEmpty();
    }

    public void Dispose() => _service.Dispose();
}
