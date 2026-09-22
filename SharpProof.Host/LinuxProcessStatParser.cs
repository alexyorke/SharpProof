using System.Globalization;

namespace SharpProof.Host;

internal readonly record struct LinuxProcessStat(
    int ParentProcessId,
    ulong? StartTime,
    int ProcessGroupId = 0,
    int SessionId = 0);

internal static class LinuxProcessStatParser
{
    internal static bool TryParse(
        string stat,
        out LinuxProcessStat processStat)
    {
        processStat = default;
        var closeName = stat.LastIndexOf(')');
        if (closeName < 0)
        {
            return false;
        }

        var fields = stat.AsSpan(closeName + 2)
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2 ||
            !int.TryParse(
                fields[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parentId))
        {
            return false;
        }

        ulong? startTime = fields.Length > 19 &&
            ulong.TryParse(
                fields[19],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsedStartTime)
            ? parsedStartTime
            : null;
        var groupId = fields.Length > 2 && int.TryParse(fields[2],
            NumberStyles.None, CultureInfo.InvariantCulture, out var parsedGroupId)
            ? parsedGroupId : 0;
        var sessionId = fields.Length > 3 && int.TryParse(fields[3],
            NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSessionId)
            ? parsedSessionId : 0;
        processStat = new LinuxProcessStat(parentId, startTime, groupId, sessionId);
        return true;
    }
}
