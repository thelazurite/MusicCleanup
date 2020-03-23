using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ATL;
using ATL.AudioData;
using ATL.PlaylistReaders;

namespace MusicCleanup
{
    internal static class Program
    {
        private static readonly String DefaultDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

        private static readonly Dictionary<String, PathUpdate> ImportDirectories =
            new Dictionary<String, PathUpdate>();

        private static readonly AudioDataIoFactory AudioFactory = AudioDataIoFactory.GetInstance();
        private static readonly PlaylistReaderFactory PlaylistFactory = PlaylistReaderFactory.GetInstance();

        private static String _exportDirectory;
        private static Boolean _updatePlaylists;

        private static async Task Main(String[] args)
        {
            Console.WriteLine(
                $@"
Music Cleanup, Copyright 2018 Dylan Eddies - Licensed under the MIT License

Please read the below carefully.

This application will read all available audio files in the inputted directories,
and will move and reorganize them in the provided export directory. 
This process CANNOT be reverted, so please ensure you have sufficient backups.

- Exported Audio files will be removed from their original folders.
- Files other than audio or playlist ({String.Join(',', PlaylistFactory.FormatExtensions)}) will be ignored
- Playlists will be stored in the 'Playlists' folder once exported
- Duplicates will be removed.

Accepted Types:"
            );

            Console.WriteLine(String.Join(',', AudioFactory.FormatExtensions));
            InputImports();
            InputExport();

            Console.WriteLine("Should playlist file listings be updated? [Y/n]");
            if (Console.ReadKey().Key != ConsoleKey.N)
            {
                _updatePlaylists = true;
            }

            Console.WriteLine("\nWorking, please wait...");
            PopulateDirectoryListings();
            var playlists = new List<IPlaylistReader>();
            ReadAudioFiles();
            var audioFiles = new List<TrackFilter>();

            if (_updatePlaylists)
            {
                await ReadPlaylistFiles();
            }

            foreach (var importDirectoriesValue in ImportDirectories.Values)
            {
                audioFiles.AddRange(importDirectoriesValue.Tracks);
                playlists.AddRange(importDirectoriesValue.Playlists);
            }

            var artists = audioFiles.Select(itm => itm.Artist).Distinct().ToList();
            var albums = audioFiles.Select(itm => itm.Album).Distinct().ToList();
            Console.WriteLine($"Found: {artists.Count()} Artists, {albums.Count()} albums, {audioFiles.Count} Files");
            Console.WriteLine($"Found: {playlists.Count()} playlists");


            foreach (var album in albums)
            {
                var filtered = audioFiles.Where(itm => itm.Album == album).ToList();
                filtered.ForEach(itm => audioFiles.Remove(itm));
                var albumArtists = filtered.Select(itm => itm.Artist).Distinct().ToList();
                var artistFolderName =
                    albumArtists.Count() > 1 ? "Various Artists" : albumArtists.SingleOrDefault() ?? "Unknown";

                var fullArtistFolder = Path.Combine(_exportDirectory, artistFolderName);
                var fullAlbumFolder = Path.Combine(fullArtistFolder, album);
                if (!Directory.Exists(fullArtistFolder))
                {
                    Console.WriteLine($" New --> {fullArtistFolder}");
                    Directory.CreateDirectory(fullArtistFolder);
                }

                if (!Directory.Exists(fullAlbumFolder))
                {
                    Console.WriteLine($"New --> {fullAlbumFolder}");
                    Directory.CreateDirectory(fullAlbumFolder);
                }

                var types = filtered.Select(itm => itm.FileType).Distinct().ToList();
                if (types.Count() > 1)
                {
                    foreach (var type in types)
                    {
                        var typePath = Path.Combine(fullAlbumFolder, type);
                        if (!Directory.Exists(typePath))
                        {
                            Console.WriteLine($"New --> {typePath}");
                            Directory.CreateDirectory(typePath);
                        }

                        audioFiles.AddRange(
                            MoveFiles(filtered.Where(itm => itm.FileType == type).ToList(), typePath));
                    }
                }
                else
                {
                    audioFiles.AddRange(MoveFiles(filtered, fullAlbumFolder).ToList());
                }
            }

            var playlistPaths = Path.Combine(_exportDirectory, "_Playlists");
            Console.WriteLine("Creating Playlist Directory");
            if (!Directory.Exists(playlistPaths))
                Directory.CreateDirectory(playlistPaths);

            Console.WriteLine("Updating Playlists");
            foreach (var playlist in playlists)
            {
                var dirs = audioFiles.Select(itm => new {itm.Path, itm.PathUri, itm.UpdatedPath, itm.UpdatedPathUri});
                foreach (var dir in dirs)
                {
                    if (playlist.IsUri)
                    {
                        playlist.UpdateFile(dir.PathUri, dir.UpdatedPathUri);
                    }
                    else
                    {
                        playlist.UpdateFile(dir.Path, dir.UpdatedPath);
                    }
                }

                var newPath = Path.Combine(playlistPaths, Path.GetFileName(playlist.Path));
                if (!File.Exists(newPath))
                {
                    await playlist.Save(newPath);
                }
                else
                {
                    Console.WriteLine(
                        $"{newPath} already exists.\nShould it be Overwritten or save new file under a Different name? [D/o]");
                    var key = Console.ReadKey().Key;
                    Console.WriteLine();
                    if (key == ConsoleKey.O)
                    {
                        await playlist.Save(newPath);
                        try
                        {
                            File.Delete(playlist.Path);
                        }
                        catch
                        {
                            Console.WriteLine($"Couldn't delete {playlist.Path}");
                        }
                    }
                    else
                    {
                        String fileName = null;
                        while (String.IsNullOrWhiteSpace(fileName))
                        {
                            Console.WriteLine("Enter a new file name (without extension): ");
                            fileName = Console.ReadLine().RemoveIllegalChars();
                        }

                        var file = $"{fileName}.{Path.GetExtension(playlist.Path)}";
                        Console.WriteLine($"Saving as: {file}");

                        await playlist.Save(Path.Combine(playlistPaths, file));
                        try
                        {
                            File.Delete(playlist.Path);
                        }
                        catch
                        {
                            Console.WriteLine($"Couldn't delete {playlist.Path}");
                        }
                    }
                }
            }
        }

