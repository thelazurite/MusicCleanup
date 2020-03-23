using System;

namespace ATL
{
    public class ChapterInfo
    {
        public UInt32 StartTime = 0;          // Start time (ms)
        public UInt32 EndTime = 0;            // End time (ms)
        public UInt32 StartOffset = 0;        // Start offset (bytes)
        public UInt32 EndOffset = 0;          // End offset (bytes)
        public Boolean UseOffset = false;      // True if StartOffset / EndOffset are usable, false if not

        public String UniqueID = ""; // Specific to ID3v2

        public String Title = "";
        public String Subtitle = "";
        public String Url = "";
        public PictureInfo Picture = null;


        // ---------------- CONSTRUCTORS

        public ChapterInfo() { }

        public ChapterInfo(ChapterInfo chapter)
        {
            StartTime = chapter.StartTime; EndTime = chapter.EndTime; StartOffset = chapter.StartOffset; EndOffset = chapter.EndOffset; Title = chapter.Title; Subtitle = chapter.Subtitle; Url = chapter.Url; UniqueID = chapter.UniqueID;

            if (chapter.Picture != null) Picture = new PictureInfo(chapter.Picture);
        }
    }
}
