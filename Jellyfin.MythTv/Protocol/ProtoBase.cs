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

        public enum AnnounceMode
        {
            FileTransfer,
            Playback,
            MediaServer,
            Monitor,
            SlaveBackend
        }

        public enum ERROR_t
        {
            ERROR_NO_ERROR = 0,
            ERROR_SERVER_UNREACHABLE = 1,
            ERROR_SOCKET_ERROR = 2,
            ERROR_UNKNOWN_VERSION = 3,
        }

        public virtual bool IsOpen { get; private set; }
        public uint ProtoVersion { get; private set; }
        public string Server { get; private set; }
        public int Port { get; private set; }
        public bool HasHanging { get; private set; }
        public AnnounceMode Mode { get; private set; }

        protected ILogger _logger;
        
        private TcpClient socket;

        private Dictionary<uint, string> protomap = new Dictionary<uint, string>()
        {
            {91, "BuzzOff"},
            {90, "BuzzCut"},
            {89, "BuzzKill"},
            {88, "XmasGift"}
        };

        public ProtoBase(string server, int port, AnnounceMode mode, ILogger logger)
        {
            Server = server;
            Port = port;
            Mode = mode;
            _logger = logger;
            IsOpen = false;
        }

        ~ProtoBase()
        {
            Dispose(false);
        }

        public virtual async Task<bool> Open()
        {
            bool ok = false;
            if (!await OpenConnection())
            {
                return false;
            }

            if (ProtoVersion >= 75)
                ok = await Announce75();

            if (ok)
                return true;

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

        private async Task<List<string>> sendToServerAsync(string toSend)
        {

            string result;

            _logger.LogDebug($"[MythTV] Sending: {toSend}");

            try
            {
                var stream = socket.GetStream();

                var sendBytes = Encoding.ASCII.GetBytes(toSend);

                await stream.WriteAsync(sendBytes, 0, sendBytes.Length);

                var buffer = new byte[8];
                var bytesRead = await stream.ReadAsync(buffer, 0, 8);

                if (bytesRead == 0)
                {
                    return new[] { "" }.ToList();
                }

                var length = Encoding.ASCII.GetString(buffer, 0, 8);

                var bytesAvailable = int.Parse(length);
                var readBytes = new byte[bytesAvailable];

                var totalBytesRead = 0;
                result = string.Empty;

                while (totalBytesRead < bytesAvailable)
                {
                    bytesRead = await stream.ReadAsync(readBytes, 0, bytesAvailable);
                    totalBytesRead += bytesRead;

                    result += Encoding.ASCII.GetString(readBytes, 0, bytesRead);
                }

            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[MythTV] Sending exception: {ex.Message}");
                throw new Exception(ex.Message, ex);
            }

            _logger.LogDebug($"[MythTV] Received: {result}");

            return result.Split(new[] { DELIMITER }, StringSplitOptions.None).ToList();
        }

        protected async Task<List<string>> SendCommand(string command)
        {
            return await sendToServerAsync(FormatMessage(command));
        }

        public async Task<bool> OpenConnection()
        {
            socket = new TcpClient();
            await socket.ConnectAsync(Server, Port);
            uint max_version = protomap.Keys.Max();
            var result = await SendCommand($"MYTH_PROTO_VERSION {max_version} {protomap[max_version]}");
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
            result = await SendCommand($"MYTH_PROTO_VERSION {server_version} {protomap[server_version]}");
            IsOpen = result[0] == "ACCEPT";
            if(IsOpen)
            {
                ProtoVersion = server_version;
            }
            
            return IsOpen;
        }

        public virtual async Task Close()
        {
            if (socket.Connected)
            {
                if (IsOpen)
                {
                    await SendCommand("DONE");
                }
            }
            IsOpen = false;
        }

        public virtual async Task<bool> Announce75()
        {
            var mode = Enum.GetName(typeof(AnnounceMode), Mode);
            var result = await SendCommand($"ANN {mode} jellyfin 0");
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
            
            _logger.LogDebug($"[MythTV] StorageGroup: {program.HostName}/{program.Recording.StorageGroup}");
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
