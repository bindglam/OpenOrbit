using System;
using System.Collections.Generic;
using Godot;
using OpenOrbit.Core.Charts;

namespace OpenOrbit.Godot;

/// <summary>
/// 히트사운드를 타일 시각에 샘플 단위로 정확히 합성해서 내보낸다.
/// <para>
/// AudioStreamPlayer.Play() 로 소리를 내면 프레임마다 한 번밖에 낼 수 없어서, 판정 간격이 한 프레임보다 짧은
/// 고BPM 구간에서는 소리가 뭉개진다. 얼불춤처럼 히트사운드를 채보 시각에 미리 배치해 두면, 소리가 아주 촘촘해질 때
/// 히트 빈도 자체가 음높이로 들린다 (체감 BPM 이 높을수록 높은 음).
/// </para>
/// <para>
/// 곡과 같은 프레임에 재생을 시작하면 둘 다 같은 오디오 스레드에서 섞이므로, 생성기의 n 번째 샘플이 곧 곡 시작 후
/// n / mixRate 초 지점이다. 입력 판정과 상관없이 채보 시각에 울린다 (얼불춤과 같은 방식).
/// 미리 알고 있는 시각에 배치하므로 버퍼를 길게 잡아도 지연이 생기지 않고, 프레임이 멈춰 버퍼가 비더라도
/// 곡 재생 위치에 다시 맞춘다.
/// </para>
/// </summary>
public sealed class HitsoundMixer
{
    private const float BufferSeconds = 1f; // 프레임이 이만큼 멈춰도 소리가 끊기지 않는다
    private const double ResyncSeconds = 0.03; // 곡과 이보다 많이 어긋나면 다시 맞춘다
    private const int MaxVoices = 32; // 이보다 많이 겹치면 가장 오래된 소리를 끊는다

    private readonly AudioStreamPlayer _player;
    private readonly AudioStreamPlayer _music;
    private readonly double _mixRate;
    private readonly Vector2[] _sample;

    private AudioStreamGeneratorPlayback? _playback;
    private int _capacity; // 생성기 버퍼 전체 크기 (프레임)
    private long[] _hits = [];
    private int _firstActive; // 아직 울리고 있을 수 있는 첫 히트
    private long _cursor; // 다음에 써 넣을 샘플 위치 (곡 시작 = 0)
    private Vector2[] _block = [];

    /// <summary>player 에 지정된 소리를 히트사운드로 쓰고, player 는 합성 출력용으로 바꾼다. music 은 박자 기준.</summary>
    public HitsoundMixer(AudioStreamPlayer player, AudioStreamPlayer music)
    {
        _player = player;
        _music = music;
        _mixRate = AudioServer.GetMixRate();
        _sample = Decode(player.Stream, _mixRate);
        player.Stream = new AudioStreamGenerator { MixRate = (float)_mixRate, BufferLength = BufferSeconds };
    }

    /// <summary>곡 재생을 시작한 것과 같은 프레임에 호출해야 박자가 맞는다.</summary>
    public void Start(Chart chart)
    {
        var hits = new List<long>(chart.Tiles.Count);
        for (var i = 1; i < chart.Tiles.Count; i++)
        {
            if (!chart.Tiles[i].IsMidspin)
                hits.Add((long)Math.Round(chart.Tiles[i].TimeMs / 1000.0 * _mixRate));
        }
        _hits = hits.ToArray();
        Array.Sort(_hits);
        _firstActive = 0;
        _cursor = 0;

        _player.Play();
        _playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
        _capacity = _playback.GetFramesAvailable();
        Fill(); // 재생이 실제로 시작되기 전에 버퍼를 채워 둬야 곡과 같은 시점에서 출발한다
    }

    public void Stop()
    {
        _player.Stop();
        _playback = null;
    }

    /// <summary>매 프레임 호출. 생성기 버퍼의 빈 만큼 다음 구간을 합성해서 밀어 넣는다.</summary>
    public void Fill()
    {
        if (_playback is null)
            return;

        var frames = _playback.GetFramesAvailable();
        if (frames <= 0)
            return;

        // 버퍼 끝에 쓰는 샘플은 (곡의 현재 위치 + 버퍼에 남은 양) 시점에 들린다. 어긋났으면 그 위치로 옮긴다.
        if (_music.Playing)
        {
            var expected = (long)Math.Round(_music.GetPlaybackPosition() / _music.PitchScale * _mixRate) + (_capacity - frames);
            if (Math.Abs(expected - _cursor) > ResyncSeconds * _mixRate)
                Seek(expected);
        }

        if (_block.Length < frames)
            _block = new Vector2[frames];
        var block = _block.AsSpan(0, frames);
        block.Clear();
        Mix(block);
        _playback.PushBuffer(block);
        _cursor += frames;
    }

    private void Seek(long cursor)
    {
        _cursor = cursor;
        // 아직 울리고 있을 수 있는 첫 히트(시작 > cursor - 소리 길이)를 다시 찾는다
        var index = Array.BinarySearch(_hits, cursor - _sample.Length + 1);
        _firstActive = index >= 0 ? index : ~index;
    }

    private void Mix(Span<Vector2> block)
    {
        var blockStart = _cursor;
        var blockEnd = _cursor + block.Length;
        var length = _sample.Length;

        while (_firstActive < _hits.Length && _hits[_firstActive] + length <= blockStart)
            _firstActive++;

        for (var k = _firstActive; k < _hits.Length && _hits[k] < blockEnd; k++)
        {
            // 이 히트가 울리는 구간: 소리가 끝나거나, MaxVoices 번째 뒤 히트가 시작해 끊길 때까지
            var start = _hits[k];
            var end = start + length;
            if (k + MaxVoices < _hits.Length)
                end = Math.Min(end, _hits[k + MaxVoices]);

            var from = Math.Max(start, blockStart);
            var to = Math.Min(end, blockEnd);
            for (var t = from; t < to; t++)
                block[(int)(t - blockStart)] += _sample[t - start];
        }

        // 많이 겹치면 진폭이 커지므로 부드럽게 눌러서 찢어지는 소리를 막는다 (작은 소리는 거의 그대로)
        for (var i = 0; i < block.Length; i++)
            block[i] = new Vector2(MathF.Tanh(block[i].X), MathF.Tanh(block[i].Y));
    }

    /// <summary>mp3/ogg/wav 무엇이든 출력 샘플레이트의 PCM 으로 디코딩.</summary>
    private static Vector2[] Decode(AudioStream stream, double mixRate)
    {
        var playback = stream.InstantiatePlayback();
        playback.Start(0);
        var seconds = stream.GetLength() > 0 ? stream.GetLength() : 0.5;
        return playback.MixAudio(1f, (int)Math.Ceiling(seconds * mixRate));
    }
}
