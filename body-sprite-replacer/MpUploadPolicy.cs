using System.Collections.Generic;

// Server-side rule for an upload while the lock is on. A client with no
// stored skin gets one upload. The server also grants one extra upload after
// that. Later uploads are rejected until the lock changes.
internal sealed class MpUploadPolicy
{
    internal enum Decision
    {
        Reject,
        Accept,
        AcceptOnce
    }

    private readonly HashSet<uint> _allowance = new HashSet<uint>();

    internal Decision Check(uint clientId, bool locked, bool hasSkin)
    {
        if (!locked) return Decision.Accept;

        if (!hasSkin)
        {
            _allowance.Add(clientId);
            return Decision.Accept;
        }

        if (_allowance.Remove(clientId))
            return Decision.AcceptOnce;

        return Decision.Reject;
    }

    internal bool HasAllowance(uint clientId)
    {
        return _allowance.Contains(clientId);
    }

    internal void Clear()
    {
        _allowance.Clear();
    }
}
