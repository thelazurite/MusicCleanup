using System;
using System.Collections.Generic;
using System.IO;

namespace ATL.CatalogDataReaders.BinaryLogic
{
    /// <summary>
    /// Class for cuesheet files reading (extension .cue)
    /// http://wiki.hydrogenaud.io/index.php?title=Cue_sheet
    /// </summary>
    public class Cue : ICatalogDataReader
    {
        private String path = "";
        private String title = "";
        private String artist = "";
        private String comments = "";

        IList<Track> tracks = new List<Track>();


        public String Path
        {
            get => path;
            set => path = value;
        }

        public String Artist => artist;

        public String Comments => comments;

        public String Title => title;

        public IList<Track> Tracks => tracks;


        // ----------------------- Constructor

        public Cue(String path)
        {
            this.path = path;
            read();
        }


        // ----------------------- Specific methods

        private String stripBeginEndQuotes(String s)
        {
            if (s.Length < 2) return s;
            if ((s[0] != '"') || (s[s.Length - 1] != '"')) return s;

            return s.Substring(1, s.Length - 2);
        }

        private static Int32 decodeTimecodeToMs(String timeCode)
        {
            var result = -1;
            var valid = false;

            var frames = 0;
            var minutes = 0;
            var seconds = 0;

            if (timeCode.Contains(":"))
            {
                valid = true;
                var parts = timeCode.Split(':');
                if (parts.Length >= 1) frames = Int32.Parse(parts[parts.Length - 1]);
                if (parts.Length >= 2) seconds = Int32.Parse(parts[parts.Length - 2]);
                if (parts.Length >= 3) minutes = Int32.Parse(parts[parts.Length - 3]);

                result = (Int32)Math.Round(frames / 75.0); // 75 frames per seconds (CD sectors)
                result += seconds;
                result += minutes * 60;
            }

            if (!valid) result = -1;

            return result * 1000;
        }

