using Jellyfin.MythTv.Helpers;
using Jellyfin.MythTv.Responses;
using Jellyfin.MythTv.Protocol;
using Jellyfin.MythTv.Model;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.MythTv
{
    public class LiveTvService : ILiveTvService
    {
        private readonly IHttpClient _httpClient;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private ProtoManager _manager;
        private IImageGrabber _imageGrabber;

        public DateTime LastRecordingChange = DateTime.MinValue;

        // cache the listings data
        private readonly AsyncLock _guideLock = new AsyncLock();
        private GuideResponse _guide;

        // cache the channelId -> chanNum map for liveTV
        private readonly AsyncLock _channelLock = new AsyncLock();
        private Dictionary<string, string> channelNums;

        public LiveTvService(IHttpClient httpClient, IJsonSerializer jsonSerializer, ILogger logger, IFileSystem fileSystem)
        {
            _httpClient = httpClient;
            _jsonSerializer = jsonSerializer;
            _logger = logger;
            _fileSystem = fileSystem;
        }

        /// <summary>
        /// Ensure that we are connected to the NextPvr server
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task EnsureSetup()
        {
            var config = Plugin.Instance.Configuration;

            if (string.IsNullOrEmpty(config.Host))
            {
                _logger.LogError("[MythTV] Host must be configured.");
                throw new InvalidOperationException("MythTV host must be configured.");
            }


            List<StorageGroupDir> storageGroups;
            using (var stream = await _httpClient.Get(GetOptions(CancellationToken.None, "/Myth/GetStorageGroupDirs")).ConfigureAwait(false))
            {
                storageGroups = new MythResponse().GetStorageGroupDirs(stream, _jsonSerializer, _logger, true);
            }
            
            List<StorageGroupMap> storageGroupMaps = config.StorageGroupMaps;
            if (!storageGroupMaps.Any())
            {
                storageGroupMaps = storageGroups.Select(x => new StorageGroupMap {
                    GroupName = x.GroupName,
                    DirName = x.DirName,
                    DirNameOverride = x.DirName
                }).ToList();
            }
            else
            {
                // drop groups no longer on server
                storageGroupMaps.RemoveAll(g => !storageGroups.Select(x => x.GroupName).Contains(g.GroupName));

                // add in new groups from server
                List<StorageGroupDir> newGroups = storageGroups;
                newGroups.RemoveAll(g => storageGroupMaps.Select(x => x.GroupName).Contains(g.GroupName));
                storageGroupMaps.AddRange(newGroups.Select(x => new StorageGroupMap {
                    GroupName = x.GroupName,
                    DirName = x.DirName,
                    DirNameOverride = x.DirName
                }));
            }

            // make sure there are no trailing path separators
            foreach (var group in storageGroupMaps)
            {
                group.DirName = group.DirName.TrimEnd('/');
                group.DirNameOverride = group.DirNameOverride.TrimEnd('/');
            }
            
            Plugin.Instance.Configuration.StorageGroupMaps = storageGroupMaps;

            List<string> recGroupNames;
            using (var stream = await _httpClient.Get(GetOptions(CancellationToken.None, "/Dvr/GetRecGroupList")).ConfigureAwait(false))
            {
                recGroupNames = new DvrResponse().GetRecGroupList(stream, _jsonSerializer, _logger);
            }

            List<RecGroup> recGroups = config.RecGroups;
            if (!recGroups.Any())
            {
                recGroups = recGroupNames.Select(x => new RecGroup(x)).ToList();
            }
            else
            {
                // drop groups no longer on server
                recGroups.RemoveAll(g => !recGroupNames.Contains(g.Name));

                // add in new groups from server
                recGroups.AddRange(recGroupNames.Except(recGroups.Select(x => x.Name)).Select(g => new RecGroup(g)).ToList());
            }
            Plugin.Instance.Configuration.RecGroups = recGroups;
            
            Plugin.Instance.SaveConfiguration();


            if (config.UseSchedulesDirectImages) {
                _imageGrabber = new SchedulesDirectImages(_httpClient, _jsonSerializer, _logger);
            }
        }

        private HttpRequestOptions PostOptions(CancellationToken cancellationToken, string requestContent, string uriPathQuery, params object[] plist) 
        {
            var options = GetOptions(cancellationToken, uriPathQuery, plist);
            
            if (!string.IsNullOrWhiteSpace(requestContent))
            {
                options.RequestContentType = "application/x-www-form-urlencoded";
                options.RequestContent = requestContent;
            }

            return options;
        }

        private HttpRequestOptions GetOptions(CancellationToken cancellationToken, string uriPathQuery, params object[] plist)
        {
            var options = new HttpRequestOptions
                {
                    CancellationToken = cancellationToken,
                    Url = string.Format("{0}{1}", Plugin.Instance.Configuration.WebServiceUrl, string.Format(uriPathQuery, plist)),
                    AcceptHeader = "application/json"
                };            

            return options;
        }

        private string ConvertJsonRecRuleToPost(string serializedRule)
        {
            string ret = serializedRule
               .Replace("{", string.Empty)
               .Replace("}", string.Empty);
            ret = Regex.Replace(ret, "\"Id\"", "\"RecordId\"");
            ret = Regex.Replace(ret, "\"CallSign\"", "\"Station\"");
            ret = Regex.Replace(ret, "\":\"?", "=");
            ret = Regex.Replace(ret, "\"?,\"", "&");
            ret = Regex.Replace(ret, @"(T\d\d:\d\d:\d\d)\.\d+Z", "$1");
            ret = ret.Replace("\"", string.Empty)
                .Trim();

            return ret;
        }

        private string FormatMythDate(DateTime inDate)
        {
            return inDate.ToString("yyyy-MM-ddTHH:mm:ss");
        }

        /// <summary>
        /// Gets the channels async.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task{IEnumerable{ChannelInfo}}.</returns>
        public async Task<IEnumerable<ChannelInfo>> GetChannelsAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[MythTV] Start GetChannels Async, retrieve all channels");

            var sources = await GetVideoSourceList(cancellationToken);
            var channels = new List<ChannelInfo>();
            foreach (var sourceId in sources) {
                
                var options = GetOptions(cancellationToken,
                                         "/Channel/GetChannelInfoList?SourceID={0}&Details=true&OnlyVisible=true",
                                         sourceId);
                    
                using (var stream = await _httpClient.Get(options).ConfigureAwait(false))
                {
                    channels.AddRange(ChannelResponse.GetChannels(stream, _jsonSerializer, _logger,
                                                                  Plugin.Instance.Configuration.LoadChannelIcons));
                }
            }

            using (var releaser = await _channelLock.LockAsync()) {
                channelNums = channels.ToDictionary(i => i.Id, i => i.Number);
            }
            
            return channels;
        }

        /// <summary>
        /// Gets the Recordings async
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task{IEnumerable{RecordingInfo}}</returns>
        public async Task<IEnumerable<RecordingInfo>> GetRecordingsAsync(CancellationToken cancellationToken)
        {

            _logger.LogInformation("[MythTV] Start GetRecordings Async, retrieve all 'Pending', 'Inprogress' and 'Completed' recordings ");
            await EnsureSetup();

            IEnumerable<RecordingInfo> outp;

            using (var stream = await _httpClient.Get(GetOptions(cancellationToken, "/Dvr/GetRecordedList?RecGroup=Default&Count=10&Descending=true")).ConfigureAwait(false))
            {
                outp = new DvrResponse(Plugin.Instance.Configuration.StorageGroupMaps).GetRecordings(stream, _jsonSerializer, _logger, _fileSystem).ToList();
            }

            if (_imageGrabber != null)
            {
                await _imageGrabber.AddImages(outp, cancellationToken);
            }

            return outp;

        }

        /// <summary>
        /// Delete the Recording async from the disk
        /// </summary>
        /// <param name="recordingId">The recordingId</param>
        /// <param name="cancellationToken">The cancellationToken</param>
        /// <returns></returns>
        public async Task DeleteRecordingAsync(string recordingId, CancellationToken cancellationToken)
        {

            _logger.LogInformation(string.Format("[MythTV] Start Delete Recording Async for recordingId: {0}", recordingId));
            await EnsureSetup();

            var options = PostOptions(cancellationToken,
                                      $"RecordedId={recordingId}",
                                      "/Dvr/DeleteRecording");
            await _httpClient.Post(options).ConfigureAwait(false);

            LastRecordingChange = DateTime.UtcNow;

        }

        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value>The name.</value>
        public string Name
        {
            get { return "MythTV"; }
        }

        /// <summary>
        /// Cancel pending scheduled Recording 
        /// </summary>
        /// <param name="timerId">The timerId</param>
        /// <param name="cancellationToken">The cancellationToken</param>
        /// <returns></returns>
        public async Task CancelTimerAsync(string timerId, CancellationToken cancellationToken)
        {

            _logger.LogInformation($"[MythTV] Start Cancel Recording Async for recordingId: {timerId}");

            // A timer coming from a series timer will have a ficticious id
            // of the form xxx_yyyy
            // In this case we have to create a new 'do not record' rule for the program
            if (timerId.Contains('_'))
            {
                var ChannelId = timerId.Split('_')[0];
                var StartDate = new DateTime(Convert.ToInt64(timerId.Split('_')[1]));
                await CreateDoNotRecordTimerAsync(ChannelId, StartDate, cancellationToken);
                return;
            }

            // We are cancelling a legitimate single timer
            await EnsureSetup();

            var options = PostOptions(cancellationToken, $"RecordId={timerId}", "/Dvr/RemoveRecordSchedule");
            await _httpClient.Post(options).ConfigureAwait(false);

            LastRecordingChange = DateTime.UtcNow;
        
        }

        private async Task<IEnumerable<string>> GetVideoSourceList(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[MythTV] Start GetVideoSourceList");

            var options = GetOptions(cancellationToken,
                                     "/Channel/GetVideoSourceList");

            using (var sourcesstream = await _httpClient.Get(options).ConfigureAwait(false))
            {
                return ChannelResponse.GetVideoSourceList(sourcesstream, _jsonSerializer, _logger);
            }
        }

        /// <summary>
        /// Create a new recording
        /// </summary>
        /// <param name="info">The TimerInfo</param>
        /// <param name="cancellationToken">The cancellationToken</param>
        /// <returns></returns>
        public async Task CreateTimerAsync(TimerInfo info, CancellationToken cancellationToken)
        {

            var timerJson = _jsonSerializer.SerializeToString(info);
            _logger.LogInformation($"[MythTV] Start CreateTimer Async for TimerInfo\n{timerJson}");

            await EnsureSetup();

            var options = GetRuleStreamOptions(info.ProgramId, info.StartDate, cancellationToken);
            using (var stream = await _httpClient.Get(options))
            {
                try
                {
                    var json = new DvrResponse().GetNewTimerJson(info, stream, _jsonSerializer, _logger);
                    var post = PostOptions(cancellationToken,
                                           ConvertJsonRecRuleToPost(json),
                                           "/Dvr/AddRecordSchedule");
                    await _httpClient.Post(post).ConfigureAwait(false);
                }
                catch (ExistingTimerException existing)
                {
                    _logger.LogInformation($"[MythTV] found existing rule {existing.id}");
                    await CancelTimerAsync(existing.id, cancellationToken);
                }
            }          

        }

        private async Task CreateDoNotRecordTimerAsync(string ChannelId, DateTime StartDate,
                                                       CancellationToken cancellationToken)
        {

            _logger.LogInformation($"[MythTV] Start CreateDoNotRecordTimer Async for Channel {ChannelId} at {StartDate}");

            await EnsureSetup();

            var StartTime = FormatMythDate(StartDate);
            var url = $"/Dvr/AddDontRecordSchedule?ChanId={ChannelId}&StartTime={StartTime}&NeverRecord=False";

            var options = GetOptions(cancellationToken, url);
            using (var stream = await _httpClient.Get(options))
            {
                //nothing to do
            }

        }

        /// <summary>
        /// Get the pending Recordings.
        /// </summary>
        /// <param name="cancellationToken">The CancellationToken</param>
        /// <returns></returns>
        public async Task<IEnumerable<TimerInfo>> GetTimersAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[MythTV] Start GetTimer Async, retrieve the 'Pending' recordings");
            await EnsureSetup();

            using (var stream = await _httpClient.Get(GetOptions(cancellationToken, "/Dvr/GetUpcomingList?ShowAll=false")).ConfigureAwait(false))
            {
                return  new DvrResponse().GetUpcomingList(stream, _jsonSerializer, _logger);
            }
        }

        /// <summary>
        /// Get the recurrent recordings
        /// </summary>
        /// <param name="cancellationToken">The CancellationToken</param>
        /// <returns></returns>
        public async Task<IEnumerable<SeriesTimerInfo>> GetSeriesTimersAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[MythTV] Start GetSeriesTimer Async, retrieve the recurring recordings");
            await EnsureSetup();

            using (var stream = await _httpClient.Get(GetOptions(cancellationToken, "/Dvr/GetRecordScheduleList")).ConfigureAwait(false))
            {
                return new DvrResponse().GetSeriesTimers(stream, _jsonSerializer, _logger);
            }
        }

        private HttpRequestOptions GetRuleStreamOptions(string ProgramId, DateTime StartDate,
                                                        CancellationToken cancellationToken)
        {
            //split the program id back into channel + starttime if ChannelId not defined
            var ChanId = ProgramId.Split('_')[0];
            var StartTime = FormatMythDate(StartDate);

            var url = $"/Dvr/GetRecordSchedule?ChanId={ChanId}&StartTime={StartTime}";

            //now get myth to generate the standard recording template for the program
            return GetOptions(cancellationToken, url);
        }

        private HttpRequestOptions GetRuleStreamOptions(string Id, CancellationToken cancellationToken)
        {
            var url = $"/Dvr/GetRecordSchedule?RecordId={Id}";

            //now get myth to generate the standard recording template for the program
            return GetOptions(cancellationToken, url);
        }


        /// <summary>
        /// Create a recurrent recording
        /// </summary>
        /// <param name="info">The recurrend program info</param>
        /// <param name="cancellationToken">The CancelationToken</param>
        /// <returns></returns>
        public async Task CreateSeriesTimerAsync(SeriesTimerInfo info, CancellationToken cancellationToken)
        {

            var seriesTimerJson = _jsonSerializer.SerializeToString(info);
            _logger.LogInformation($"[MythTV] Start CreateSeriesTimer Async for SeriesTimerInfo\n{seriesTimerJson}");

            await EnsureSetup();

            var options = GetRuleStreamOptions(info.ProgramId, info.StartDate, cancellationToken);
            using (var stream = await _httpClient.Get(options))
            {
                var json = new DvrResponse().GetNewSeriesTimerJson(info, stream, _jsonSerializer, _logger);
                var post = PostOptions(cancellationToken, ConvertJsonRecRuleToPost(json), "/Dvr/AddRecordSchedule");
                await _httpClient.Post(post).ConfigureAwait(false);
            }          
        }

        /// <summary>
        /// Update the series Timer
        /// </summary>
        /// <param name="info">The series program info</param>
        /// <param name="cancellationToken">The CancellationToken</param>
        /// <returns></returns>
        public async Task UpdateSeriesTimerAsync(SeriesTimerInfo info, CancellationToken cancellationToken)
        {
            var seriesTimerJson = _jsonSerializer.SerializeToString(info);
            _logger.LogInformation($"[MythTV] Start UpdateSeriesTimer Async for SeriesTimerInfo\n{seriesTimerJson}");

            await EnsureSetup();

            var options = GetRuleStreamOptions(info.Id, cancellationToken);
            using (var stream = await _httpClient.Get(options))
            {
                var json = new DvrResponse().GetNewSeriesTimerJson(info, stream, _jsonSerializer, _logger);
                var post = PostOptions(cancellationToken, ConvertJsonRecRuleToPost(json), "/Dvr/UpdateRecordSchedule");
                await _httpClient.Post(post).ConfigureAwait(false);
            }          

        }

        /// <summary>
        /// Update a single Timer
        /// </summary>
        /// <param name="info">The program info</param>
        /// <param name="cancellationToken">The CancellationToken</param>
        /// <returns></returns>
        public async Task UpdateTimerAsync(TimerInfo info, CancellationToken cancellationToken)
        {
            var timerJson = _jsonSerializer.SerializeToString(info);
            _logger.LogInformation($"[MythTV] Start UpdateTimer Async for TimerInfo\n{timerJson}");

            await EnsureSetup();

            var options = GetRuleStreamOptions(info.Id, cancellationToken);
            using (var stream = await _httpClient.Get(options))
            {
                var json = new DvrResponse().GetNewTimerJson(info, stream, _jsonSerializer, _logger);
                var post = PostOptions(cancellationToken, ConvertJsonRecRuleToPost(json), "/Dvr/UpdateRecordSchedule");
                await _httpClient.Post(post).ConfigureAwait(false);
            }

            LastRecordingChange = DateTime.UtcNow;
        }

        /// <summary>
        /// Cancel the Series Timer
        /// </summary>
        /// <param name="timerId">The Timer Id</param>
        /// <param name="cancellationToken">The CancellationToken</param>
        /// <returns></returns>
        public async Task CancelSeriesTimerAsync(string timerId, CancellationToken cancellationToken)
        {

            _logger.LogInformation(string.Format("[MythTV] Start Cancel SeriesRecording Async for recordingId: {0}", timerId));
            await EnsureSetup();

            var options = PostOptions(cancellationToken,
                                      $"RecordId={timerId}",
                                      "/Dvr/RemoveRecordSchedule");
            await _httpClient.Post(options).ConfigureAwait(false);

        }
               
        public Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(string channelId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private async Task EnsureConnection()
        {
            _logger.LogInformation("[MythTV] Connecting...");
            
            if (_manager == null)
            {
                _manager = new ProtoManager {
                    Server = Plugin.Instance.Configuration.Host,
                    Port = 6543
                };

                await _manager.Open();
            }
        }

        public async Task<MediaSourceInfo> GetChannelStream(string channelId, string mediaSourceId, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"[MythTV] Start ChannelStream for {channelId}");

            // await GetChannels if channelNums isn't populated
            if (channelNums == null)
                await GetChannelsAsync(cancellationToken);

            // make sure livetv connected
            await EnsureConnection();
            
            var id = await _manager.SpawnLiveTV(channelNums[channelId]);
            if (id == 0)
                return new MediaSourceInfo();
            
            var filepath = await _manager.GetCurrentRecording(id, Plugin.Instance.Configuration.StorageGroupMaps);

            _logger.LogInformation($"[MythTV] ChannelStream at {filepath}");

            var output = new MediaSourceInfo
            {
                Id = id.ToString(),
                Path = filepath,
                Protocol = MediaProtocol.File,
                ReadAtNativeFramerate = true,
                MediaStreams = new List<MediaStream>
                {
                    new MediaStream
                    {
                        Type = MediaStreamType.Video,
                        // Set the index to -1 because we don't know the exact index of the video stream within the container
                        Index = -1,

                        // Set to true if unknown to enable deinterlacing
                        IsInterlaced = true
                    },
                    new MediaStream
                    {
                        Type = MediaStreamType.Audio,
                        // Set the index to -1 because we don't know the exact index of the audio stream within the container
                        Index = -1,

                        // Set to true if unknown to enable deinterlacing
                        IsInterlaced = true
                    }
                },
                IsInfiniteStream = true
            };

            return output;
        }

        public async Task CloseLiveStream(string id, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"[MythTV] Closing {id}");
            await _manager.StopLiveTV(int.Parse(id));
        }

        public async Task<SeriesTimerInfo> GetNewTimerDefaultsAsync(CancellationToken cancellationToken, ProgramInfo program = null)
        {
            _logger.LogInformation("[MythTV] Start GetNewTimerDefault Async");
            await EnsureSetup();
            using (var stream = await _httpClient.Get(GetOptions(cancellationToken, "/Dvr/GetRecordSchedule?Template=Default")).ConfigureAwait(false))
            {
                return new DvrResponse().GetDefaultTimerInfo(stream, _jsonSerializer, _logger);
            }               
        }

        private async Task CacheGuideResponse(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
        {
            using (var releaser = await _guideLock.LockAsync()) {
            
                if (_guide != null && (DateTime.Now - _guide.FetchTime).Hours < 1)
                    return;

                _logger.LogInformation("[MythTV] Start CacheGuideResponse");

                await EnsureSetup();
            
                var options = GetOptions(cancellationToken,
                                         "/Guide/GetProgramGuide?StartTime={0}&EndTime={1}&Details=1",
                                         FormatMythDate(startDate),
                                         FormatMythDate(endDate));

                using (var stream = await _httpClient.Get(options).ConfigureAwait(false))
                {
                    _guide = new GuideResponse(stream, _jsonSerializer);
                }
            }

            _logger.LogInformation("[MythTV] End CacheGuideResponse");
        }

        public async Task<IEnumerable<ProgramInfo>> GetProgramsAsync(string channelId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
        {
            _logger.LogInformation("[MythTV] Start GetPrograms Async, retrieve programs for: {0}", channelId);

            await CacheGuideResponse(startDate, endDate, cancellationToken);
            IEnumerable<ProgramInfo> programs;
            
            using (var releaser = await _guideLock.LockAsync())
            {
                programs = _guide.GetPrograms(channelId, _logger).ToList();
            }

            if (_imageGrabber != null)
            {
                await _imageGrabber.AddImages(programs, cancellationToken);
            }

            return programs;
        }

        public Task RecordLiveStream(string id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public string HomePageUrl
        {
            get { return "http://www.mythtv.org/"; }
        }

        public Task ResetTuner(string id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
