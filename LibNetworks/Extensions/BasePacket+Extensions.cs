using Google.Protobuf;
using LibCommons;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            packetId = BitConverter.ToInt32(basePacket.Data.Slice(0, 4));

            var messageBuffers = basePacket.Data.Slice(4, basePacket.DataSize - 4).ToArray();

            message = new T();
            message.MergeFrom(messageBuffers);

            return true;
        }
    }
}
