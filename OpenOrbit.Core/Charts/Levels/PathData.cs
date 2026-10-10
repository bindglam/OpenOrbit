namespace OpenOrbit.Core.Charts.Levels;

/// <summary>구버전 레벨의 "pathData" 문자열을 angleData 로 변환.</summary>
public static class PathData
{
    private static readonly Dictionary<char, double> AbsoluteAngles = new()
    {
        ['R'] = 0, ['p'] = 15, ['J'] = 30, ['E'] = 45, ['T'] = 60, ['o'] = 75,
        ['U'] = 90, ['q'] = 105, ['G'] = 120, ['Q'] = 135, ['H'] = 150, ['W'] = 165,
        ['L'] = 180, ['x'] = 195, ['N'] = 210, ['Z'] = 225, ['F'] = 240, ['V'] = 255,
        ['D'] = 270, ['Y'] = 285, ['B'] = 300, ['C'] = 315, ['M'] = 330, ['A'] = 345,
        ['!'] = Level.MidspinAngle,
    };

    public static List<double> ToAngles(string pathData)
    {
        var result = new List<double>(pathData.Length);
        foreach (var c in pathData)
        {
            // TODO: '5','6','7','8' 등 직전 타일 기준 상대각 문자 지원
            if (!AbsoluteAngles.TryGetValue(c, out var angle))
                throw new LevelParseException($"지원하지 않는 pathData 문자: '{c}'");
            result.Add(angle);
        }
        return result;
    }
}
