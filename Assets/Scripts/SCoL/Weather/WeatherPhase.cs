namespace SCoL.Weather
{
    /// <summary>
    /// High-level global weather state used by the runtime WeatherSystem.
    /// Thunderstorm is treated as an escalation of Rain (rain + lightning).
    /// </summary>
    public enum WeatherPhase
    {
        // IMPORTANT: keep existing numeric values stable for Unity serialization.
        Clear = 0,
        Rain = 1,
        Thunderstorm = 2,
        Wind = 3,
        Snow = 4,
    }
}
