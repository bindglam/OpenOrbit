namespace OpenOrbit.Core;

public interface IInputEventBus
{
    public event Action Pressed;
    public event Action Released;
}