        private void read()
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 2048, FileOptions.SequentialScan))
            using (TextReader source = new StreamReader(fs, System.Text.Encoding.UTF8))
            {
                var s = source.ReadLine();
                Track physicalTrack = null;
                var audioFilePath = "";

                Track currentTrack = null;
                Track previousTrack = null;
                var previousTimeOffset = 0;
                var indexRelativePosition = 0;

                while (s != null)
                {
                    s = s.Trim();
                    var firstBlank = s.IndexOf(' ');
                    var firstWord = s.Substring(0, firstBlank);
                    var trackInfo = s.Split(' ');


                    if (null == currentTrack)
                    {
                        if ("REM".Equals(firstWord, StringComparison.OrdinalIgnoreCase))
                        {
                            if (comments.Length > 0) comments += Settings.InternalValueSeparator;
                            comments += s.Substring(firstBlank + 1, s.Length - firstBlank - 1);
                        }
                        else if ("PERFORMER".Equals(firstWord, StringComparison.OrdinalIgnoreCase))
                        {
                            artist = stripBeginEndQuotes(s.Substring(firstBlank + 1, s.Length - firstBlank - 1));
                        }
                        else if ("TITLE".Equals(firstWord, StringComparison.OrdinalIgnoreCase))
                        {
                            title = stripBeginEndQuotes(s.Substring(firstBlank + 1, s.Length - firstBlank - 1));
                        }
                        else if ("FILE".Equals(firstWord, StringComparison.OrdinalIgnoreCase))
                        {
                            audioFilePath = s.Substring(firstBlank + 1, s.Length - firstBlank - 1);
                            audioFilePath = audioFilePath.Substring(0, audioFilePath.LastIndexOf(' ')); // Get rid of the last word representing the audio format
                            audioFilePath = stripBeginEndQuotes(audioFilePath);

                            // Strip the ending word representing the audio format
                            if (!System.IO.Path.IsPathRooted(audioFilePath))
                            {
                                audioFilePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), audioFilePath);
                            }
                            physicalTrack = new Track(audioFilePath);
                        }
                        else if ("TRACK".Equals(firstWord, StringComparison.OrdinalIgnoreCase))
                        {
                            currentTrack = new Track();
                            if (trackInfo.Length > 0) currentTrack.TrackNumber = Byte.Parse(trackInfo[1]);
                            currentTrack.Genre = physicalTrack.Genre;
                            currentTrack.IsVBR = physicalTrack.IsVBR;
                            currentTrack.Bitrate = physicalTrack.Bitrate;
                            currentTrack.CodecFamily = physicalTrack.CodecFamily;
                            currentTrack.Year = physicalTrack.Year;
                            currentTrack.PictureTokens = physicalTrack.PictureTokens;
                            currentTrack.DiscNumber = physicalTrack.DiscNumber;
                            currentTrack.Artist = "";
                            currentTrack.Title = "";
                            currentTrack.Comment = "";
                        }
                    } else
                    {
                        if ("TRACK".Equals(firstWord, StringComparison.OrdinalIgnoreCase))
                        {
                            if (0 == currentTrack.Artist.Length) currentTrack.Artist = artist;
                            if (0 == currentTrack.Artist.Length) currentTrack.Artist = physicalTrack.Artist;
                            if (0 == currentTrack.Title.Length) currentTrack.Title = physicalTrack.Title;
                            if (0 == currentTrack.Comment.Length) currentTrack.Comment = physicalTrack.Comment;
                            if (0 == currentTrack.TrackNumber) currentTrack.TrackNumber = physicalTrack.TrackNumber;
                            currentTrack.Album = title;

                            tracks.Add(currentTrack);

                            previousTrack = currentTrack;
                            currentTrack = new Track();
                            if (trackInfo.Length > 0) currentTrack.TrackNumber = Byte.Parse(trackInfo[1]);
                            currentTrack.Genre = physicalTrack.Genre;
                            currentTrack.IsVBR = physicalTrack.IsVBR;
                            currentTrack.Bitrate = physicalTrack.Bitrate;
                            currentTrack.CodecFamily = physicalTrack.CodecFamily;
                            currentTrack.Year = physicalTrack.Year;
                            currentTrack.PictureTokens = physicalTrack.PictureTokens;
                            currentTrack.DiscNumber = physicalTrack.DiscNumber;
                            currentTrack.Artist = "";
                            currentTrack.Title = "";
                            currentTrack.Comment = "";

                            indexRelativePosition = 0;
                        }
                        else if ("REM".Equals(firstWord, StringComparison.OrdinalIgnoreCase))
                        {
                            if (currentTrack.Comment.Length > 0) currentTrack.Comment += Settings.InternalValueSeparator;
                            currentTrack.Comment += s.Substring(firstBlank + 1, s.Length - firstBlank - 1);
                        }
                        else if ("PERFORMER".Equals(firstWord, StringComparison.OrdinalIgnoreCase))
                        {
                            currentTrack.Artist = stripBeginEndQuotes(s.Substring(firstBlank + 1, s.Length - firstBlank - 1));
                        }
                        else if ("TITLE".Equals(firstWord, StringComparison.OrdinalIgnoreCase))
                        {
                            currentTrack.Title = stripBeginEndQuotes(s.Substring(firstBlank + 1, s.Length - firstBlank - 1));
                        }
                        else if ( ("PREGAP".Equals(firstWord, StringComparison.OrdinalIgnoreCase)) || ("POSTGAP".Equals(firstWord, StringComparison.OrdinalIgnoreCase)))
                        {
                            if (trackInfo.Length > 0) currentTrack.DurationMs += decodeTimecodeToMs(trackInfo[1]);
                        }
                        else if ("INDEX".Equals(firstWord, StringComparison.OrdinalIgnoreCase))
                        {
                            if (trackInfo.Length > 1)
                            {
                                var timeOffset = decodeTimecodeToMs(trackInfo[2]);

                                if (0 == indexRelativePosition && previousTrack != null)
                                {
                                    previousTrack.DurationMs += timeOffset - previousTimeOffset;
                                }
                                else
                                {
                                    currentTrack.DurationMs += timeOffset - previousTimeOffset;
                                }
                                previousTimeOffset = timeOffset;

                                indexRelativePosition++;
                            }
                        }

                    }

                    s = source.ReadLine();
                } // while

                if (currentTrack != null)
                {
                    if (0 == currentTrack.Artist.Length) currentTrack.Artist = artist;
                    if (0 == currentTrack.Artist.Length) currentTrack.Artist = physicalTrack.Artist;
                    if (0 == currentTrack.Title.Length) currentTrack.Title = physicalTrack.Title;
                    if (0 == currentTrack.Comment.Length) currentTrack.Comment = physicalTrack.Comment;
                    if (0 == currentTrack.TrackNumber) currentTrack.TrackNumber = physicalTrack.TrackNumber;
                    currentTrack.Album = title;
                    currentTrack.DurationMs += physicalTrack.DurationMs - previousTimeOffset;

                    tracks.Add(currentTrack);
                }
            } // using

        } // read method

    }
}
