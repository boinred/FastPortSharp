using LibCommons;

namespace LibCommonTest;

[TestClass]
public sealed class BasePacketTest
{
    [TestMethod]
    public void TestBasePacket_Create_ValidPacket()
    {
        // Arrange: [2바이트 헤더(패킷크기=10)] + [8바이트 데이터]
        byte[] rawBuffer = new byte[10];
        BitConverter.GetBytes((ushort)10).CopyTo(rawBuffer, 0); // 헤더: 패킷 크기
        for (int i = 2; i < 10; i++)
        {
            rawBuffer[i] = (byte)(i - 2); // 데이터: 0, 1, 2, 3, 4, 5, 6, 7
        }

        // Act
        var packet = new BasePacket(10, rawBuffer);

        // Assert
        Assert.AreEqual(10, packet.PacketSize);
        Assert.AreEqual(8, packet.DataSize);
        Assert.AreEqual(8, packet.Data.Length);
    }

    [TestMethod]
    public void TestBasePacket_Data_CorrectContent()
    {
        // Arrange
        byte[] rawBuffer = new byte[6]; // 2바이트 헤더 + 4바이트 데이터
        BitConverter.GetBytes((ushort)6).CopyTo(rawBuffer, 0);
        rawBuffer[2] = 0xAA;
        rawBuffer[3] = 0xBB;
        rawBuffer[4] = 0xCC;
        rawBuffer[5] = 0xDD;

        // Act
        var packet = new BasePacket(6, rawBuffer);

        // Assert
        Assert.AreEqual(0xAA, packet.Data[0]);
        Assert.AreEqual(0xBB, packet.Data[1]);
        Assert.AreEqual(0xCC, packet.Data[2]);
        Assert.AreEqual(0xDD, packet.Data[3]);
    }

    [TestMethod]
    public void TestBasePacket_HeaderSize_IsTwo()
    {
        Assert.AreEqual(2, BasePacket.HeaderSize);
    }

    [TestMethod]
    public void TestBasePacket_MinimalPacket_HeaderOnly()
    {
        // Arrange: 헤더만 있는 최소 패킷
        byte[] rawBuffer = new byte[2];
        BitConverter.GetBytes((ushort)2).CopyTo(rawBuffer, 0);

        // Act
        var packet = new BasePacket(2, rawBuffer);

        // Assert
        Assert.AreEqual(2, packet.PacketSize);
        Assert.AreEqual(0, packet.DataSize);
        Assert.AreEqual(0, packet.Data.Length);
    }
}
