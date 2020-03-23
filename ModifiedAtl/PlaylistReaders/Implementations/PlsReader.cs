using System;
using System.IO;
using System.Text;
using ATL.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ATL.PlaylistReaders.BinaryLogic
{
    /// <summary>
    /// PLS playlist reader
    /// </summary>
    public class PlsReader : PlaylistReader
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
            foreach (var s in FileContents.Where(s => "FILE" == s.Substring(0, 4).ToUpper()))
            {
                var equalIndex = s.IndexOf("=", StringComparison.Ordinal) + 1;
                var fn = s.Substring(equalIndex, s.Length - equalIndex);
                if (!System.IO.Path.IsPathRooted(s))
                {
                    var uri = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(FFileName), fn);
                    Files.Add(uri);
                }
                else
                {
                    Files.Add(System.IO.Path.GetFullPath(fn));
                }
            }
        }
    }
}