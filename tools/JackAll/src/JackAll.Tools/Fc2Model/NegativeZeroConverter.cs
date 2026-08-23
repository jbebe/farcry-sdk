using System.Text.Json;
using System.Text.Json.Serialization;

namespace JackAll.Tools.Fc2Model;

/// <summary>
/// Writes negative zero so that a JSON reader can tell it from a positive one.
/// </summary>
/// <remarks>
/// A number with no decimal point and no exponent is an integer to plenty of readers - Python's is
/// one - and an integer has no negative zero, so <c>-0</c> comes back as <c>0</c>. That is the only
/// value a whole-number float loses on the trip: every other integral one survives being read as an
/// int and written back.
/// <para>
/// It costs three bytes on the AK-47's reload bank and it breaks the property the whole clip writer
/// rests on, which is that a clip nobody edited goes back byte for byte. A quaternion component of
/// <c>-0</c> means the same rotation as <c>0</c> and a different sign bit on disk.
/// </para>
/// </remarks>
public sealed class NegativeZeroConverter : JsonConverter<float>
{
    public override float Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        => reader.GetSingle();

    public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
    {
        if (value == 0f && float.IsNegative(value))
        {
            writer.WriteRawValue("-0.0");
            return;
        }
        writer.WriteNumberValue(value);
    }
}
