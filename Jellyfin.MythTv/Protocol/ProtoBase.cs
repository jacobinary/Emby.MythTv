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

        public string Server { get; set; }
        public int Port { get; set; }
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

        public ProtoBase() {

        }

        ~ProtoBase()
        {
            Dispose(false);
        }

        public virtual async Task<bool> Open()
        {
            if (!await OpenConnection())
            {
                return false;
            }

            if (ProtoVersion >= 75 && await Announce75())
            {
                Logger.LogInformation($"[MythTV] MythProtocol connection opened, protocol version {ProtoVersion}");

                return true;
            }

            await Close();
            return false;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && socket != null)
            {
                Task.WaitAll(Close());
                socket.Dispose();
                socket = null;
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

            await stream.WriteAsync(sendBytes, 0, sendBytes.Length);
        }

        protected async Task<string> ReadAsync(int length) {
            var stream = socket.GetStream();
            var result = string.Empty;
            var readBytes = new byte[length];
            var totalBytesRead = 0;

            while (totalBytesRead < length)
            {
                var bytesRead = await stream.ReadAsync(readBytes, 0, (length - totalBytesRead));
                totalBytesRead += bytesRead;

                result += Encoding.ASCII.GetString(readBytes, 0, bytesRead);
            }

            return result;
        }
        
        protected async Task<List<string>> ListenAsync() {
            try {
                var length = int.Parse(await ReadAsync(8));
                var response = await ReadAsync(length);

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
            command = FormatMessage(command);

            Logger.LogDebug($"[MythTV] Sending: {command}");

            try
            {
                await WriteAsync(command);

                return await ListenAsync();
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[MythTV] Sending exception: {ex.Message}");
                throw new Exception(ex.Message, ex);
            }
        }

        public async Task<bool> OpenConnection()
        {
            Logger.LogInformation("[MythTV] Initiating MythProtocol connection");

            socket = new TcpClient();
            await socket.ConnectAsync(Server, Port);
            uint max_version = protomap.Keys.Max();
            var result = await SendCommandAsync($"MYTH_PROTO_VERSION {max_version} {protomap[max_version]}");
            IsOpen = result[0] == "ACCEPT";
            if(IsOpen)
            {
                ProtoVersion = max_version;
                return true;
            }

            // Rejected, close socket
            await Close();

            // Got rejected, so see if we speak the required version
            uint server_version = Convert.ToUInt32(result[1]);
            if (!protomap.ContainsKey(server_version))
                throw new Exception($"Unknown version {server_version}");

            socket = new TcpClient();
            await socket.ConnectAsync(Server, Port);
            result = await SendCommandAsync($"MYTH_PROTO_VERSION {server_version} {protomap[server_version]}");
            IsOpen = result[0] == "ACCEPT";
            if(IsOpen)
            {
                ProtoVersion = server_version;
            }
            
            return IsOpen;
        }

        public virtual async Task Close()
        {
            if (socket.Connected && IsOpen)
            {
                await SendCommandAsync("DONE");
            }
            IsOpen = false;
        }

        public virtual async Task<bool> Announce75()
        {
            var announceMode = Enum.GetName(typeof(AnnounceModeType), AnnounceMode);
            var eventMode = EventMode;
            var result = await SendCommandAsync($"ANN {announceMode} jellyfin {eventMode}");
            return result[0] == "OK";
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
