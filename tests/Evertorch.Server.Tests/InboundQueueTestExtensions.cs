using System;
using Evertorch.Protocol;

namespace Evertorch.Server.Tests
{
internal static class InboundQueueTestExtensions
{
    /// <summary>
    ///     Hands the queue a payload as a well-behaved transport would: with the delivery its opcode's route names.
    ///     Tests of the delivery check call the queue's own overload.
    /// </summary>
    public static void OnPayload(
        this InboundQueue queue,
        ConnectionId connection,
        ProtocolChannel channel,
        ReadOnlySpan<byte> payload)
    {
        MessageDelivery delivery = channel == ProtocolChannel.Control
            ? MessageDelivery.ReliableOrdered
            : MessageDelivery.UnreliableSequenced;
        if (MessageRouting.TryReadOpcode(payload, out MessageOpcode opcode))
        {
            MessageRouting.TryGetRoute(opcode, out ProtocolChannel _, out delivery);
        }

        queue.OnPayload(connection, channel, delivery, payload);
    }
}
}