        private static IEnumerable<TrackFilter> MoveFiles(IEnumerable<TrackFilter> tracks, String directory)
        {
            var trackFilters = tracks as TrackFilter[] ?? tracks.ToArray();
            foreach (var track in trackFilters)
            {
                var fileName = track.Title != "unknown"
                    ? $"{track.Title}.{track.FileType}"
                    : Path.GetFileName(track.Path);
                var absolutePath = Path.Combine(directory, fileName);
                if (track.Path == absolutePath)
                {
                    Console.WriteLine($"Ignoring {fileName}");
                    track.UpdatedPath = absolutePath;
                    continue;
                }
                else if (File.Exists(absolutePath))
                {
                    Console.WriteLine($"File {absolutePath} Exists, overwrite? [N/y]");
                    try
                    {
                        if (Console.ReadKey().Key != ConsoleKey.Y)
                        {
                            track.UpdatedPath = absolutePath;
                            continue;
                        }
                    }
                    finally
                    {
                        Console.WriteLine();
                    }
                }
                else
                {
                    track.UpdatedPath = absolutePath;
                }

                Console.WriteLine($"{track.Path} --> {track.UpdatedPath}");
                File.Copy(track.Path, Path.Combine(track.UpdatedPath));
                try
                {
                    if (track.Path != track.UpdatedPath)
                    {
                        Console.WriteLine($"{track.Path} --> DEL");
                        File.Delete(track.Path);
                    }
                }
                catch
                {
                    Console.WriteLine($"Couldn't delete {track.Path}");
                }
            }

            return trackFilters;
        }

