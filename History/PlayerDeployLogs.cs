using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RobloxDeployHistory
{
    public class PlayerDeployLogs
    {
        private const string LogPattern = "New (WindowsPlayer) (version-[A-z\\d]+) at (\\d+/\\d+/\\d+ \\d+:\\d+:\\d+ [A,P]M), file version: (\\d+), (\\d+), (\\d+), (\\d+), git hash: ([a-f\\d]+) ...";
        private const StringComparison StringFormat = StringComparison.InvariantCulture;
        private static readonly NumberFormatInfo NumberFormat = NumberFormatInfo.InvariantInfo;

        public Channel Channel { get; private set; }
        public bool HasHistory { get; private set; }

        private string LastDeployHistory = "";
        private static readonly Dictionary<string, PlayerDeployLogs> LogCache = new Dictionary<string, PlayerDeployLogs>();

        public HashSet<DeployLog> CurrentLogs_x86 { get; private set; } = new HashSet<DeployLog>();

        public HashSet<DeployLog> CurrentLogs_x64 { get; private set; } = new HashSet<DeployLog>();

        private PlayerDeployLogs(Channel channel)
        {
            Channel = channel;
            LogCache[channel] = this;
        }

        private static void MakeDistinct(HashSet<DeployLog> targetSet)
        {
            var byChangelist = new Dictionary<int, DeployLog>();
            var rejected = new List<DeployLog>();

            foreach (DeployLog log in targetSet)
            {
                int changelist = log.Changelist;

                if (byChangelist.ContainsKey(changelist))
                {
                    DeployLog oldLog = byChangelist[changelist];

                    if (oldLog.TimeStamp.CompareTo(log.TimeStamp) < 0)
                    {
                        byChangelist[changelist] = log;
                        rejected.Add(oldLog);
                    }
                }
                else
                {
                    byChangelist.Add(changelist, log);
                }
            }

            rejected.ForEach(log => targetSet.Remove(log));
        }

        private async Task<DeployLog> GetLiveLog(Channel channel, string binaryType)
        {
            var info = await ClientVersionInfo.Get(channel, binaryType);

            var versionData = info.Version
                .Split('.')
                .Select(int.Parse)
                .ToArray();

            return new DeployLog()
            {
                Is64Bit = binaryType.EndsWith("64", StringFormat),
                VersionGuid = info.VersionGuid,
                TimeStamp = DateTime.Now,
                Channel = channel,

                MajorRev = versionData[0],
                Version = versionData[1],
                Patch = versionData[2],
                Changelist = versionData[3],
            };
        }

        private async Task UpdateLogs(Channel channel, string deployHistory)
        {
            CurrentLogs_x86.Clear();
            CurrentLogs_x64.Clear();

            if (deployHistory.Contains("version-hidden") || (channel.Name == "live"))
            {
                if (Environment.Is64BitOperatingSystem)
                {
                    var liveInfo_x64 = await GetLiveLog(channel, "WindowsPlayer");
                    CurrentLogs_x64.Add(liveInfo_x64);
                }
                else
                {
                    var liveInfo_x86 = await GetLiveLog(channel, "WindowsPlayer");
                    CurrentLogs_x86.Add(liveInfo_x86);
                }

                HasHistory = false;
            }
            else
            {
                var matches = Regex.Matches(deployHistory, LogPattern);

                foreach (Match match in matches)
                {
                    string[] data = match.Groups.Cast<Group>()
                        .Select(group => group.Value)
                        .Where(value => value.Length != 0)
                        .ToArray();

                    string buildType = data[1];
                    string versionGuid = data[2];

                    if (versionGuid.ToLowerInvariant() == "version-hidden")
                        continue;

                    var deployLog = new DeployLog()
                    {
                        VersionGuid = versionGuid,
                        TimeStamp = DateTime.Parse(data[3], DateTimeFormatInfo.InvariantInfo),

                        MajorRev = int.Parse(data[4], NumberFormat),
                        Version = int.Parse(data[5], NumberFormat),
                        Patch = int.Parse(data[6], NumberFormat),
                        Changelist = int.Parse(data[7], NumberFormat),

                        Is64Bit = buildType.EndsWith("64", StringFormat),
                        Channel = channel,
                        GitHash = data[8]
                    };

                    // Block builds before 550 due to RCE.
                    if (deployLog.Version < 550)
                        continue;

                    HashSet<DeployLog> targetList;

                    if (deployLog.Is64Bit)
                        targetList = CurrentLogs_x64;
                    else
                        targetList = CurrentLogs_x86;

                    targetList.Add(deployLog);
                }

                MakeDistinct(CurrentLogs_x64);
                MakeDistinct(CurrentLogs_x86);

                HasHistory = true;
            }
        }

        public static async Task<PlayerDeployLogs> Get(Channel channel)
        {
            PlayerDeployLogs logs;

            if (LogCache.ContainsKey(channel))
                logs = LogCache[channel];
            else
                logs = new PlayerDeployLogs(channel);

            string deployHistory = await HistoryCache.GetDeployHistory(channel);

            if (logs.LastDeployHistory != deployHistory)
            {
                logs.LastDeployHistory = deployHistory;
                await logs.UpdateLogs(channel, deployHistory);
            }

            return logs;
        }
    }
}
