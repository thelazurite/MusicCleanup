using System;
using System.Collections.Generic;
using ATL.PlaylistReaders;

namespace MusicCleanup
{
    public class PathUpdate
    {
        public IList<String> files = new List<String>();
        public IList<TrackFilter> Tracks = new List<TrackFilter>(); 
        public IList<IPlaylistReader> Playlists = new List<IPlaylistReader>();
    }
}