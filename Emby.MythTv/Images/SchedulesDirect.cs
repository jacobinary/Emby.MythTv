using Emby.MythTv.Model;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.MythTv
{

    public class SchedulesDirectImages : IImageGrabber
    {
        private readonly IHttpClient _httpClient;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILogger _logger;

        private const string ApiUrl = "https://json.schedulesdirect.org/20141201";

        public SchedulesDirectImages(IHttpClient httpClient,
                                     IJsonSerializer jsonSerializer,
                                     ILogger logger)
        {
            _httpClient = httpClient;
            _jsonSerializer = jsonSerializer;
            _logger = logger;
        }

        public async Task AddImages(IEnumerable<ProgramInfo> programs, CancellationToken cancellationToken)
        {
            // get all images
            var progIds = programs.Select(p => p.ShowId).ToList();
            var images = await GetImageForPrograms(progIds, cancellationToken).ConfigureAwait(false);
            
            if (images == null)
                return;

            foreach (var program in programs)
            {
                var progImages = FilterImages(images, program.ShowId.Substring(0,10));

                program.ImageUrl = progImages.Image;
                program.ThumbImageUrl = progImages.Thumb;
                program.BackdropImageUrl = progImages.Backdrop;
            }
        }

        public async Task AddImages(IEnumerable<RecordingInfo> programs, CancellationToken cancellationToken)
        {
            _logger.Debug($"[MythTV] Add images");
            var progIds = programs.Select(p => p.ShowId).ToList();
            var images = await GetImageForPrograms(progIds, cancellationToken).ConfigureAwait(false);
            
            if (images == null)
                return;

            _logger.Debug($"[MythTV] Got images");

            foreach (var program in programs)
            {
                if (program.ImageUrl != null)
                    continue;
                
                _logger.Debug($"[MythTV] Fetching SchedulesDirect images for recording {program.ShowId}");
                var progImages = FilterImages(images, program.ShowId.Substring(0,10));

                program.ImageUrl = progImages.Image;
            }
        }            

        private Images FilterImages(List<ScheduleDirect.ShowImages> images, string programID)
        {

            var imageIndex = images.FindIndex(i => i.programID == programID);
            Images outp = new Images();

            if (imageIndex > -1)
            {
                var allImages = (images[imageIndex].data ?? new List<ScheduleDirect.ImageData>()).ToList();
                var imagesWithText = allImages.Where(i => string.Equals(i.text, "yes", StringComparison.OrdinalIgnoreCase)).ToList();
                var imagesWithoutText = allImages.Where(i => string.Equals(i.text, "no", StringComparison.OrdinalIgnoreCase)).ToList();

                double desiredAspect = 0.666666667;
                double wideAspect = 1.77777778;

                outp.Image = GetProgramImage(ApiUrl, imagesWithText, true, desiredAspect) ??
                    GetProgramImage(ApiUrl, allImages, true, desiredAspect);

                _logger.Debug($"[MythTV] Found Schedules Direct Image for {programID}: {outp.Image}");

                outp.Thumb = GetProgramImage(ApiUrl, imagesWithText, true, wideAspect);

                // Don't supply the same image twice
                if (string.Equals(outp.Image, outp.Thumb, StringComparison.Ordinal))
                {
                    outp.Thumb = null;
                }

                outp.Backdrop = GetProgramImage(ApiUrl, imagesWithoutText, true, wideAspect);
            }

            return outp;

        }

        private async Task<HttpResponseInfo> Post(HttpRequestOptions options,
                                                  bool enableRetry)
        {
            try
            {
                return await _httpClient.Post(options).ConfigureAwait(false);
            }
            catch (HttpException ex)
            {
                if (!ex.StatusCode.HasValue || (int)ex.StatusCode.Value >= 500)
                {
                    enableRetry = false;
                }

                if (!enableRetry)
                {
                    throw;
                }
            }
            return await Post(options, false).ConfigureAwait(false);
        }

        private string GetProgramImage(string apiUrl, List<ScheduleDirect.ImageData> images, bool returnDefaultImage, double desiredAspect)
        {
            string url = null;

            var matches = images;

            matches = matches
                .OrderBy(i => Math.Abs(desiredAspect - GetAspectRatio(i)))
                .ThenByDescending(GetSizeOrder)
                .ToList();

            var match = matches.FirstOrDefault();

            if (match == null)
            {
                return null;
            }

            var uri = match.uri;

            if (!string.IsNullOrWhiteSpace(uri))
            {
                if (uri.IndexOf("http", StringComparison.OrdinalIgnoreCase) != -1)
                {
                    url = uri;
                }
                else
                {
                    url = apiUrl + "/image/" + uri;
                }
            }
            //_logger.Debug("URL for image is : " + url);
            return url;
        }

        private int GetSizeOrder(ScheduleDirect.ImageData image)
        {
            if (!string.IsNullOrWhiteSpace(image.height))
            {
                int value;
                if (int.TryParse(image.height, out value))
                {
                    return value;
                }
            }

            return 0;
        }

        private double GetAspectRatio(ScheduleDirect.ImageData i)
        {
            int width = 0;
            int height = 0;

            if (!string.IsNullOrWhiteSpace(i.width))
            {
                int.TryParse(i.width, out width);
            }

            if (!string.IsNullOrWhiteSpace(i.height))
            {
                int.TryParse(i.height, out height);
            }

            if (height == 0 || width == 0)
            {
                return 0;
            }

            double result = width;
            result /= height;
            return result;
        }

        private async Task<List<ScheduleDirect.ShowImages>> GetImageForPrograms(List<string> programIds,
                                                                                CancellationToken cancellationToken)
        {
            _logger.Debug($"[MythTV] Fetching SchedulesDirect images");
            
            if (programIds.Count == 0)
            {
                return new List<ScheduleDirect.ShowImages>();
            }

            var imageIdString = "[";

            foreach (var i in programIds)
            {
                var imageId = i.Substring(0, 10);

                if (!imageIdString.Contains(imageId))
                {
                    imageIdString += "\"" + imageId + "\",";
                }
            }

            imageIdString = imageIdString.TrimEnd(',') + "]";

            var httpOptions = new HttpRequestOptions()
                {
                    Url = ApiUrl + "/metadata/programs",
                    UserAgent = "Emby",
                    CancellationToken = cancellationToken,
                    RequestContent = imageIdString.ToCharArray(),
                    LogErrorResponseBody = true,
                    // The data can be large so give it some extra time
                    TimeoutMs = 60000
                };

            try
            {
                using (var innerResponse2 = await Post(httpOptions, true).ConfigureAwait(false))
                {
                    return _jsonSerializer.DeserializeFromStream<List<ScheduleDirect.ShowImages>>(
                                                                                                  innerResponse2.Content);
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error getting image info from schedules direct", ex);

                return new List<ScheduleDirect.ShowImages>();
            }
        }
    }

}
