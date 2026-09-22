using System.Globalization;
using System.Text;

namespace JackAll.Core.Format.Move.Rules;

public enum MoveConditionOp
{
    Is,
    IsNot,
    Between,
    Chance,
}

/// <summary>A test to build or rewrite a criterion from; a range keeps its bounds in stored units.</summary>
/// <param name="Value">The compared value, or a range's lower bound, or a chance's percentage.</param>
/// <param name="IsOr">Joins this test to the ones before it with "or" instead of "and".</param>
public sealed record MoveConditionSpec(
    int Channel, MoveConditionOp Op, double Value, double High = 0, bool IsOr = false);

/// <summary>One criterion, read as a test of one value channel.</summary>
public sealed class MoveCondition(MoveObject criterion)
{
    public MoveObject Criterion { get; } = criterion;

    public int Channel => (int)(Criterion.Field("m_eValueID") ?? 0);

    public bool IsOr => Criterion.Field("m_logicOperator") == 1;

    public MoveConditionOp Op => OpOf(Criterion.ClassName);

    public MoveConditionSpec Spec => Op switch
    {
        MoveConditionOp.Between => new(Channel, Op, Number("m_LowerBound"), Number("m_UpperBound"), IsOr),
        MoveConditionOp.Chance => new(Channel, Op, Number("m_uchPercentage"), 0, IsOr),
        _ => new(Channel, Op, Number("m_Value"), 0, IsOr),
    };

    /// <summary>The criteria a descriptor's own list holds, in the order the engine tests them.</summary>
    public static IReadOnlyList<MoveCondition> Of(MoveObject descriptor)
        => [.. descriptor.Ops
            .Where(op => op.Name == "CMoveCriteria" && op.Target is not null)
            .Select(op => new MoveCondition(op.Target!))];

    public static MoveConditionOp OpOf(string className) => className switch
    {
        _ when className.Contains("Intv", StringComparison.Ordinal) => MoveConditionOp.Between,
        _ when className.Contains("Perc", StringComparison.Ordinal) => MoveConditionOp.Chance,
        _ when className.Contains("NotEqual", StringComparison.Ordinal) => MoveConditionOp.IsNot,
        _ => MoveConditionOp.Is,
    };

    /// <summary>Whether a channel value passes, or null when the test is a dice roll.</summary>
    public bool? Test(double value)
    {
        MoveConditionSpec spec = Spec;
        switch (spec.Op)
        {
            case MoveConditionOp.Is:
                return value == spec.Value;
            case MoveConditionOp.IsNot:
                return value != spec.Value;
            case MoveConditionOp.Between when spec.Value <= spec.High:
                bool inclusive = Criterion.Field("m_inclusive") != 0;
                return value >= spec.Value && (inclusive ? value <= spec.High : value < spec.High);
            case MoveConditionOp.Between:
                // An angle range that wraps through +-pi, such as "behind": 135 to -135 degrees.
                return value >= spec.Value || value <= spec.High;
            default:
                return null;
        }
    }

    public string Describe(MoveChannels channels)
    {
        MoveConditionSpec spec = Spec;
        string name = channels.NameOf(spec.Channel);
        return spec.Op switch
        {
            MoveConditionOp.Is when channels.TypeOf(spec.Channel) == MoveValueType.Bool
                => spec.Value != 0 ? name : "not " + name,
            MoveConditionOp.Is => $"{name} = {channels.Format(spec.Channel, spec.Value)}",
            MoveConditionOp.IsNot => $"{name} ≠ {channels.Format(spec.Channel, spec.Value)}",
            MoveConditionOp.Between
                => $"{name} {channels.Format(spec.Channel, spec.Value)}…{channels.Format(spec.Channel, spec.High)}",
            _ => $"{spec.Value.ToString(CultureInfo.InvariantCulture)}% chance",
        };
    }

    /// <summary>A criteria list as one expression, bracketed where it mixes "and" and "or".</summary>
    public static string Describe(IReadOnlyList<MoveCondition> list, MoveChannels channels)
    {
        StringBuilder text = new();
        for (int i = 0; i < list.Count; i++)
        {
            if (i == 0)
            {
                text.Append(list[0].Describe(channels));
                continue;
            }

            string join = list[i].IsOr ? " or " : " and ";
            if (list[i].IsOr != list[i - 1].IsOr && i > 1)
            {
                text.Insert(0, '(').Append(')');
            }

            text.Append(join).Append(list[i].Describe(channels));
        }

        return text.ToString();
    }

    private double Number(string field)
    {
        if (Criterion.FieldF32(field) is { } real)
        {
            return real;
        }

        MoveOp op = Criterion.Ops.FirstOrDefault(o => o.Name == field);
        return op.Kind == MoveOpKind.S32 ? unchecked((int)op.Number) : op.Number;
    }
}

/// <summary>Builds and rewrites criteria in the layout, and at the versions, the shipped graphs use.</summary>
internal static class MoveCriteria
{
    private const uint CriteriaVersion = 4;
    private const uint EnumVersion = 1;
    private const uint IntervalVersion = 2;
    private const uint ObjectVersion = 1;

