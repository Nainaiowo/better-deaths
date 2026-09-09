namespace BetterDeaths.DamageParsing;

using System;
using System.Collections.Generic;

// Match the game's extracted IPC buffer to its later dispatch, never to a global clock.
internal sealed class DamagePacketTimingHandoff(int capacity = 16384)
{
    private readonly object gate = new();
    private readonly Dictionary<nint, Entry> pending = [];
    private readonly LinkedList<nint> order = [];
    private long published, matched, missing, replaced, mismatched, evicted;

    private sealed record Entry(ulong Header1, ulong Header2, DateTime Time, LinkedListNode<nint> Node);

    public void Publish(nint buffer, ulong header1, ulong header2, DateTime? time)
    {
        if (buffer == 0)
            return;
        lock (gate)
        {
            if (pending.Remove(buffer, out var previous))
            {
                order.Remove(previous.Node);
                replaced++;
            }
            // A rejected new packet must still invalidate an older use of the same buffer.
            if (time is null)
                return;
            while (pending.Count >= Math.Max(1, capacity))
            {
                pending.Remove(order.First!.Value);
                order.RemoveFirst();
                evicted++;
            }
            pending.Add(buffer, new(header1, header2, time.Value, order.AddLast(buffer)));
            published++;
        }
    }

    public DateTime? Take(nint buffer, ulong header1, ulong header2)
    {
        lock (gate)
        {
            if (!pending.Remove(buffer, out var entry))
            {
                missing++;
                return null;
            }
            order.Remove(entry.Node);
            if (entry.Header1 != header1 || entry.Header2 != header2)
            {
                mismatched++;
                return null;
            }
            matched++;
            return entry.Time;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            pending.Clear();
            order.Clear();
        }
    }

    public DamagePacketTimingHealth Snapshot()
    {
        lock (gate)
            return new(published, matched, missing, replaced, mismatched, evicted, pending.Count);
    }
}

internal sealed record DamagePacketTimingHealth(long Published, long Matched, long Missing,
    long Replaced, long HeaderMismatches, long Evicted, int Pending);
