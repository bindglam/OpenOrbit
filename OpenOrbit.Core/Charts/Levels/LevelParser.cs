using System.Text.Json;
using System.Text.Json.Nodes;
using OpenOrbit.Core.Charts.Levels.Events;

namespace OpenOrbit.Core.Charts.Levels;

/// <summary>
/// .adofai(JSON) → <see cref="Level"/>.
/// NativeAOT 호환을 위해 리플렉션 직렬화 대신 JsonNode 로 직접 읽는다.
/// </summary>
public static class LevelParser
{
    // 얼불춤 레벨 파일은 BOM, 후행 쉼표가 흔하다
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static Level ParseFile(string path) => Parse(File.ReadAllBytes(path));

    public static Level Parse(string json) => Parse(System.Text.Encoding.UTF8.GetBytes(json));

    public static Level Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.StartsWith("﻿"u8))
            utf8 = utf8[3..];

        JsonObject root;
        try
        {
            root = JsonNode.Parse(utf8, documentOptions: DocumentOptions) as JsonObject
                   ?? throw new LevelParseException("최상위 JSON 이 객체가 아닙니다.");
        }
        catch (JsonException e)
        {
            throw new LevelParseException($"JSON 파싱 실패: {e.Message}", e);
        }

        return new Level
        {
            Settings = ParseSettings(root["settings"] as JsonObject),
            Angles = ParseAngles(root),
            Events = ParseEvents(root["actions"] as JsonArray),
        };
    }

    private static LevelSettings ParseSettings(JsonObject? s)
    {
        if (s is null)
            return new LevelSettings();

        var defaults = new LevelSettings();
        return new LevelSettings
        {
            Bpm = GetDouble(s, "bpm", defaults.Bpm),
            OffsetMs = GetDouble(s, "offset", defaults.OffsetMs),
            Pitch = GetDouble(s, "pitch", defaults.Pitch),
            CountdownTicks = (int)GetDouble(s, "countdownTicks", defaults.CountdownTicks),
            SongFilename = GetString(s, "songFilename") ?? defaults.SongFilename,
        };
    }

    private static List<double> ParseAngles(JsonObject root)
    {
        if (root["angleData"] is JsonArray angleData)
        {
            var result = new List<double>(angleData.Count);
            foreach (var node in angleData)
            {
                if (node is not JsonValue v || !v.TryGetValue(out double angle))
                    throw new LevelParseException("angleData 에 숫자가 아닌 값이 있습니다.");
                result.Add(angle);
            }
            return result;
        }

        if (GetString(root, "pathData") is { } pathData)
            return PathData.ToAngles(pathData);

        throw new LevelParseException("angleData 또는 pathData 가 없습니다.");
    }

    private static List<LevelEvent> ParseEvents(JsonArray? actions)
    {
        var result = new List<LevelEvent>();
        if (actions is null)
            return result;

        foreach (var node in actions)
        {
            if (node is not JsonObject e)
                continue;

            var floor = (int)GetDouble(e, "floor", -1);
            var type = GetString(e, "eventType") ?? "";
            if (floor < 0)
                throw new LevelParseException($"floor 가 없는 이벤트: {type}");

            result.Add(type switch
            {
                "SetSpeed" => new SetSpeedEvent(
                    floor,
                    GetString(e, "speedType") == "Multiplier" ? SpeedType.Multiplier : SpeedType.Bpm,
                    GetDouble(e, "beatsPerMinute", 100),
                    GetDouble(e, "bpmMultiplier", 1)),
                "Twirl" => new TwirlEvent(floor),
                "Pause" => new PauseEvent(floor, GetDouble(e, "duration", 1)),
                "Hold" => new HoldEvent(floor, (int)GetDouble(e, "duration", 0)),
                "MultiPlanet" => new MultiPlanetEvent(floor, GetPlanets(e)),
                _ => new UnknownEvent(floor, type, e),
            });
        }
        return result;
    }

    /// <summary>"planets" 는 숫자이거나 "TwoPlanets", "ThreePlanets" 같은 이름. 얼불춤처럼 2~8 개로 제한.</summary>
    private static int GetPlanets(JsonObject e)
    {
        var planets = GetString(e, "planets") switch
        {
            "TwoPlanets" => 2,
            "ThreePlanets" => 3,
            "FourPlanets" => 4,
            "FivePlanets" => 5,
            "SixPlanets" => 6,
            "SevenPlanets" => 7,
            "EightPlanets" => 8,
            _ => (int)GetDouble(e, "planets", 2),
        };
        return Math.Clamp(planets, 2, 8);
    }

    private static double GetDouble(JsonObject obj, string key, double fallback) =>
        obj[key] is JsonValue v && v.TryGetValue(out double d) ? d : fallback;

    private static string? GetString(JsonObject obj, string key) =>
        obj[key] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
}
