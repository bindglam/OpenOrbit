namespace OpenOrbit.Core.Charts.Levels;

public sealed class LevelParseException(string message, Exception? inner = null) : Exception(message, inner);
