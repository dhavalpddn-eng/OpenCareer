namespace OpenCareer.Domain.Simulation;

/// <summary>
/// Small deterministic PRNG for repeatable career simulations.
/// SplitMix64 is used so saves do not depend on System.Random implementation changes.
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
        // 53 random bits mapped to [0, 1).
        return (NextUInt64() >> 11) * (1.0 / (1UL << 53));
    }

    public double NextDouble(double minInclusive, double maxExclusive)
    {
        if (!double.IsFinite(minInclusive)
            || !double.IsFinite(maxExclusive)
            || maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        }

        return minInclusive + (maxExclusive - minInclusive) * NextDouble();
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        }

        var range = (ulong)((long)maxExclusive - minInclusive);
        var threshold = unchecked((0UL - range) % range);

        ulong sample;
        do
        {
            sample = NextUInt64();
        }
        while (sample < threshold);

        return (int)(minInclusive + (long)(sample % range));
    }

    public bool Chance(double probability)
    {
        if (!double.IsFinite(probability) || probability is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(probability));
        }

        return NextDouble() < probability;
    }

    public double NextNormal(double mean = 0.0, double standardDeviation = 1.0)
    {
        if (!double.IsFinite(mean)
            || !double.IsFinite(standardDeviation)
            || standardDeviation < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(standardDeviation));
        }

        if (standardDeviation == 0)
        {
            return mean;
        }

        var u1 = 1.0 - NextDouble();
        var u2 = NextDouble();
        var radius = Math.Sqrt(-2.0 * Math.Log(u1));
        var z = radius * Math.Cos(2.0 * Math.PI * u2);
        return mean + standardDeviation * z;
    }

    public double NextExponential(double ratePerUnit)
    {
        if (!double.IsFinite(ratePerUnit) || ratePerUnit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ratePerUnit));
        }

        return -Math.Log(1.0 - NextDouble()) / ratePerUnit;
    }
}
