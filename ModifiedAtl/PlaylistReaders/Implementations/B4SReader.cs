using ATL.Logging;
using Commons;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace ATL.PlaylistReaders.BinaryLogic
{
    /// <summary>
    /// B4S playlist reader
    /// </summary>
    public class B4SReader : PlaylistReader
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
            // The following flags indicate if the parser is currently reading
            // the content of the corresponding tag
            var inPlaylist = false;
            var inTracklist = false;

            Files = new List<String>();

            using (var source = XmlReader.Create(String.Join('\n', FileContents)))
            {
                while (source.Read())
                {
                    if (source.NodeType == XmlNodeType.Element)
                    {
                        if (source.Name.Equals("WinampXML", StringComparison.OrdinalIgnoreCase))
                        {
                            inPlaylist = true;
                        }
                        else if (inPlaylist && source.Name.Equals("playlist", StringComparison.OrdinalIgnoreCase))
                        {
                            inTracklist = true;
                        }
                        else if (inTracklist && source.Name.Equals("entry", StringComparison.OrdinalIgnoreCase))
                        {
                            Files.Add(getResourceLocation(source));
                        }
                    }
                    else if (source.NodeType == XmlNodeType.EndElement)
                    {
                        if (source.Name.Equals("WinampXML", StringComparison.OrdinalIgnoreCase))
                        {
                            inPlaylist = false;
                        }
                        else if (inPlaylist && source.Name.Equals("playlist", StringComparison.OrdinalIgnoreCase))
                        {
                            inTracklist = false;
                        }
                    }
                }
            }
        }

        private String getResourceLocation(XmlReader source)
        {
            var result = "";
            while (source.MoveToNextAttribute()) // Read the attributes.
            {
                if (!source.Name.Equals("Playstring", StringComparison.OrdinalIgnoreCase)) continue;
                result = source.Value.Substring(5, source.Value.Length - 5);
                if (System.IO.Path.IsPathRooted(result)) continue;

                var updated = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(FFileName), result);
                UpdateFile(result, updated);
                return updated;
            }

            return result;
        }
    }
}