using ATL.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Xml;
using Commons;

namespace ATL.PlaylistReaders.BinaryLogic
{
    /// <summary>
    /// SMIL playlist reader
    /// 
    /// This is a very basic implementation that lists every single audio file in the playlist
    /// 
    /// Implementation notes : Playlist items other than local files (e.g. file accessible via HTTP) are not supported
    /// </summary>
    public class SmilReader : PlaylistReader
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
            using (var source = XmlReader.Create(String.Join('\n', FileContents).ToStream()))
            {
                while (source.Read())
                {
                    if (source.NodeType != XmlNodeType.Element) continue;
                    if (source.Name.Equals("audio", StringComparison.OrdinalIgnoreCase))
                    {
                        Files.Add(GetResourceLocation(source));
                    }
                    else if (source.Name.Equals("media", StringComparison.OrdinalIgnoreCase))
                    {
                        Files.Add(GetResourceLocation(source));
                    }

                    break;
                }
            }
        }

        // Most SMIL sample playlists store resource location with a relative path
        private String GetResourceLocation(XmlReader source)
        {
            var result = "";
            while (source.MoveToNextAttribute()) // Read the attributes.
            {
                if (!source.Name.Equals("src", StringComparison.OrdinalIgnoreCase)) continue;
                result = source.Value;

                // It it an URI ?
                if (result.Contains("://"))
                {
                    if (Uri.IsWellFormedUriString(result, UriKind.RelativeOrAbsolute))
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
                    else
                    {
                        result = result.Replace("file:///", "").Replace("file://", "");
                    }
                }

                if (!System.IO.Path.IsPathRooted(result))
                {
                    result = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(FFileName), result);
                }

                result = System.IO.Path.GetFullPath(result);
            }

            return result;
        }
    }
}