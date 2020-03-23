using System;
using System.IO;
using System.Collections;
using System.Text;
using ATL.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ATL.PlaylistReaders.BinaryLogic
{
    /// <summary>
    /// M3U/M3U8 playlist reader
    /// </summary>
    public class M3UReader : PlaylistReader
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
            foreach (var s in FileContents.Where(itm => itm[0] != '#').ToList())
            {
                IsUri = s.StartsWith("file://");
                var check = IsUri ? new Uri(s).LocalPath:s; 
                if (!System.IO.Path.IsPathRooted(check))
                {
                    var u = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(FFileName), check);
                    UpdateFile(s, IsUri ? new Uri(u).AbsoluteUri : u);
                    Files.Add(u);
                }
                else
                {
                    Files.Add(System.IO.Path.GetFullPath(s));
                }
            }
        }
    }
}