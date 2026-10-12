using System.Globalization;

namespace LaunchPad.Ignite.Core.Parsing;

public static class MathExpressionParser
{
    public static bool TryParse(string commandText, out string expression)
    {
        expression = string.Empty;
        if (commandText.Length == 0 || commandText[0] != '=')
        {
            return false;
        }

        expression = commandText[1..].TrimStart();
        return true;
    }

    public static bool TryEvaluate(string expression, out double value)
    {
        value = 0;
        try
        {
            var parser = new Parser(expression);
            value = parser.Parse();
            return double.IsFinite(value);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed class Parser(string expression)
    {
        private int _position;

        public double Parse()
        {
            var value = ParseExpression();
            SkipWhitespace();
            if (_position != expression.Length)
            {
                throw new FormatException();
            }

            return value;
        }

        private double ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (TryConsume('+'))
                {
                    value += ParseTerm();
                }
                else if (TryConsume('-'))
                {
                    value -= ParseTerm();
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParseTerm()
        {
            var value = ParseUnary();
            while (true)
            {
                SkipWhitespace();
                if (TryConsume('*'))
                {
                    value *= ParseUnary();
                }
                else if (TryConsume('/'))
                {
                    value /= ParseUnary();
                }
                else if (TryConsume('%'))
                {
                    value %= ParseUnary();
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParseUnary()
        {
            SkipWhitespace();
            if (TryConsume('+'))
            {
                return ParseUnary();
            }

            if (TryConsume('-'))
            {
                return -ParseUnary();
            }

            return ParsePower();
        }

        private double ParsePower()
        {
            var value = ParsePrimary();
            SkipWhitespace();
            return TryConsume('^') ? Math.Pow(value, ParseUnary()) : value;
        }

        private double ParsePrimary()
        {
            SkipWhitespace();
            if (TryConsume('('))
            {
                var value = ParseExpression();
                SkipWhitespace();
                if (!TryConsume(')'))
                {
                    throw new FormatException();
                }

                return value;
            }

            return ParseNumber();
        }

        private double ParseNumber()
        {
            var start = _position;
            var hasDigits = ConsumeDigits();
            if (TryConsume('.'))
            {
                hasDigits |= ConsumeDigits();
            }

            if (!hasDigits)
            {
                throw new FormatException();
            }

            if (TryConsume('e') || TryConsume('E'))
            {
                if (!TryConsume('+'))
                {
                    TryConsume('-');
                }

                if (!ConsumeDigits())
                {
                    throw new FormatException();
                }
            }

            if (!double.TryParse(expression.AsSpan(start, _position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !double.IsFinite(value))
            {
                throw new FormatException();
            }

            return value;
        }

        private bool ConsumeDigits()
        {
            var start = _position;
            while (_position < expression.Length && char.IsAsciiDigit(expression[_position]))
            {
                _position++;
            }

            return _position > start;
        }

        private bool TryConsume(char character)
        {
            if (_position >= expression.Length || expression[_position] != character)
            {
                return false;
            }

            _position++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < expression.Length && char.IsWhiteSpace(expression[_position]))
            {
                _position++;
            }
        }
    }
}
