namespace OpenCareer.Domain.Aircraft;

/// <summary>Permanent OpenCareer identity of one physical aircraft, not a model or ownership tenure.</summary>
public readonly record struct AirframeId
{
    public AirframeId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Airframe identity cannot be empty.", nameof(value));

        Value = value;
    }

    public Guid Value { get; }

    public static AirframeId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new AirframeId(Guid.ParseExact(value, "D"));
    }

    // A value type's default value must also fail at every authority boundary.
    public void Validate()
    {
        if (Value == Guid.Empty)
            throw new ArgumentException("Airframe identity cannot be empty.");
    }

    public override string ToString() => Value.ToString("D");
}
