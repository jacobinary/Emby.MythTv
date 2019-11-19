using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.MythTv.Model;
using Microsoft.Extensions.Logging;

namespace Jellyfin.MythTv.Protocol
{
    class ProtoBase : IDisposable
    {
        protected static readonly string DELIMITER = "[]:[]";

        public enum AnnounceModeType
        {
            FileTransfer,
            Playback,
            MediaServer,
            Monitor,
            SlaveBackend
        }

        public enum EventModeType {
            None = 0,
            All = 1,
            ExcludeSystem = 2,
            SystemOnly = 3
        }

        public string Server { get; protected set; }
        public int Port { get; protected set; }
        public AnnounceModeType AnnounceMode { get; protected set; } = AnnounceModeType.Monitor;
        public EventModeType EventMode { get; protected set; } = EventModeType.None;

        public ILogger Logger { get; set; }
        public bool IsOpen { get; protected set; } = false;
        public uint ProtoVersion { get; private set; }
        
        private TcpClient socket;

        private Dictionary<uint, string> protomap = new Dictionary<uint, string>()
        {
            {91, "BuzzOff"},
            {90, "BuzzCut"},
            {89, "BuzzKill"},
            {88, "XmasGift"}
        };

        public ProtoBase(string server, int port, AnnounceModeType announceMode, EventModeType eventMode, ILogger logger) {
            Server = server;
            Port = port;
            AnnounceMode = announceMode;
            EventMode = eventMode;
            Logger = logger;
        }

        ~ProtoBase()
        {
            Dispose(false);
        }

        protected virtual async Task<bool> OpenAsync()
        {
            if (!await OpenConnectionAsync().ConfigureAwait(false))
            {
                return false;
            }

            if (ProtoVersion >= 75 && await Announce75())
            {
                Logger.LogInformation($"[MythTV] MythProtocol connection opened, protocol version {ProtoVersion}");

                return true;
            }

            await CloseAsync().ConfigureAwait(false);
            return false;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && socket != null)
            {
                _ = CloseAsync().ConfigureAwait(false);

                Logger.LogInformation($"[MythTV] MythProtocol connection closed, protocol version {ProtoVersion}");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private string FormatMessage(string message)
        {
            var messageFormat = "{0,-8:G}{1}";
            return string.Format(messageFormat, message.Length, message);
        }

        protected async Task WriteAsync(string payload) {
            var stream = socket.GetStream();
            var sendBytes = Encoding.ASCII.GetBytes(payload);

            await stream.WriteAsync(sendBytes, 0, sendBytes.Length).ConfigureAwait(false);
        }

        protected async Task<string> ReadAsync(int length) {
            var stream = socket.GetStream();
            var result = string.Empty;
            var readBytes = new byte[length];
            var totalBytesRead = 0;

            while (totalBytesRead < length)
            {
                var bytesRead = await stream.ReadAsync(readBytes, 0, (length - totalBytesRead)).ConfigureAwait(false);
                totalBytesRead += bytesRead;

                result += Encoding.ASCII.GetString(readBytes, 0, bytesRead);
            }

            return result;
        }
        
        protected async Task<List<string>> ListenAsync() {
            try {
                var length = int.Parse(await ReadAsync(8).ConfigureAwait(false));
                var response = await ReadAsync(length).ConfigureAwait(false);

                Logger.LogDebug($"[MythTV] Received: {response}");

                return response.Split(new[] { DELIMITER }, StringSplitOptions.None).ToList();
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[MythTV] Listening exception: {ex.Message}");
                throw new Exception(ex.Message, ex);
            }
        }

        protected async Task<List<string>> SendCommandAsync(string command)
        {
            return await SendCommandAsync(command, true).ConfigureAwait(false);
        }
        protected async Task<List<string>> SendCommandAsync(string command, bool waitForResponse)
        {
            command = FormatMessage(command);

            Logger.LogDebug($"[MythTV] Sending: {command}");

            try
            {
                await WriteAsync(command).ConfigureAwait(false);

                if (!waitForResponse) {
                    return null;
                }

                return await ListenAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[MythTV] Sending exception: {ex.Message}");
                throw new Exception(ex.Message, ex);
            }
        }

        private async Task<bool> OpenConnectionAsync()
        {
            Logger.LogInformation("[MythTV] Initiating MythProtocol connection");

            socket = new TcpClient();
            await socket.ConnectAsync(Server, Port).ConfigureAwait(false);
            uint max_version = protomap.Keys.Max();
            var response = await SendCommandAsync($"MYTH_PROTO_VERSION {max_version} {protomap[max_version]}").ConfigureAwait(false);
            IsOpen = response[0] == "ACCEPT";
            if(IsOpen)
            {
                ProtoVersion = max_version;
                return true;
            }

            // Rejected, close socket
            await CloseAsync().ConfigureAwait(false);

            Logger.LogInformation("[MythTV] No MythProtocol connection");

            // Got rejected, so see if we speak the required version
            uint server_version = Convert.ToUInt32(response[1]);
            if (!protomap.ContainsKey(server_version))
                throw new Exception($"Unknown version {server_version}");

            socket = new TcpClient();
            await socket.ConnectAsync(Server, Port).ConfigureAwait(false);
            var status = (await SendCommandAsync($"MYTH_PROTO_VERSION {server_version} {protomap[server_version]}").ConfigureAwait(false))[0];
            IsOpen = status == "ACCEPT";
            if(IsOpen)
            {
                ProtoVersion = server_version;
            }
            
            return IsOpen;
        }

        protected virtual async Task CloseAsync()
        {

            if (socket.Connected && IsOpen)
            {
                await SendCommandAsync("DONE", false).ConfigureAwait(false);

                socket.Close();
                socket = null;

                Logger.LogInformation($"[MythTV] MythProtocol connection closed, protocol version {ProtoVersion}");
            }

            IsOpen = false;
        }

        public virtual async Task<bool> Announce75()
        {
            var status = (await SendCommandAsync($"ANN {AnnounceMode} jellyfin {(int)EventMode}").ConfigureAwait(false))[0];
            return status == "OK";
        }

        protected Program RcvProgramInfo86(List<string> fields)
        {
            var program = new Program {
                Title = fields[0],
                SubTitle = fields[1],
                Description = fields[2],
                Season = int.Parse(fields[3]),
                Episode = int.Parse(fields[4]),
                Category = fields[7],
                FileName = fields[12],
                FileSize = long.Parse(fields[13]),
                StartTime = UnixTimeStampToDateTime(int.Parse(fields[14])),
                EndTime = UnixTimeStampToDateTime(int.Parse(fields[15])),
                HostName = fields[17],
                Recording = new Recording {
                    StorageGroup = fields[41]
                }
            };
            
            Logger.LogDebug($"[MythTV] StorageGroup: {program.HostName}/{program.Recording.StorageGroup}");
            return program;
        }

        protected static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }
    }
}
