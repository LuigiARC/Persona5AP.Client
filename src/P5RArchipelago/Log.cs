using Reloaded.Mod.Interfaces;

namespace P5RArchipelago;

internal sealed class Log(ILogger logger)
{
    public void Info(string message)
    {
        logger.Write("[Persona 5 AP] ");
        logger.WriteLine(message);
    }

    public void Error(string message)
    {
        logger.Write("[Persona 5 AP] [ERROR] ");
        logger.WriteLine(message);
    }
}
