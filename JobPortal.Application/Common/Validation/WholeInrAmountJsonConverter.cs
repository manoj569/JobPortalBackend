using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobPortal.Application.Common.Validation;

public sealed class WholeInrAmountJsonConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.Number)
            throw new JsonException("Salary must be a whole-number INR amount.");
        var span = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan.ToArray();
        if (span.Length == 0 || span.Length > 10 || span.Any(value => value is < (byte)'0' or > (byte)'9'))
            throw new JsonException("Salary must be a non-negative whole-number INR amount up to 1000000000.");
        if (!reader.TryGetDecimal(out var amount) || amount > 1000000000)
            throw new JsonException("Salary exceeds the permitted maximum of 1000000000 INR.");
        return amount;
    }

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}
