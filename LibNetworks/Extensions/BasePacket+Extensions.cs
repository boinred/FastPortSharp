using Google.Protobuf;
using LibCommons;
using System.Buffers.Binary;

namespace LibNetworks.Extensions
{
    public static class BasePacketExtensions
    {
        public static bool ParseMessageFromPacket<T>(this BasePacket basePacket, out int packetId, out T? message) where T : IMessage<T>, new()
        {
            packetId = 0;
            message = default;
            if (basePacket.DataSize < 4)
            {
                //m_Logger.LogError($"BaseSession, ReceivedMessage, Data Size is too small. Data Size : {basePacket.DataSize}");

                return false;
            }
            // 목적: protocol id를 임시 배열 없이 packet payload span에서 직접 읽기
            packetId = BinaryPrimitives.ReadInt32LittleEndian(basePacket.Data.Slice(0, 4));

            message = new T();
            // 목적: protobuf payload ToArray 할당 없이 ReadOnlySpan<byte>에서 직접 merge
            message.MergeFrom(basePacket.Data.Slice(4, basePacket.DataSize - 4));

            return true;
        }
    }
}
