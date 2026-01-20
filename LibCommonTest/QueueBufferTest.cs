using LibCommons;

namespace LibCommonTest;

[TestClass]
public sealed class QueueBufferTest
{
    [TestMethod]
    public void TestQueueBuffer_Write_Success()
    {
        // Arrange
        using var buffer = new BaseQueueBuffers(100);
        byte[] data = [1, 2, 3, 4, 5];

        // Act
        int written = buffer.Write(data, 0, data.Length);

        // Assert
        Assert.AreEqual(5, written);
        Assert.AreEqual(5, buffer.CanReadSize);
    }

    [TestMethod]
    public void TestQueueBuffer_WriteAndPeek_Success()
    {
        // Arrange
        using var buffer = new BaseQueueBuffers(100);
        byte[] data = [10, 20, 30, 40, 50];

        // Act
        buffer.Write(data, 0, data.Length);
        byte[] readBuffer = [];
        int read = buffer.Peek(ref readBuffer);

        // Assert
        Assert.AreEqual(5, read);
        Assert.AreEqual(10, readBuffer[0]);
        Assert.AreEqual(20, readBuffer[1]);
        Assert.AreEqual(30, readBuffer[2]);
        Assert.AreEqual(40, readBuffer[3]);
        Assert.AreEqual(50, readBuffer[4]);
    }

    [TestMethod]
    public void TestQueueBuffer_Drain_Success()
    {
        // Arrange
        using var buffer = new BaseQueueBuffers(100);
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        buffer.Write(data, 0, data.Length);

        // Act
        int drained = buffer.Drain(5);

        // Assert
        Assert.AreEqual(5, drained);
        Assert.AreEqual(5, buffer.CanReadSize);
    }

    [TestMethod]
    public void TestQueueBuffer_Drain_MoreThanAvailable()
    {
        // Arrange
        using var buffer = new BaseQueueBuffers(100);
        byte[] data = [1, 2, 3];
        buffer.Write(data, 0, data.Length);

        // Act
        int drained = buffer.Drain(10);

        // Assert
        Assert.AreEqual(3, drained);
        Assert.AreEqual(0, buffer.CanReadSize);
    }

    [TestMethod]
    public void TestQueueBuffer_CanWriteSize_Correct()
    {
        // Arrange
        using var buffer = new BaseQueueBuffers(100);
        byte[] data = [1, 2, 3, 4, 5];

        // Act
        buffer.Write(data, 0, data.Length);

        // Assert
        Assert.AreEqual(95, buffer.CanWriteSize);
    }

    [TestMethod]
    public void TestQueueBuffer_MultipleWrites_Success()
    {
        // Arrange
        using var buffer = new BaseQueueBuffers(100);
        byte[] data1 = [1, 2, 3];
        byte[] data2 = [4, 5, 6];

        // Act
        buffer.Write(data1, 0, data1.Length);
        buffer.Write(data2, 0, data2.Length);

        // Assert
        Assert.AreEqual(6, buffer.CanReadSize);

        byte[] readBuffer = [];
        buffer.Peek(ref readBuffer);
        Assert.AreEqual(1, readBuffer[0]);
        Assert.AreEqual(2, readBuffer[1]);
        Assert.AreEqual(3, readBuffer[2]);
        Assert.AreEqual(4, readBuffer[3]);
        Assert.AreEqual(5, readBuffer[4]);
        Assert.AreEqual(6, readBuffer[5]);
    }

    [TestMethod]
    public void TestQueueBuffer_Dispose_NoException()
    {
        // Arrange
        var buffer = new BaseQueueBuffers(100);
        buffer.Write([1, 2, 3], 0, 3);

        // Act & Assert: Dispose should not throw
        buffer.Dispose();
        buffer.Dispose(); // Double dispose should be safe
    }
}
