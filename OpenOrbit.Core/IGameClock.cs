namespace OpenOrbit.Core;

public interface IGameClock
{
    public bool Running { get; }
    public double NowMs { get; }

    public void Start();

    public void Stop();
}