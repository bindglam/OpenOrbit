using System.Text.Json;
using System.Text.Json.Nodes;
using OpenOrbit.Core.Charts.Levels.Events;
using OpenOrbit.Core.Charts.Track;
using OpenOrbit.Core.Geometry;

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
            TrackColor = ParseTrackColor(s),
            TrackAnimation = GetEnum(s, "trackAnimation", defaults.TrackAnimation),
            BeatsAhead = GetDouble(s, "beatsAhead", defaults.BeatsAhead),
            TrackDisappearAnimation = GetEnum(s, "trackDisappearAnimation", defaults.TrackDisappearAnimation),
            BeatsBehind = GetDouble(s, "beatsBehind", defaults.BeatsBehind),
            StickToFloors = GetBool(s, "stickToFloors", defaults.StickToFloors),
            CameraRelativeTo = GetEnum(s, "relativeTo", defaults.CameraRelativeTo),
            CameraPosition = GetPair(s, "position") is var (x, y) ? new Vec2(x ?? 0, y ?? 0) : defaults.CameraPosition,
            CameraRotation = GetDouble(s, "rotation", defaults.CameraRotation),
            CameraZoom = GetDouble(s, "zoom", defaults.CameraZoom * 100) / 100,
            LegacyCameraRelativeTo = GetBool(s, "legacyCamRelativeTo", defaults.LegacyCameraRelativeTo),
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
            // 에디터에서 꺼 둔 이벤트는 실행되지 않는다
            if (!GetBool(e, "active", true))
                continue;

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
                "ColorTrack" => new ColorTrackEvent(floor, ParseTrackColorPatch(e), GetBool(e, "justThisTile", false)),
                "RecolorTrack" => new RecolorTrackEvent(
                    floor,
                    GetTileReference(e, "startTile"),
                    GetTileReference(e, "endTile"),
                    (int)GetDouble(e, "gapLength", 0),
                    ParseTrackColorPatch(e),
                    GetDouble(e, "duration", 0),
                    GetEnum(e, "ease", Ease.Linear),
                    GetDouble(e, "angleOffset", 0)),
                "MoveTrack" => ParseMoveTrack(floor, e),
                "MoveCamera" => new MoveCameraEvent(
                    floor,
                    GetDouble(e, "duration", 1),
                    GetNullableEnum<CameraRelativeTo>(e, "relativeTo"),
                    GetPair(e, "position").X,
                    GetPair(e, "position").Y,
                    GetNullableDouble(e, "rotation"),
                    GetNullableDouble(e, "zoom") / 100,
                    GetEnum(e, "ease", Ease.Linear),
                    GetDouble(e, "angleOffset", 0)),
                "AnimateTrack" => new AnimateTrackEvent(
                    floor,
                    GetNullableEnum<TrackAppearAnimation>(e, "trackAnimation"),
                    GetNullableDouble(e, "beatsAhead"),
                    GetNullableEnum<TrackDisappearAnimation>(e, "trackDisappearAnimation"),
                    GetNullableDouble(e, "beatsBehind")),
                "PositionTrack" => new PositionTrackEvent(
                    floor,
                    e.ContainsKey("positionOffset") && GetPair(e, "positionOffset") is var (x, y) ? new Vec2(x ?? 0, y ?? 0) : null,
                    GetTileReference(e, "relativeTo"),
                    GetNullableDouble(e, "rotation"),
                    GetNullableDouble(e, "scale") / 100,
                    GetNullableDouble(e, "opacity") / 100,
                    GetBool(e, "justThisTile", false),
                    GetBool(e, "editorOnly", false)),
                _ => new UnknownEvent(floor, type, e),
            });
        }
        return result;
    }

    private static MoveTrackEvent ParseMoveTrack(int floor, JsonObject e)
    {
        var (x, y) = GetPair(e, "positionOffset");

        // scale 은 숫자(양쪽 같은 값)이거나 [x, y]
        double? scaleX, scaleY;
        if (e["scale"] is JsonArray)
            (scaleX, scaleY) = GetPair(e, "scale");
        else
            scaleX = scaleY = GetNullableDouble(e, "scale");

        return new MoveTrackEvent(
            floor,
            GetTileReference(e, "startTile"),
            GetTileReference(e, "endTile"),
            (int)GetDouble(e, "gapLength", 0),
            GetDouble(e, "duration", 1),
            x,
            y,
            GetNullableDouble(e, "rotationOffset"),
            scaleX / 100,
            scaleY / 100,
            GetNullableDouble(e, "opacity") / 100,
            GetEnum(e, "ease", Ease.Linear),
            GetDouble(e, "angleOffset", 0));
    }

    /// <summary>ColorTrack, RecolorTrack 의 트랙 색. 없는 값은 null (바꾸지 않음).</summary>
    private static TrackColorPatch ParseTrackColorPatch(JsonObject o) => new(
        GetNullableEnum<TrackColorType>(o, "trackColorType"),
        GetString(o, "trackColor") is { } c1 ? Rgba.Parse(c1, Rgba.White) : null,
        GetString(o, "secondaryTrackColor") is { } c2 ? Rgba.Parse(c2, Rgba.White) : null,
        GetNullableDouble(o, "trackColorAnimDuration"),
        GetNullableEnum<TrackColorPulse>(o, "trackColorPulse"),
        GetNullableDouble(o, "trackPulseLength") is { } length ? (int)length : null,
        GetNullableEnum<TrackStyle>(o, "trackStyle"),
        GetNullableDouble(o, "trackGlowIntensity"));

    /// <summary>settings 의 트랙 색. 없는 값은 기본값.</summary>
    private static TrackColor ParseTrackColor(JsonObject o)
    {
        var d = TrackColor.Default;
        return new TrackColor(
            GetEnum(o, "trackColorType", d.Type),
            Rgba.Parse(GetString(o, "trackColor"), d.Primary),
            Rgba.Parse(GetString(o, "secondaryTrackColor"), d.Secondary),
            GetDouble(o, "trackColorAnimDuration", d.AnimDurationSec),
            GetEnum(o, "trackColorPulse", d.Pulse),
            (int)GetDouble(o, "trackPulseLength", d.PulseLength),
            GetEnum(o, "trackStyle", d.Style),
            GetDouble(o, "trackGlowIntensity", d.GlowIntensity));
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
        GetNullableDouble(obj, key) ?? fallback;

    private static double? GetNullableDouble(JsonObject obj, string key) => ToDouble(obj[key]);

    private static double? ToDouble(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out double d) ? d : null;

    /// <summary>[x, y] 배열. 원소가 null 이면 그 축은 바꾸지 않는다는 뜻이다.</summary>
    private static (double? X, double? Y) GetPair(JsonObject obj, string key) =>
        obj[key] is JsonArray { Count: >= 2 } a ? (ToDouble(a[0]), ToDouble(a[1])) : (null, null);

    /// <summary>[offset, "ThisTile" | "Start" | "End"].</summary>
    private static TileReference GetTileReference(JsonObject obj, string key)
    {
        if (obj[key] is not JsonArray { Count: >= 2 } a)
            return new TileReference(0, TileAnchor.ThisTile);
        var anchor = a[1] is JsonValue v && v.TryGetValue(out string? name) && Enum.TryParse<TileAnchor>(name, out var parsed)
            ? parsed
            : TileAnchor.ThisTile;
        return new TileReference((int)(ToDouble(a[0]) ?? 0), anchor);
    }

    /// <summary>true/false 또는 옛 버전의 "Enabled"/"Disabled".</summary>
    private static bool GetBool(JsonObject obj, string key, bool fallback)
    {
        if (obj[key] is not JsonValue v)
            return fallback;
        if (v.TryGetValue(out bool b))
            return b;
        if (v.TryGetValue(out string? s))
            return s switch { "Enabled" => true, "Disabled" => false, _ => fallback };
        return fallback;
    }

    private static T GetEnum<T>(JsonObject obj, string key, T fallback) where T : struct, Enum =>
        GetNullableEnum<T>(obj, key) ?? fallback;

    /// <summary>모르는 이름이면 null (새 버전에서 생긴 값을 만나도 파싱이 실패하지 않게).</summary>
    private static T? GetNullableEnum<T>(JsonObject obj, string key) where T : struct, Enum =>
        GetString(obj, key) is { } name && Enum.TryParse<T>(name, out var value) ? value : null;

    private static string? GetString(JsonObject obj, string key) =>
        obj[key] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
}
