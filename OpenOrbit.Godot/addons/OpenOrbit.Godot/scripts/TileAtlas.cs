using System;
using Godot;

namespace OpenOrbit.Godot;

/// <summary>
/// 타일 이미지(arm, joint, midspin)와 arm, joint 의 "채움" 버전을 텍스처 한 장에 모은 아틀라스.
/// 한 텍스처만 쓰면 Godot 가 타일 여러 개를 한 번의 드로우 콜로 묶어 그릴 수 있다.
/// <para>
/// 채움 버전은 원본에서 어두운 테두리 픽셀을 투명하게 만든 것이다. 타일을 "원본 → 채움" 순서로 두 번 그리면
/// 팔과 중앙 원이 겹치는 안쪽 테두리가 채움에 덮여서, 타일 하나의 바깥 테두리만 남는다.
/// </para>
/// </summary>
public sealed class TileAtlas
{
    private const int Padding = 32; // 밉맵을 만들어도 옆 영역 색이 번지지 않을 만큼

    public Texture2D Texture { get; }
    public Rect2 Arm { get; }
    public Rect2 ArmFill { get; }
    public Rect2 Joint { get; }
    public Rect2 JointFill { get; }
    public Rect2 Midspin { get; }

    private TileAtlas(Texture2D texture, Rect2[] regions)
    {
        Texture = texture;
        (Arm, ArmFill, Joint, JointFill, Midspin) = (regions[0], regions[1], regions[2], regions[3], regions[4]);
    }

    public static TileAtlas Load(string armPath, string jointPath, string midspinPath)
    {
        var arm = LoadImage(armPath);
        var joint = LoadImage(jointPath);
        var midspin = LoadImage(midspinPath);
        Image[] parts = [arm, ExtractFill(arm), joint, ExtractFill(joint), midspin];

        var width = Padding;
        var height = 0;
        foreach (var part in parts)
        {
            width += part.GetWidth() + Padding;
            height = Math.Max(height, part.GetHeight());
        }

        var atlas = Image.CreateEmpty(width, height + Padding * 2, false, Image.Format.Rgba8);
        var regions = new Rect2[parts.Length];
        var x = Padding;
        for (var i = 0; i < parts.Length; i++)
        {
            var size = parts[i].GetSize();
            atlas.BlitRect(parts[i], new Rect2I(Vector2I.Zero, size), new Vector2I(x, Padding));
            regions[i] = new Rect2(x, Padding, size.X, size.Y);
            x += size.X + Padding;
        }

        // 화면에서는 원본보다 훨씬 작게 그려지므로 밉맵이 없으면 테두리가 깨진다
        atlas.GenerateMipmaps();
        return new TileAtlas(ImageTexture.CreateFromImage(atlas), regions);
    }

    private static Image LoadImage(string path)
    {
        var image = GD.Load<Texture2D>(path).GetImage();
        if (image.IsCompressed())
            image.Decompress();
        image.ClearMipmaps();
        image.Convert(Image.Format.Rgba8);
        return image;
    }

    /// <summary>밝은 부분만 남기고 어두운 테두리는 투명하게. 경계는 밝기에 따라 부드럽게 처리.</summary>
    private static Image ExtractFill(Image source)
    {
        var data = source.GetData();
        for (var i = 0; i < data.Length; i += 4)
        {
            var luminance = (data[i] + data[i + 1] + data[i + 2]) / (3f * 255f);
            var coverage = Math.Clamp((luminance - 0.3f) / 0.4f, 0f, 1f);
            data[i] = data[i + 1] = data[i + 2] = 255;
            data[i + 3] = (byte)(data[i + 3] * coverage);
        }
        return Image.CreateFromData(source.GetWidth(), source.GetHeight(), false, Image.Format.Rgba8, data);
    }
}
