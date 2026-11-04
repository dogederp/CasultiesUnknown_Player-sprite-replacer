using System.Collections.Generic;

// Server-side rule for an upload while the lock is on. A client with no
// stored skin gets one upload. The server also grants one extra upload after
// that. Later uploads are rejected until the lock changes. A player that is
// already known stays known when the stored skin is removed.
internal sealed class MpUploadPolicy
{
    internal enum Decision
    {
        Reject,
        Accept,
        AcceptOnce
    }

    private readonly HashSet<uint> _allowance = new HashSet<uint>();
    private readonly HashSet<uint> _seen = new HashSet<uint>();

    internal Decision Check(uint clientId, bool locked, bool hasSkin)
    {
        if (!locked) return Decision.Accept;

        if (!_seen.Contains(clientId))
        {
            _seen.Add(clientId);
            if (!hasSkin)
            {
                _allowance.Add(clientId);
                return Decision.Accept;
            }
        }

        if (_allowance.Remove(clientId))
            return Decision.AcceptOnce;

        return Decision.Reject;
    }

    internal bool HasAllowance(uint clientId)
    {
        return _allowance.Contains(clientId);
    }

    internal void MarkSeen(uint clientId)
    {
        if (clientId != 0) _seen.Add(clientId);
    }

    internal void Clear()
    {
        _allowance.Clear();
        _seen.Clear();
    }
}
