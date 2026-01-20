using LibCommons;

namespace LibCommonTest;

[TestClass]
public sealed class IBuffersInterfaceTest
{
    /// <summary>
    /// IBuffers 인터페이스 구현체들의 공통 동작을 검증하는 테스트
    /// </summary>
    
    [TestMethod]
    [DataRow(typeof(BaseCircularBuffers))]
    [DataRow(typeof(ArrayPoolCircularBuffers))]
    [DataRow(typeof(BaseQueueBuffers))]
    public void TestIBuffers_Write_AllImplementations(Type bufferType)
    {
        // Arrange
        var buffer = CreateBuffer(bufferType, 100);
        try
        {
            byte[] data = [1, 2, 3, 4, 5];

            // Act
            int written = buffer.Write(data, 0, data.Length);

            // Assert
            Assert.AreEqual(5, written);
            Assert.AreEqual(5, buffer.CanReadSize);
        }
        finally
        {
            DisposeBuffer(buffer);
        }
    }

    [TestMethod]
    [DataRow(typeof(BaseCircularBuffers))]
    [DataRow(typeof(ArrayPoolCircularBuffers))]
    [DataRow(typeof(BaseQueueBuffers))]
    public void TestIBuffers_Drain_AllImplementations(Type bufferType)
    {
        // Arrange
        var buffer = CreateBuffer(bufferType, 100);
        try
        {
            byte[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
            buffer.Write(data, 0, data.Length);

            // Act
            int drained = buffer.Drain(5);

            // Assert
            Assert.AreEqual(5, drained);
            Assert.AreEqual(5, buffer.CanReadSize);
        }
        finally
        {
            DisposeBuffer(buffer);
        }
    }

    [TestMethod]
    [DataRow(typeof(BaseCircularBuffers))]
    [DataRow(typeof(ArrayPoolCircularBuffers))]
    public void TestIBuffers_TryGetBasePackets_CircularBuffers(Type bufferType)
    {
        // Arrange
        var buffer = CreateBuffer(bufferType, 100);
        try
        {
            // 패킷 생성: [2바이트 크기=10][8바이트 데이터]
            byte[] packet = new byte[10];
            BitConverter.GetBytes((ushort)10).CopyTo(packet, 0);
            for (int i = 2; i < 10; i++) packet[i] = (byte)i;
            
            buffer.Write(packet, 0, packet.Length);

            // Act
            bool result = buffer.TryGetBasePackets(out var packets);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(1, packets.Count);
        }
        finally
        {
            DisposeBuffer(buffer);
        }
    }

    private static IBuffers CreateBuffer(Type type, int capacity)
    {
        if (type == typeof(BaseCircularBuffers))
            return new BaseCircularBuffers(capacity);
        if (type == typeof(ArrayPoolCircularBuffers))
            return new ArrayPoolCircularBuffers(capacity);
        if (type == typeof(BaseQueueBuffers))
            return new BaseQueueBuffers(capacity);
        
        throw new ArgumentException($"Unknown buffer type: {type.Name}");
    }

    private static void DisposeBuffer(IBuffers buffer)
    {
        if (buffer is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
