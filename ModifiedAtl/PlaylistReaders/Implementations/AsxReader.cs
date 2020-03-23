using ATL.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Xml;

namespace ATL.PlaylistReaders.BinaryLogic
{
    /// <summary>
    /// ASX playlist reader
    /// 
    /// Implementation notes : Playlist items other than local files (e.g. file accessible via HTTP) are not supported
    /// </summary>
    public class AsxReader : PlaylistReader
    {
        public override IList<String> FilesList
        {
            get
            {
                if (Files == null)
                {
                    Initialize();
                }

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
            // The following flags indicate if the parser is currently reading
            // the content of the corresponding tag
            var inPlaylist = false;
            var inTrack = false;

            Files = new List<String>();

            using (var source = XmlReader.Create(String.Join('\n', FileContents)))
            {
                while (source.Read())
                {
                    if (source.NodeType == XmlNodeType.Element)
                    {
                        if (source.Name.Equals("asx", StringComparison.OrdinalIgnoreCase))
                        {
                            inPlaylist = true;
                        }
                        else if (inPlaylist && source.Name.Equals("entry", StringComparison.OrdinalIgnoreCase))
                        {
                            inTrack = true;
                        }
                        else if (inTrack && source.Name.Equals("ref", StringComparison.OrdinalIgnoreCase))
                        {
                            Files.Add(GetResourceLocation(source));
                        }
                    }
                    else if (source.NodeType == XmlNodeType.EndElement)
                    {
                        if (source.Name.Equals("asx", StringComparison.OrdinalIgnoreCase))
                        {
                            inPlaylist = false;
                        }
                        else if (source.Name.Equals("entry", StringComparison.OrdinalIgnoreCase))
                        {
                            inTrack = false;
                        }
                    }
                }
            }
        }

        private String GetResourceLocation(XmlReader source)
        {
            var result = "";
            while (source.MoveToNextAttribute()) // Read the attributes.
            {
                if (!source.Name.Equals("href", StringComparison.OrdinalIgnoreCase)) continue;
                result = source.Value;

                if (result.Contains("://") && Uri.IsWellFormedUriString(result, UriKind.RelativeOrAbsolute))
                {
                    try
                    {
                        var uri = new Uri(result);
                        if (uri.IsFile)
                        {
                            result = uri.LocalPath;
                        }
                    }
                    catch (UriFormatException)
                    {
                        LogDelegator.GetLogDelegate()(Log.LV_WARNING,
                            result + " is not a valid URI [" + FFileName + "]");
                    }
                }

                if (System.IO.Path.IsPathRooted(result)) continue;

                var updated = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(FFileName), result);
                UpdateFile(result, updated);
                return updated;
            }

            return result;
        }
    }
}