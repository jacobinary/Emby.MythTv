using Jellyfin.MythTv.Model;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.MythTv
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
            var progIds = programs.Select(p => p.ShowId).ToList();
            var images = await GetImageForPrograms(progIds, cancellationToken).ConfigureAwait(false);
            
            if (images == null)
                return;

            foreach (var program in programs)
            {
                var progImages = GetImageUrls(images, program.ShowId.Substring(0,10));

                program.ImageUrl = progImages.Image;
                program.ThumbImageUrl = progImages.Thumb;
                program.BackdropImageUrl = progImages.Backdrop;
            }
        }

        public async Task AddImages(IEnumerable<RecordingInfo> programs, CancellationToken cancellationToken)
        {
            _logger.LogDebug($"[MythTV] Add images");

            var progIds = programs.Select(p => p.ShowId).ToList();
            var images = await GetImageForPrograms(progIds, cancellationToken).ConfigureAwait(false);
            
            if (images == null)
                return;

            foreach (var program in programs)
            {
                if (program.ImageUrl != null)
                    continue;
                
                if (program.ShowId.Length >= 10) {
                    var progImages = GetImageUrls(images, program.ShowId.Substring(0,10));

                    program.ImageUrl = progImages.Image;
                }
            }
        }            

        private Images GetImageUrls(List<ScheduleDirect.ShowImages> images, string programID)
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

                outp.Image = GetProgramImage(imagesWithText, true, desiredAspect) ??
                    GetProgramImage(allImages, true, desiredAspect);

                _logger.LogDebug($"[MythTV] Found Schedules Direct Image for {programID}: {outp.Image}");

                outp.Thumb = GetProgramImage(imagesWithText, true, wideAspect);

                // Don't supply the same image twice
                if (string.Equals(outp.Image, outp.Thumb, StringComparison.Ordinal))
                {
                    outp.Thumb = null;
                }

                outp.Backdrop = GetProgramImage(imagesWithoutText, true, wideAspect);
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

        private string GetProgramImage(List<ScheduleDirect.ImageData> images, bool returnDefaultImage, double desiredAspect)
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
                    url = ApiUrl + "/image/" + uri;
                }
            }
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

        private async Task<List<ScheduleDirect.ShowImages>> GetImageForPrograms(List<string> programIds, CancellationToken cancellationToken)
        {
            _logger.LogDebug($"[MythTV] Fetching SchedulesDirect images");
            
            if (programIds.Count == 0)
            {
                return new List<ScheduleDirect.ShowImages>();
            }

            var uniqueProgramIds = new List<string>();
            foreach (var i in programIds)
            {
                if (i.Length >= 10)
                {
                    var imageId = i.Substring(0, 10);

                    if (!uniqueProgramIds.Contains(imageId))
                    {
                        uniqueProgramIds.Add($"\"{imageId}\"");
                    }
                }
            }

            var httpOptions = new HttpRequestOptions()
                {
                    Url = ApiUrl + "/metadata/programs",
                    UserAgent = "Jellyfin",
                    CancellationToken = cancellationToken,
                    RequestContent = $"[{String.Join(", ", uniqueProgramIds)}]",
                    LogErrorResponseBody = true
                };

            try
            {
                using (var innerResponse2 = await Post(httpOptions, true).ConfigureAwait(false))
                {
                    return _jsonSerializer.DeserializeFromStream<List<ScheduleDirect.ShowImages>>(innerResponse2.Content);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting image info from schedules direct");

                return new List<ScheduleDirect.ShowImages>();
            }
        }
    }

}