    /// <summary>The class that tests a channel of this type with this operator.</summary>
    public static string ClassFor(MoveValueType type, MoveConditionOp op) => (type, op) switch
    {
        (MoveValueType.Enum, MoveConditionOp.Is) => "CMoveCriteriaEnumEqual",
        (MoveValueType.Enum, MoveConditionOp.IsNot) => "CMoveCriteriaEnumNotEqual",
        (MoveValueType.Bool, MoveConditionOp.Is) => "TMoveCriteriaEqual<bool>",
        (MoveValueType.Int, MoveConditionOp.Is) => "TMoveCriteriaEqual<int>",
        (MoveValueType.Int, MoveConditionOp.IsNot) => "TMoveCriteriaNotEqual<int>",
        (MoveValueType.Int, MoveConditionOp.Between) => "TMoveCriteriaIntv<int>",
        (MoveValueType.UInt8, MoveConditionOp.Is) => "TMoveCriteriaEqual<uint8>",
        (MoveValueType.UInt8, MoveConditionOp.IsNot) => "TMoveCriteriaNotEqual<uint8>",
        (MoveValueType.Float, MoveConditionOp.Between) => "TMoveCriteriaIntv<float>",
        (MoveValueType.Angle, MoveConditionOp.Between) => "TMoveCriteriaIntv<CAngle>",
        _ => throw new MoveFormatException(
            $"a {type} channel cannot be tested with '{op}'"),
    };

    /// <summary>
    /// Rewrites <paramref name="criterion"/> to test <paramref name="spec"/>, or returns false when
    /// its class cannot express it and a new criterion has to replace it.
    /// </summary>
    public static bool TryWrite(MoveObject criterion, MoveConditionSpec spec, MoveValueType type)
    {
        string wanted = type == MoveValueType.Unknown || spec.Op == MoveConditionOp.Chance
            ? criterion.ClassName
            : ClassFor(type, spec.Op);
        if (MoveCondition.OpOf(criterion.ClassName) != spec.Op || Encoding(criterion.ClassName) != Encoding(wanted))
        {
            return false;
        }

        Write(criterion, "m_eValueID", spec.Channel);
        Write(criterion, "m_logicOperator", spec.IsOr ? 1 : 0);
        Write(criterion, "m_Value", spec.Value);
        Write(criterion, "m_LowerBound", spec.Value);
        Write(criterion, "m_uchPercentage", spec.Value);
        Write(criterion, "m_UpperBound", spec.High);
        return true;

        static void Write(MoveObject obj, string field, double value)
        {
            if (!obj.SetFieldF32(field, (float)value))
            {
                obj.SetField(field, unchecked((uint)(int)Math.Round(value)));
            }
        }
    }

    public static MoveObject Build(MoveConditionSpec spec, MoveValueType type)
    {
        MoveObject criterion = new(ClassFor(type, spec.Op));
        List<MoveOp> ops = criterion.Ops;
        switch (criterion.ClassName)
        {
            case "CMoveCriteriaEnumEqual" or "CMoveCriteriaEnumNotEqual":
                ops.Add(MoveOp.Integer(MoveOpKind.Version, "CMoveCriteriaEnum", EnumVersion));
                ops.Add(MoveOp.Integer(MoveOpKind.S32, "m_Value", 0));
                break;
            case "TMoveCriteriaEqual<int>" or "TMoveCriteriaNotEqual<int>":
                ops.Add(MoveOp.Integer(MoveOpKind.S32, "m_Value", 0));
                break;
            case "TMoveCriteriaIntv<int>":
                ops.Add(MoveOp.Integer(MoveOpKind.Version, "TMoveCriteriaIntv", IntervalVersion));
                ops.Add(MoveOp.Integer(MoveOpKind.S32, "m_LowerBound", 0));
                ops.Add(MoveOp.Integer(MoveOpKind.S32, "m_UpperBound", 0));
                ops.Add(MoveOp.Integer(MoveOpKind.U8, "m_inclusive", 1));
                break;
            case "TMoveCriteriaIntv<float>":
                ops.Add(MoveOp.Integer(MoveOpKind.Version, "TMoveCriteriaIntv", IntervalVersion));
                ops.Add(Float("m_LowerBound"));
                ops.Add(Float("m_UpperBound"));
                ops.Add(MoveOp.Integer(MoveOpKind.U8, "m_inclusive", 1));
                break;
            case "TMoveCriteriaIntv<CAngle>":
                ops.Add(Float("m_LowerBound"));
                ops.Add(Float("m_UpperBound"));
                break;
            default:
                ops.Add(MoveOp.Integer(MoveOpKind.U8, "m_Value", 0));
                break;
        }

        ops.Add(MoveOp.Integer(MoveOpKind.Version, "CMoveCriteria", CriteriaVersion));
        ops.Add(MoveOp.Integer(MoveOpKind.U8, "m_eValueID", 0));
        ops.Add(MoveOp.Integer(MoveOpKind.Version, "CMoveObject", ObjectVersion));
        ops.Add(MoveOp.Integer(MoveOpKind.S32, "m_logicOperator", 0));
        TryWrite(criterion, spec, type);
        return criterion;

        static MoveOp Float(string name) => MoveOp.Blob(MoveOpKind.F32, name, new byte[4]);
    }

    /// <summary>How a class stores its compared value, which is what an in-place rewrite must keep.</summary>
    private static string Encoding(string className) => className switch
    {
        _ when className.StartsWith("CMoveCriteriaEnum", StringComparison.Ordinal) => "s32",
        _ when className.EndsWith("<int>", StringComparison.Ordinal) => "s32",
        _ when className.EndsWith("<float>", StringComparison.Ordinal) => "f32",
        _ when className.EndsWith("<CAngle>", StringComparison.Ordinal) => "angle",
        _ => "u8",
    };
}
