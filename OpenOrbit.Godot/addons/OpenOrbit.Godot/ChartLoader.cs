using System;
using Godot;
using OpenOrbit.Core.Charts;
using OpenOrbit.Core.Charts.Levels;
using FileAccess = Godot.FileAccess;

namespace OpenOrbit.Godot;

public sealed record LoadedChart(Chart Chart, AudioStream? Song, string Name);

/// <summary>.adofai 와 곡 파일을 Godot 파일 시스템(res://, user://)에서 읽는다.</summary>
public static class ChartLoader
{
    public static LoadedChart Load(string adofaiPath)
    {
        var bytes = FileAccess.GetFileAsBytes(adofaiPath);
        if (bytes.Length == 0)
            throw new InvalidOperationException($"차트 파일을 읽을 수 없습니다: {adofaiPath} ({FileAccess.GetOpenError()})");

        var level = LevelParser.Parse(bytes);
        var chart = ChartCompiler.Compile(level);

        var songFile = level.Settings.SongFilename;
        var song = string.IsNullOrEmpty(songFile) ? null : LoadAudio(adofaiPath.GetBaseDir().PathJoin(songFile));
        if (song is null)
            GD.PushWarning($"곡 파일을 불러오지 못했습니다: {songFile}. 음악 없이 진행합니다.");

        return new LoadedChart(chart, song, adofaiPath.GetFile().GetBaseName());
    }

    private static AudioStream? LoadAudio(string path)
    {
        // 에디터가 import 한 리소스가 있으면 그걸 쓰고, 없으면(user:// 등) 원본 파일을 직접 디코딩
        if (ResourceLoader.Exists(path))
            return GD.Load<AudioStream>(path);

        var bytes = FileAccess.GetFileAsBytes(path);
        if (bytes.Length == 0)
            return null;

        return path.GetExtension().ToLowerInvariant() switch
        {
            "ogg" => AudioStreamOggVorbis.LoadFromBuffer(bytes),
            "mp3" => new AudioStreamMP3 { Data = bytes },
            "wav" => AudioStreamWav.LoadFromBuffer(bytes),
            _ => null,
        };
    }
}
