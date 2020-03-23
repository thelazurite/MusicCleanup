using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace MusicCleanup
{
    public class TrackFilter
    {
        public String Path { get; set; }
        public String UpdatedPath { get; set; }

        public String PathUri => new Uri(Path).AbsoluteUri;
        public String UpdatedPathUri => new Uri(UpdatedPath).AbsoluteUri;

        public String FileType =>
            _fileType ?? (_fileType = System.IO.Path.GetExtension(Path).ToLower().Replace(".", ""));

        public String Artist
        {
            get => _artist;
            set => _artist = String.IsNullOrWhiteSpace(value.RemoveIllegalChars())
                ? "Unknown"
                : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value.RemoveIllegalChars()).Trim();
        }

        public String Album
        {
            get => _album;
            set => _album = String.IsNullOrWhiteSpace(value.RemoveIllegalChars())
                ? "Unknown"
                : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value.RemoveIllegalChars()).Trim();
        }

        public String Title
        {
            get => _title;
            set => _title = String.IsNullOrWhiteSpace(value) ? "Unknown" : value.RemoveIllegalChars().Trim();
        }

        public Int32 BitRate
        {
            get => _bitRate;
            set => _bitRate = value;
        }

        private String _fileType;
        private String _artist;
        private String _album;
        private String _title;
        private Int32 _bitRate;

    }
    public static class StringHelper {
        public static String RemoveIllegalChars(this String value)
        {
            var regexSearch = new String(System.IO.Path.GetInvalidFileNameChars());
            var r = new Regex($"[{Regex.Escape(regexSearch)}]");
            return r.Replace(value, "");
        }    
    }
}