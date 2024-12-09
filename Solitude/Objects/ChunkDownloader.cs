﻿using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using CUE4Parse.Compression;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Readers;
using EpicManifestParser;
using EpicManifestParser.Api;
using EpicManifestParser.UE;
using EpicManifestParser.ZlibngDotNetDecompressor;
using OffiUtils;
using Solitude.Managers;

namespace Solitude.Objects;

public class ChunkDownloader 
{
    private FBuildPatchAppManifest? Manifest { get; set; }

    // https://github.com/4sval/FModel/blob/c014478abc4e455c7116504be92aa00eb00d757b/FModel/ViewModels/CUE4ParseViewModel.cs#L53
    private static readonly Regex PakFinder = new(@"^FortniteGame(/|\\)Content(/|\\)Paks(/|\\)",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private void LoadFileForProvider(FFileManifest file, ref StreamedFileProvider provider)
    {
        if (Manifest is null)
        {
            Log.Error("{FileName} could not be found", file.FileName);
            return;
        }

        var sw = Stopwatch.StartNew();

        if (file.FileName.EndsWith(".utoc"))
        {
            var versions = provider.Versions;

            // https://github.com/4sval/FModel/blob/c014478abc4e455c7116504be92aa00eb00d757b/FModel/ViewModels/CUE4ParseViewModel.cs#L196
            provider.RegisterVfs(file.FileName, [file.GetStream()],
                it => new FRandomAccessStreamArchive(it, Manifest.Files.First(x => x.FileName.Equals(it)).GetStream(), versions));
        }
        else if (file.FileName.EndsWith(".ucas"))
        {
            return;
        }
        else if (file.FileName.EndsWith(".sig"))
        {
            return;
        }
        else
        {
            using var pakStream = file.GetStream();
            provider.RegisterVfs(file.FileName, [pakStream], null);
        }

        var ms = sw.ElapsedMilliseconds;

        Log.Information("Downloaded {FileName} in {Milliseconds} ms", file.FileName, ms);
    }

    public void LoadFileForProvider(string fileName, ref StreamedFileProvider provider)
    {
        var file = Manifest?.Files.First(x => x.FileName == fileName);

        if (file is null)
        {
            Log.Error("{FileName} could not be found", fileName);
            return;
        }

        LoadFileForProvider(file, ref provider);
    }

    public void LoadAllPaksForProvider(ref StreamedFileProvider provider)
    {
        if (Manifest is null)
            return;

        var _provider = provider;

        Parallel.ForEach(Manifest.Files, file =>
        {
            if (!PakFinder.IsMatch(file.FileName) || file.FileName.Contains("optional"))
                return;

            _provider.RegisterVfs(
                file.FileName, [(RandomAccessStream)file.GetStream()],
                it => new FRandomAccessStreamArchive(it, Manifest.Files.First(x => x.FileName.Equals(it)).GetStream(), _provider.Versions));
        });

        provider.Mount();
    }

    public async Task DownloadManifestAsync(ManifestInfo info)
    {
        ManifestParseOptions manifestOptions = new ManifestParseOptions
        {
            ChunkCacheDirectory = DirectoryManager.ChunksDir,
            ManifestCacheDirectory = DirectoryManager.ChunksDir,
            ChunkBaseUrl = "http://epicgames-download1.akamaized.net/Builds/Fortnite/CloudDir/",
            Decompressor = ManifestZlibngDotNetDecompressor.Decompress,
            DecompressorState = ZlibHelper.Instance,
        };

        (Manifest, _) =  await info.DownloadAndParseAsync(manifestOptions).ConfigureAwait(false);
    }
}
