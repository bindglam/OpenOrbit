namespace OpenOrbit.Core.Charts.Track;

/// <summary>타일이 나타나는 애니메이션 (settings.trackAnimation, AnimateTrack).</summary>
public enum TrackAppearAnimation
{
    None,
    Assemble,
    Assemble_Far,
    Extend,
    Grow,
    Grow_Spin,
    Fade,
    Drop,
    Rise,
}

/// <summary>타일이 사라지는 애니메이션 (settings.trackDisappearAnimation, AnimateTrack).</summary>
public enum TrackDisappearAnimation
{
    None,
    Scatter,
    Scatter_Far,
    Retract,
    Shrink,
    Shrink_Spin,
    Fade,
}
