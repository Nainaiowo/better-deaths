using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using System.Collections.Generic;
using System.IO;

namespace BetterDeaths;

public sealed class DeathChatLinkPayload : Payload
{
    private const byte CustomPayloadId = 190;

    public long DeathSeenAtTicks { get; private set; }

    public uint MemberKeyHash { get; private set; }

    public override PayloadType Type => PayloadType.Unknown;

    private DeathChatLinkPayload()
    {
    }

    public DeathChatLinkPayload(long deathSeenAtTicks, uint memberKeyHash)
    {
        DeathSeenAtTicks = deathSeenAtTicks;
        MemberKeyHash = memberKeyHash;
    }

    public static DeathChatLinkPayload? Decode(RawPayload rawPayload)
    {
        var data = rawPayload.Data;
        if (data.Length < 4 || data[1] != CustomPayloadId)
        {
            return null;
        }

        using var reader = new BinaryReader(new MemoryStream(data));
        reader.BaseStream.Position = 3;
        var payload = new DeathChatLinkPayload();
        payload.DecodeImpl(reader, reader.BaseStream.Length - 1);
        return payload;
    }

    protected override byte[] EncodeImpl()
    {
        var highTicks = MakeInteger((uint)(DeathSeenAtTicks >> 32));
        var lowTicks = MakeInteger((uint)DeathSeenAtTicks);
        var memberHash = MakeInteger(MemberKeyHash);
        var result = new List<byte> { 2, CustomPayloadId, (byte)(highTicks.Length + lowTicks.Length + memberHash.Length + 1) };
        result.AddRange(highTicks);
        result.AddRange(lowTicks);
        result.AddRange(memberHash);
        result.Add(3);
        return result.ToArray();
    }

    protected override void DecodeImpl(BinaryReader reader, long endOfStream)
    {
        var highTicks = GetInteger(reader);
        var lowTicks = GetInteger(reader);
        MemberKeyHash = GetInteger(reader);
        DeathSeenAtTicks = ((long)highTicks << 32) | lowTicks;
    }
}
