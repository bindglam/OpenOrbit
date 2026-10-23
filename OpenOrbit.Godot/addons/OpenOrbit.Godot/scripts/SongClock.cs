using System;
using Godot;
using OpenOrbit.Core;

namespace OpenOrbit.Godot;

/// <summary>
/// 곡 기준 시각(ms). 프레임 단위로 뚝뚝 끊기는 오디오 재생 위치 대신 시스템 시계로 부드럽게 진행하고,
/// 오디오 재생 위치 쪽으로 조금씩 보정한다. 입력 시각도 이 시계로 재므로 프레임레이트와 무관하게 정확하다.
/// </summary>
public sealed class SongClock(AudioStreamPlayer player) : IGameClock
{
    private ulong _startUsec;
    private double _correctionMs;

    public bool Running { get; private set; }

    public double NowMs => Running ? (Time.GetTicksUsec() - _startUsec) / 1000.0 + _correctionMs : 0;

    public void Start()
    {
        if (player.Stream is not null)
            player.Play();
        _startUsec = Time.GetTicksUsec();
        _correctionMs = 0;
        Running = true;
    }

    /// <summary>아무 스레드에서나 호출할 수 있다 (게임 엔진은 업데이트 스레드에서 실패 처리를 한다).</summary>
    public void Stop()
    {
        Running = false;
        if (OS.GetThreadCallerId() == OS.GetMainThreadId())
            player.Stop();
        else // 노드는 메인 스레드에서만 다룰 수 있다. 그 사이 다시 시작했으면 멈추지 않는다.
            Callable.From(() => { if (!Running) player.Stop(); }).CallDeferred();
    }

    /// <summary>매 프레임 호출. 곡이 재생 중이면 오디오 위치와의 오차를 줄인다.</summary>
    public void Sync()
    {
        if (!Running || !player.Playing)
            return;

        var audioSec = player.GetPlaybackPosition() + AudioServer.GetTimeSinceLastMix() - AudioServer.GetOutputLatency();
        var audioMs = audioSec * 1000.0 / player.PitchScale;
        var drift = audioMs - NowMs;

        // 크게 어긋나면(시작 직후, 프레임 멈춤) 바로 맞추고, 아니면 지터가 안 보이게 천천히
        _correctionMs += Math.Abs(drift) > 50 ? drift : drift * 0.05;
    }
}
