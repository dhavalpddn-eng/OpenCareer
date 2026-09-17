namespace OpenCareer.Domain.Simulation;

/// <summary>
/// Small deterministic PRNG for repeatable career simulations.
/// SplitMix64 is used so test saves do not depend on System.Random implementation changes.
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(ulong seed)
    {
        _state = seed;
    }

    public ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public double NextDouble()
    {
        // 53 random bits mapped to [0,1).
        return (NextUInt64() >> 11) * (1.0 / (1UL << 53));
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        }

        var width = (uint)(maxExclusive - minInclusive);
        return minInclusive + (int)(NextUInt64() % width);
    }

    public bool Chance(double probability)
    {
        if (probability is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(probability));
        }

        return NextDouble() < probability;
    }
}
