using System;
using System.Buffers.Binary;

namespace Evertorch.Protocol
{
/// <summary>
///     The one place that says how each message travels. Both transport adapters send by it, and a receiver rejects a
///     message that arrives on any other channel.
/// </summary>
public static class MessageRouting
{
    public const int ChannelCount = 3;

    public static bool TryGetRoute(MessageOpcode opcode, out ProtocolChannel channel, out MessageDelivery delivery)
    {
        channel = ProtocolChannel.Control;
        delivery = MessageDelivery.ReliableOrdered;
        switch (opcode)
        {
            case MessageOpcode.ClientHello:
            case MessageOpcode.EnterWorldRequest:
            case MessageOpcode.TargetEntity:
            case MessageOpcode.AttackEntity:
            case MessageOpcode.CancelAction:
            case MessageOpcode.Respawn:
            case MessageOpcode.CreateCharacter:
            case MessageOpcode.ServerHello:
            case MessageOpcode.WorldEntered:
            case MessageOpcode.EntitySpawn:
            case MessageOpcode.EntityDespawn:
            case MessageOpcode.TargetChanged:
            case MessageOpcode.AttackStarted:
            case MessageOpcode.Damage:
            case MessageOpcode.EntityDied:
            case MessageOpcode.ItemDropped:
            case MessageOpcode.CharacterHealth:
            case MessageOpcode.EntityRevived:
            case MessageOpcode.DisconnectNotice:
            case MessageOpcode.CharacterList:
            case MessageOpcode.CreateCharacterResult:
                return true;
            case MessageOpcode.MoveInput:
            case MessageOpcode.StopMovement:
                channel = ProtocolChannel.Input;
                delivery = MessageDelivery.UnreliableSequenced;
                return true;
            case MessageOpcode.EntitySnapshot:
                channel = ProtocolChannel.State;
                delivery = MessageDelivery.UnreliableSequenced;
                return true;
            default:
                return false;
        }
    }

    public static bool IsClientToServer(MessageOpcode opcode)
    {
        return opcode != MessageOpcode.None && (ushort)opcode < ProtocolConstants.FirstServerOpcode;
    }

    /// <summary>
    ///     Reads the opcode at the start of a payload. False when the payload is too short or the opcode is unknown.
    /// </summary>
    public static bool TryReadOpcode(ReadOnlySpan<byte> payload, out MessageOpcode opcode)
    {
        opcode = MessageOpcode.None;
        if (payload.Length < sizeof(ushort))
        {
            return false;
        }

        var candidate = (MessageOpcode)BinaryPrimitives.ReadUInt16LittleEndian(payload);
        if (!TryGetRoute(candidate, out ProtocolChannel _, out MessageDelivery _))
        {
            return false;
        }

        opcode = candidate;
        return true;
    }
}
}
