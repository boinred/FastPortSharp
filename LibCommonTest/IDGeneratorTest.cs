using LibCommons;

namespace LibCommonTest;

[TestClass]
public sealed class IDGeneratorTest
{
    [TestMethod]
    public void TestIDGenerator_GetNextGeneratedId_Increments()
    {
        // Arrange
        var generator = new IDGenerator();

        // Act
        long id1 = generator.GetNextGeneratedId();
        long id2 = generator.GetNextGeneratedId();
        long id3 = generator.GetNextGeneratedId();

        // Assert
        Assert.AreEqual(1, id1);
        Assert.AreEqual(2, id2);
        Assert.AreEqual(3, id3);
    }

    [TestMethod]
    public void TestIDGenerator_GetNextGeneratedId_ThreadSafe()
    {
        // Arrange
        var generator = new IDGenerator();
        var ids = new System.Collections.Concurrent.ConcurrentBag<long>();
        int threadCount = 10;
        int idsPerThread = 100;

        // Act
        Parallel.For(0, threadCount, _ =>
        {
            for (int i = 0; i < idsPerThread; i++)
            {
                ids.Add(generator.GetNextGeneratedId());
            }
        });

        // Assert: 모든 ID가 고유해야 함
        Assert.AreEqual(threadCount * idsPerThread, ids.Count);
        Assert.AreEqual(threadCount * idsPerThread, ids.Distinct().Count());
    }

    [TestMethod]
    public void TestIDGenerator_GetNextGeneratedGuid_ReturnsUniqueGuids()
    {
        // Arrange
        var generator = new IDGenerator();

        // Act
        Guid guid1 = generator.GetNextGeneratedGuid();
        Guid guid2 = generator.GetNextGeneratedGuid();
        Guid guid3 = generator.GetNextGeneratedGuid();

        // Assert
        Assert.AreNotEqual(Guid.Empty, guid1);
        Assert.AreNotEqual(Guid.Empty, guid2);
        Assert.AreNotEqual(Guid.Empty, guid3);
        Assert.AreNotEqual(guid1, guid2);
        Assert.AreNotEqual(guid2, guid3);
        Assert.AreNotEqual(guid1, guid3);
    }

    [TestMethod]
    public void TestIDGenerator_MultipleInstances_IndependentCounters()
    {
        // Arrange
        var generator1 = new IDGenerator();
        var generator2 = new IDGenerator();

        // Act
        long id1_1 = generator1.GetNextGeneratedId();
        long id1_2 = generator1.GetNextGeneratedId();
        long id2_1 = generator2.GetNextGeneratedId();

        // Assert: 각 인스턴스는 독립적인 카운터를 가짐
        Assert.AreEqual(1, id1_1);
        Assert.AreEqual(2, id1_2);
        Assert.AreEqual(1, id2_1);
    }
}
