using MediaBrowser.Model.Serialization;
using MediaBrowser.Controller.LiveTv;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.MythTv.Model;

namespace Jellyfin.MythTv.Responses
{
    public class ChannelResponse
    {
        private static readonly CultureInfo _usCulture = new CultureInfo("en-US");
        
        public static IEnumerable<string> GetVideoSourceList(Stream stream, IJsonSerializer json, ILogger logger)
        {
            var root = json.DeserializeFromStream<RootVideoSourceObject>(stream);
            return root.VideoSourceList.VideoSources.Select(i => i.Id);
        }

        public static IEnumerable<ChannelInfo> GetChannels(Stream stream, IJsonSerializer json, ILogger logger,
                                                           bool loadChannelIcons)
        {
            var root = json.DeserializeFromStream<RootChannelInfoListObject>(stream).ChannelInfoList.ChannelInfos;
            logger.LogDebug(string.Format("[MythTV] GetChannels Response: {0}",
                                       json.SerializeToString(root)));
            return root.Select(x => GetChannel(x, loadChannelIcons));
        }

        private static ChannelInfo GetChannel(Channel channel, bool loadChannelIcons)
        {
            bool hasIconUrl = string.IsNullOrWhiteSpace(channel.IconURL);

            return new ChannelInfo
            {
                Name = channel.ChannelName,
                Number = channel.ChanNum,
                Id = channel.ChanId,
                ImageUrl = hasIconUrl
                            ? string.Format("{0}/Guide/GetChannelIcon?ChanId={1}", Plugin.Instance.Configuration.WebServiceUrl, channel.ChanId)
                            : "https://www.mythtv.org/img/mythtv.png"
            };
        }

        private class RootVideoSourceObject
        {
            public VideoSourceList VideoSourceList { get; set; }
        }

        private class RootChannelInfoListObject
        {
            public ChannelInfoList ChannelInfoList { get; set; }
        }
    }
}
