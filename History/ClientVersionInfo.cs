using System.Diagnostics.Contracts;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace RobloxDeployHistory
{
    public class ClientVersionInfo
    {
        private class ClientVersionResponse
        {
            public string Version = "";
            public string ClientVersionUpload = "";
        }

        public Channel Channel { get; private set; }
        public string Version { get; private set; }
        public string VersionGuid { get; private set; }

        public ClientVersionInfo(Channel channel, string version, string versionGuid)
        {
            Channel = channel;
            Version = version;
            VersionGuid = versionGuid;
        }

        public ClientVersionInfo(DeployLog log)
        {
            Contract.Requires(log != null);
            VersionGuid = log.VersionGuid;
            Version = log.VersionId;
            Channel = log.Channel;
        }

        private ClientVersionInfo(Channel channel, ClientVersionResponse response)
        {
            Channel = channel;
            Version = response.Version;
            VersionGuid = response.ClientVersionUpload;
        }

        public static async Task<ClientVersionInfo> Get(Channel channel, string binaryType)
        {
            using (var http = new WebClient())
            {
                string Json;

                try
                {
                    Json = await http.DownloadStringTaskAsync($"https://clientsettings.roblox.com/v2/client-version/{binaryType}/channel/{channel}");
                    var response = JsonConvert.DeserializeObject<ClientVersionResponse>(Json);

                    return new ClientVersionInfo(channel, response);
                }
                catch (WebException e)
                {
                    MessageBox.Show($"An error has occured when trying to fetch the selected channel: {channel}" + "\n\n" + e.Message + "\n\nReturning the channel LIVE by default instead, if same error happens more than once, try to change the channel",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error,
                        MessageBoxDefaultButton.Button1);

                    Json = await http.DownloadStringTaskAsync($"https://clientsettings.roblox.com/v2/client-version/{binaryType}/channel/LIVE");
                    var response = JsonConvert.DeserializeObject<ClientVersionResponse>(Json);

                    return new ClientVersionInfo("LIVE", response);
                }
            }
        }
    }
}
