using MediaBrowser.Controller.LiveTv;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.MythTv
{
    public interface IImageGrabber
    {
        Task AddImages(IEnumerable<ProgramInfo> programs, CancellationToken cancellationToken);
        Task AddImages(IEnumerable<RecordingInfo> programs, CancellationToken cancellationToken);
    }

    public class Images
    {
        public string Image {get; set;}
        public string Thumb {get; set;}
        public string Backdrop {get; set;}
    }
}
