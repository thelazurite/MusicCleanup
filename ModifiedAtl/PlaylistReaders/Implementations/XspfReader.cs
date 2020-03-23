using ATL.Logging;
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using Commons;
using System.Xml.Linq;

namespace ATL.PlaylistReaders.BinaryLogic
{
    /// <summary>
    /// XSPF (spiff) playlist reader
    /// 
    /// Implementation notes : Playlist items other than local files (e.g. file accessible via HTTP) are not supported
    /// </summary>
    public class XspfReader : PlaylistReader
    {
        public override IList<String> FilesList
        {
            get
            {
                if (Files == null)
                    Initialize();

                return Files;
            }
        }

        public override async Task Open()
        {
            await base.Open();
            Initialize();
        }

        private void Initialize()
        {
            Files = new List<String>();

            var inPlaylist = false;
            var inTracklist = false;
            var inTrack = false;
            var inLocation = false;
            var inImage = false;

            using (var source = XmlReader.Create(String.Join('\n', FileContents).ToStream()))
            {
                while (source.Read())
                {
                    if (source.NodeType == XmlNodeType.Element)
                    {
                        if (source.Name.Equals("playlist", StringComparison.OrdinalIgnoreCase))
                        {
                            inPlaylist = true;
                        }
                        else if (inPlaylist && source.Name.Equals("tracklist", StringComparison.OrdinalIgnoreCase))
                        {
                            inTracklist = true;
                        }
                        else if (inTracklist && source.Name.Equals("track", StringComparison.OrdinalIgnoreCase))
                        {
                            inTrack = true;
                        }
                        else if (inTrack && source.Name.Equals("location", StringComparison.OrdinalIgnoreCase))
                        {
                            inLocation = true;
                        }
                        else if (inTrack && source.Name.Equals("image", StringComparison.OrdinalIgnoreCase))
                        {
                            inImage = true;
                        }
                    }
                    else if (source.NodeType == XmlNodeType.Text)
                    {
                        GetReferenceLocation(inLocation, inImage, source);
                    }
                    else if (source.NodeType == XmlNodeType.EndElement)
                    {
                        if (source.Name.Equals("playlist", StringComparison.OrdinalIgnoreCase))
                        {
                            inPlaylist = false;
                        }
                        else if (source.Name.Equals("tracklist", StringComparison.OrdinalIgnoreCase))
                        {
                            inTracklist = false;
                        }
                        else if (source.Name.Equals("track", StringComparison.OrdinalIgnoreCase))
                        {
                            inTrack = false;
                        }
                        else if (source.Name.Equals("location", StringComparison.OrdinalIgnoreCase))
                        {
                            inLocation = false;
                        }
                        else if (inTrack && source.Name.Equals("image", StringComparison.OrdinalIgnoreCase))
                        {
                            inImage = false;
                        }
                    }
                } // while
            } // using
        }

        public override void UpdateFile(String original, String updated)
        {
            foreach (var s in FileContents.Where(itm => itm.Contains(original)).ToList())
            {
                var pos = Array.IndexOf(FileContents, s);
                if(pos > -1)FileContents[pos] = FileContents[pos].Replace(original, updated);
            }
        }

        private void GetReferenceLocation(Boolean inLocation, Boolean inImage, XmlReader source)
        {
            IsUri = true;
            if (!inLocation && !inImage) return;
            try
            {
                var uri = new Uri(source.Value);
                if (!uri.IsFile || !inLocation) return;
                if (!System.IO.Path.IsPathRooted(uri.LocalPath))
                {
                    var absolute = System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(FFileName), uri.LocalPath);
                    UpdateFile(uri.LocalPath, new Uri(absolute).AbsoluteUri);
                    Files.Add(absolute);
                }
                else
                {
                    Files.Add(uri.LocalPath);
                }

                //else if (inImage) result.Add(System.IO.Path.GetFullPath(uri.LocalPath));
                //TODO fetch track picture from playlists info ?
            }
            catch (UriFormatException)
            {
                LogDelegator.GetLogDelegate()(Log.LV_WARNING,
                    Files + " is not a valid URI [" + FFileName + "]");
            }
        }
    }
}