        private static async Task ReadPlaylistFiles()
        {
            foreach (var dirs in ImportDirectories.Values)
            {
                foreach (var file in dirs.files.ToList().Where(itm =>
                    PlaylistFactory.FormatExtensions.Any(itm.EndsWith)))
                {
                    var path = file;
                    if (!path.Contains('.')) continue;

                    try
                    {
                        var pl = PlaylistFactory.GetPlaylistReader(path);
                        await pl.Open();
                        dirs.Playlists.Add(pl);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Error reading playlist file '{Path.GetFileName(path)}' - ignoring.");
                        throw;
                    }
                }
            }
        }

        private static void PopulateDirectoryListings()
        {
            foreach (var key in ImportDirectories.Keys.ToList())
            {
                try
                {
                    ImportDirectories[key].files = Directory.GetFiles(key).ToList();
                }
                catch (UnauthorizedAccessException)
                {
                    Console.WriteLine($"Couldn't access {key}");
                    ImportDirectories.Remove(key);
                }
            }
        }

        private static void ReadAudioFiles()
        {
            foreach (var dirs in ImportDirectories.Values)
            {
                foreach (var file in dirs.files.ToList().Where(itm =>
                    AudioFactory.FormatExtensions.Any(ext => itm.ToLower().EndsWith(ext))))
                {
                    var path = file;

                    if (!path.Contains('.')) continue;

                    try
                    {
                        var track = new Track(path);
                        dirs.Tracks.Add(new TrackFilter()
                        {
                            Path = track.Path,
                            Album = track.Album,
                            Artist = track.Artist,
                            Title = track.Title,
                            BitRate = track.Bitrate
                        });
                    }
                    catch (AudioDataCorruptionException)
                    {
                        Console.WriteLine($"Audio File '{Path.GetFileName(path)}' possibly corrupted - ignoring.");
                    }
                }
            }
        }

        private static void InputExport()
        {
            Console.WriteLine($"\n\nInput the export directory. Leave empty to use the default ({DefaultDirectory})");
            var invalidExport = true;
            while (invalidExport)
            {
                Console.Write("Export Directory: ");
                var input = Console.ReadLine();
                if (input != String.Empty)
                {
                    if (Directory.Exists(input))
                    {
                        _exportDirectory = input;
                        invalidExport = false;
                    }
                    else
                    {
                        Console.WriteLine("Invalid Directory");
                    }
                }
                else
                {
                    _exportDirectory = DefaultDirectory;
                    invalidExport = false;
                }
            }
        }

        private static void InputImports()
        {
            Console.WriteLine(
                $"Input import directories. Leave empty to use default ({DefaultDirectory}) or to finish inputting values.");

            var inputtingImports = true;
            while (inputtingImports)
            {
                Console.Write("Input Directory: ");
                var input = Console.ReadLine();

                if (input != String.Empty)
                {
                    if (Directory.Exists(input))
                    {
                        InsertDirectory(input);
                        ImportConfirmSubDirs(input);
                    }
                    else Console.WriteLine("Invalid Directory");

                    continue;
                }

                inputtingImports = false;
                if (ImportDirectories.Count != 0) continue;
                InsertDirectory(DefaultDirectory);
                ImportConfirmSubDirs(DefaultDirectory);
            }
        }

        private static void InsertDirectory(String input)
        {
            if (!ImportDirectories.ContainsKey(input))
                ImportDirectories.Add(input, new PathUpdate());
        }

        private static void ImportConfirmSubDirs(String input)
        {
            Console.WriteLine("Include subdirectories? [Y/n] ");
            if (Console.ReadKey().Key == ConsoleKey.N) return;

            Console.WriteLine();
            RecurseDirs(input);
        }

        private static void RecurseDirs(String input)
        {
            try
            {
                Directory.GetDirectories(input).ToList().ForEach(itm =>
                {
                    InsertDirectory(itm);
                    RecurseDirs(itm);
                    Console.WriteLine($"+ {itm}");
                });
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"Cannot access {input}");
            }
        }
    }
}