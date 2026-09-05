namespace BetterDeaths.DamageParsing;

using System.Globalization;
using System.Text;
using Lumina.Text.Expressions;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;

internal static class ActionPotencyTextResolver
{
    public static string Resolve(ReadOnlySeString description, uint classJobId, byte level)
    {
        var text = new StringBuilder();
        AppendText(text, description, classJobId, level, 0);
        return text.ToString();
    }

    private static void AppendText(StringBuilder text, ReadOnlySeString description, uint job, byte level, int depth)
    {
        if (depth > 16)
        {
            text.Append('?');
            return;
        }

        foreach (var payload in description)
        {
            if (payload.Type == ReadOnlySePayloadType.Text)
            {
                text.Append(Encoding.UTF8.GetString(payload.Body.Span));
                continue;
            }

            switch (payload.MacroCode)
            {
                case MacroCode.NewLine:
                    text.Append('\n');
                    break;
                case MacroCode.Color:
                case MacroCode.EdgeColor:
                case MacroCode.ShadowColor:
                case MacroCode.ColorType:
                case MacroCode.EdgeColorType:
                case MacroCode.Bold:
                case MacroCode.Italic:
                    break;
                case MacroCode.If when payload.TryGetExpression(out var condition, out var yes, out var no) &&
                    TryGetNumber(condition, job, level, depth + 1, out var result):
                    AppendValue(text, result != 0 ? yes : no, job, level, depth + 1);
                    break;
                case MacroCode.Num when payload.TryGetExpression(out var number):
                    AppendValue(text, number, job, level, depth + 1);
                    break;
                default:
                    // Do not let an unknown game-state expression become a guessed potency.
                    text.Append('?');
                    break;
            }
        }
    }

    private static void AppendValue(StringBuilder text, ReadOnlySeExpression expression, uint job, byte level, int depth)
    {
        if (expression.TryGetString(out var nested))
        {
            AppendText(text, nested, job, level, depth);
        }
        else if (TryGetNumber(expression, job, level, depth, out var value))
        {
            text.Append(value.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            text.Append('?');
        }
    }

    private static bool TryGetNumber(ReadOnlySeExpression expression, uint job, byte level, int depth, out uint value)
    {
        value = 0;
        if (depth > 16)
        {
            return false;
        }
        if (expression.TryGetUInt(out value))
        {
            return true;
        }
        if (expression.TryGetParameterExpression(out var parameterType, out var parameter) &&
            parameterType == (byte)ExpressionType.GlobalNumber && parameter.TryGetUInt(out var index))
        {
            value = index switch { 68 => job, 72 => level, _ => 0 };
            return value != 0;
        }
        if (!expression.TryGetBinaryExpression(out var operation, out var left, out var right) ||
            !TryGetNumber(left, job, level, depth + 1, out var a) ||
            !TryGetNumber(right, job, level, depth + 1, out var b))
        {
            return false;
        }

        bool? comparison = (ExpressionType)operation switch
        {
            ExpressionType.Equal => a == b,
            ExpressionType.NotEqual => a != b,
            ExpressionType.GreaterThanOrEqualTo => a >= b,
            ExpressionType.GreaterThan => a > b,
            ExpressionType.LessThanOrEqualTo => a <= b,
            ExpressionType.LessThan => a < b,
            _ => null,
        };
        value = comparison == true ? 1u : 0u;
        return comparison is not null;
    }
}
