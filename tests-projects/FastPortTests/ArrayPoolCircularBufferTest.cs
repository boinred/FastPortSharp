using LibCommons;

namespace FastPortTests;

[TestClass]
public sealed class ArrayPoolCircularBufferTest
{
    [TestMethod]
    public void TestArrayPoolCircular_Write_Success()
    {
        // Arrange
        using var buffer = new ArrayPoolCircularBuffers(100);
        byte[] data = [1, 2, 3, 4, 5];

        // Act
        int written = buffer.Write(data, 0, data.Length);

        // Assert
        Assert.AreEqual(5, written);
        Assert.AreEqual(5, buffer.CanReadSize);
    }

    [TestMethod]
    public void TestArrayPoolCircular_WriteSpan_Success()
    {
        // Arrange
        using var buffer = new ArrayPoolCircularBuffers(100);
        ReadOnlySpan<byte> data = stackalloc byte[] { 1, 2, 3, 4, 5 };

        // Act
        int written = buffer.Write(data);

        // Assert
        Assert.AreEqual(5, written);
        Assert.AreEqual(5, buffer.CanReadSize);
    }

    [TestMethod]
    public void TestArrayPoolCircular_WriteAndPeek_Success()
    {
        // Arrange
        using var buffer = new ArrayPoolCircularBuffers(100);
        byte[] data = [10, 20, 30, 40, 50];
        byte[] readBuffer = new byte[10];

        // Act
        buffer.Write(data, 0, data.Length);
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
    public void TestArrayPoolCircular_PeekSpan_Success()
    {
        // Arrange
        using var buffer = new ArrayPoolCircularBuffers(100);
        byte[] data = [0xAA, 0xBB, 0xCC];
        buffer.Write(data, 0, data.Length);

        // Act
        Span<byte> readBuffer = stackalloc byte[10];
        int read = buffer.Peek(readBuffer);

        // Assert
        Assert.AreEqual(3, read);
        Assert.AreEqual(0xAA, readBuffer[0]);
        Assert.AreEqual(0xBB, readBuffer[1]);
        Assert.AreEqual(0xCC, readBuffer[2]);
    }

    [TestMethod]
    public void TestArrayPoolCircular_Drain_Success()
    {
        // Arrange
        using var buffer = new ArrayPoolCircularBuffers(100);
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        buffer.Write(data, 0, data.Length);

        // Act
        int drained = buffer.Drain(5);

        // Assert
        Assert.AreEqual(5, drained);
        Assert.AreEqual(5, buffer.CanReadSize);
    }

    [TestMethod]
    public void TestArrayPoolCircular_BufferExpansion_Success()
    {
        // Arrange: start with a small buffer.
        using var buffer = new ArrayPoolCircularBuffers(10);
        byte[] data = new byte[50];
        Random.Shared.NextBytes(data);

        // Act: write more data than the initial capacity so expansion occurs.
        int written = buffer.Write(data, 0, data.Length);

        // Assert
        Assert.AreEqual(50, written);
        Assert.AreEqual(50, buffer.CanReadSize);

        // Verify that the copied data survived expansion.
        byte[] readBuffer = new byte[50];
        buffer.Peek(ref readBuffer);
        CollectionAssert.AreEqual(data, readBuffer);
    }

    [TestMethod]
    public void TestArrayPoolCircular_CircularWrite_Success()
    {
        // Arrange
        using var buffer = new ArrayPoolCircularBuffers(20);
        byte[] data1 = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        byte[] data2 = [11, 12, 13, 14, 15];

        // Act: write, drain part of the buffer, then write again to wrap.
        buffer.Write(data1, 0, data1.Length);
        buffer.Drain(8); // Remove 8 bytes and move Head.
        buffer.Write(data2, 0, data2.Length); // Trigger circular write.

        // Assert
        Assert.AreEqual(7, buffer.CanReadSize); // 10 - 8 + 5 = 7

        byte[] readBuffer = new byte[10];
        int read = buffer.Peek(ref readBuffer);
        Assert.AreEqual(7, read);
        Assert.AreEqual(9, readBuffer[0]);  // Last 2 bytes from data1.
        Assert.AreEqual(10, readBuffer[1]);
        Assert.AreEqual(11, readBuffer[2]); // 5 bytes from data2.
        Assert.AreEqual(12, readBuffer[3]);
        Assert.AreEqual(13, readBuffer[4]);
        Assert.AreEqual(14, readBuffer[5]);
        Assert.AreEqual(15, readBuffer[6]);
    }

    [TestMethod]
    public void TestArrayPoolCircular_GetPacketBuffers_Success()
    {
        // Arrange
        using var buffer = new ArrayPoolCircularBuffers(100);
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        buffer.Write(data, 0, data.Length);

        // Act
        int result = buffer.GetPacketBuffers(out byte[]? packetBuffer, 5);

        // Assert
        Assert.AreEqual(5, result);
        Assert.IsNotNull(packetBuffer);
        Assert.AreEqual(1, packetBuffer[0]);
        Assert.AreEqual(2, packetBuffer[1]);
        Assert.AreEqual(3, packetBuffer[2]);
        Assert.AreEqual(4, packetBuffer[3]);
        Assert.AreEqual(5, packetBuffer[4]);
        Assert.AreEqual(5, buffer.CanReadSize); // Drain was called.

        // Return rented buffer to avoid retaining pooled memory.
        ArrayPoolCircularBuffers.ReturnBuffer(packetBuffer);
    }

    [TestMethod]
    public void TestArrayPoolCircular_GetPacketSizeInBuffers_Success()
    {
        // Arrange
        using var buffer = new ArrayPoolCircularBuffers(100);
        // Packet format: [2 byte size=100][data...].
        byte[] header = BitConverter.GetBytes((ushort)100);
        buffer.Write(header, 0, header.Length);

        // Act
        int packetSize = buffer.GetPacketSizeInBuffers();

        // Assert
        Assert.AreEqual(100, packetSize);
    }

    [TestMethod]
    public void TestArrayPoolCircular_TryGetBasePackets_SinglePacket()
    {
        // Arrange
        using var buffer = new ArrayPoolCircularBuffers(100);
        
        // Packet format: [2 byte size=10][8 byte data].
        byte[] packet = new byte[10];
        BitConverter.GetBytes((ushort)10).CopyTo(packet, 0);
        for (int i = 2; i < 10; i++) packet[i] = (byte)i;
        
        buffer.Write(packet, 0, packet.Length);

        // Act
        bool result = buffer.TryGetBasePackets(out var packets);

        // Assert
        Assert.IsTrue(result);
        Assert.AreEqual(1, packets.Count);
        Assert.AreEqual(10, packets[0].PacketSize);
        Assert.AreEqual(8, packets[0].DataSize);
    }

    [TestMethod]
    public void TestArrayPoolCircular_TryGetBasePackets_MultiplePackets()
    {
        // Arrange
        using var buffer = new ArrayPoolCircularBuffers(200);
        
        // Create 3 packets.
        for (int p = 0; p < 3; p++)
        {
            byte[] packet = new byte[10];
            BitConverter.GetBytes((ushort)10).CopyTo(packet, 0);
            for (int i = 2; i < 10; i++) packet[i] = (byte)(p * 10 + i);
            buffer.Write(packet, 0, packet.Length);
        }

        // Act
        bool result = buffer.TryGetBasePackets(out var packets);

        // Assert
        Assert.IsTrue(result);
        Assert.AreEqual(3, packets.Count);
        foreach (var packet in packets)
        {
            Assert.AreEqual(10, packet.PacketSize);
        }
    }

    [TestMethod]
    public void TestArrayPoolCircular_TryGetBasePackets_IncompletePacket()
    {
        // Arrange
        using var buffer = new ArrayPoolCircularBuffers(100);
        
        // Incomplete packet: declared size is 100, but only 10 bytes are present.
        byte[] header = BitConverter.GetBytes((ushort)100);
        buffer.Write(header, 0, header.Length);
        buffer.Write(new byte[8], 0, 8); // 10 bytes total, 100 bytes required.

        // Act
        bool result = buffer.TryGetBasePackets(out var packets);

        // Assert
        Assert.IsFalse(result);
        Assert.AreEqual(0, packets.Count);
    }

    [TestMethod]
    public void TestArrayPoolCircular_WriteEmpty_ReturnsZero()
    {
        // Arrange
        using var buffer = new ArrayPoolCircularBuffers(100);

        // Act
        int written1 = buffer.Write([], 0, 0);
        int written2 = buffer.Write(ReadOnlySpan<byte>.Empty);

        // Assert
        Assert.AreEqual(0, written1);
        Assert.AreEqual(0, written2);
        Assert.AreEqual(0, buffer.CanReadSize);
    }

    [TestMethod]
    public void TestArrayPoolCircular_CanWriteSize_Correct()
    {
        // Arrange
        using var buffer = new ArrayPoolCircularBuffers(100);
        byte[] data = new byte[30];

        // Act
        buffer.Write(data, 0, data.Length);

        // Assert
        Assert.AreEqual(70, buffer.CanWriteSize);
    }

    [TestMethod]
    public void TestArrayPoolCircular_Dispose_NoException()
    {
        // Arrange
        var buffer = new ArrayPoolCircularBuffers(100);
        buffer.Write([1, 2, 3], 0, 3);

        // Act & Assert: Dispose should not throw
        buffer.Dispose();
        buffer.Dispose(); // Double dispose should be safe
    }

    [TestMethod]
    public void TestArrayPoolCircular_Constructor_ThrowsOnInvalidCapacity()
    {
        // Act & Assert
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new ArrayPoolCircularBuffers(0));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new ArrayPoolCircularBuffers(-1));
    }
}
