using System;
using System.Collections.Generic;
using System.Globalization;
using NecronomiDom.Css.Selectors;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.Media
{
    /// <summary>Media Queries 4/5 and CSS Conditional 3 environment, supplied by the document host in CSS pixels.</summary>
    public sealed class MediaEnvironment
    {
        public string Type { get; set; } = "screen";
        public double Width { get; set; } = 1920;
        public double Height { get; set; } = 1080;
        public double Resolution { get; set; } = 1;
        public double RootFontSize { get; set; } = 16;
        /// <summary>Real initial font/viewport unit metrics, e.g. ex/ch, supplied by the host.</summary>
        public Func<string, double?>? UnitScale { get; set; }
        private readonly Dictionary<string, string> _features = new(StringComparer.OrdinalIgnoreCase)
        {
            ["prefers-reduced-motion"] = "no-preference", ["prefers-contrast"] = "no-preference",
            ["prefers-color-scheme"] = "light", ["prefers-reduced-transparency"] = "no-preference",
            ["forced-colors"] = "none", ["pointer"] = "fine", ["any-pointer"] = "fine", ["hover"] = "hover",
            ["any-hover"] = "hover", ["update"] = "fast", ["overflow-block"] = "scroll", ["overflow-inline"] = "scroll",
            ["color"] = "8", ["color-index"] = "0", ["monochrome"] = "0", ["grid"] = "0",
            ["-necronomidom-input"] = "keyboard", ["-necronomidom-platform"] = "desktop",
        };
        public IDictionary<string, string> Features => _features;
        public IDictionary<string, QueryCondition> CustomMedia { get; } = new Dictionary<string, QueryCondition>(StringComparer.Ordinal);
        /// <summary>Actual computed container value; null denotes unavailable or guaranteed-invalid custom property.</summary>
        public Func<string, string?>? ContainerStyle { get; set; }
        internal bool SameNonViewportInputs(MediaEnvironment other)
        {
            if (Type != other.Type || !Same(Resolution, other.Resolution) || !Same(RootFontSize, other.RootFontSize) ||
                !ReferenceEquals(UnitScale, other.UnitScale) || !ReferenceEquals(ContainerStyle, other.ContainerStyle) ||
                _features.Count != other._features.Count) return false;
            foreach (var pair in _features)
                if (!other._features.TryGetValue(pair.Key, out string? value) || pair.Value != value) return false;
            return true;
            static bool Same(double first, double second) =>
                BitConverter.DoubleToInt64Bits(first) == BitConverter.DoubleToInt64Bits(second);
        }
    }

    /// <summary>Media Queries 4 § 3 condition truth values and CSS Supports property/selector feature checks.</summary>
    public static partial class MediaEvaluator
    {
        public static bool Supports(QueryCondition condition) => Evaluate(condition, new MediaEnvironment());
        public static bool Matches(MediaQueryList list, MediaEnvironment environment)
            => Matches(list, environment, new HashSet<string>(StringComparer.Ordinal));
        internal static bool Matches(MediaQueryList list, MediaEnvironment environment, HashSet<string> customStack)
        {
            if (list.Queries.Count == 0) return true;
            for (int i = 0; i < list.Queries.Count; i++)
            {
                var query = list.Queries[i];
                if (!query.IsValid) continue;
                bool value = (query.Type == null || query.Type == "all" || query.Type.Equals(environment.Type, StringComparison.OrdinalIgnoreCase))
                    && (query.Condition == null || Evaluate(query.Condition, environment, customStack));
                if (query.Modifier == "not") value = !value;
                if (value) return true;
            }
            return false;
        }
        public static bool Evaluate(QueryCondition condition, MediaEnvironment environment)
            => Evaluate(condition, environment, new HashSet<string>(StringComparer.Ordinal));
        private static bool Evaluate(QueryCondition condition, MediaEnvironment env, HashSet<string> customStack)
        {
            switch (condition)
            {
                case BooleanCondition boolean: return boolean.Value;
                case MediaQueryCondition queries: return Matches(queries.Queries, env, customStack);
                case NotCondition not: return !Evaluate(not.Condition, env, customStack);
                case GroupCondition group: return Evaluate(group.Condition, env, customStack);
                case JunctionCondition junction:
                    bool conjunction = junction.Operator == "and";
                    for (int i = 0; i < junction.Conditions.Count; i++)
                        if (Evaluate(junction.Conditions[i], env, customStack) != conjunction) return !conjunction;
                    return conjunction;
                case FeatureCondition feature:
                    if (feature.Name.StartsWith("--", StringComparison.Ordinal))
                    {
                        if (!customStack.Add(feature.Name)) return false;
                        bool result = env.CustomMedia.TryGetValue(feature.Name, out var custom) && Evaluate(custom, env, customStack);
                        customStack.Remove(feature.Name);
                        return result;
                    }
                    return EvaluateFeature(feature, env);
                case DeclarationCondition declaration:
                    return CssPropertyParser.Parse(declaration.Property, CssParser.ParseComponentValueList(declaration.Value)) != null;
                case SelectorCondition selector: return selector.Selectors.Selectors.Count > 0 && !selector.Selectors.HasInvalidSelectors;
                case FunctionCondition function:
                    if (function.Name == "style") return EvaluateStyle(function, env);
                    return false;
                default: return false;
            }
        }

        private static bool EvaluateFeature(FeatureCondition feature, MediaEnvironment env)
        {
            string name = feature.Name;
            string comparison = feature.Comparison ?? "=";
            if (name.StartsWith("min-", StringComparison.Ordinal)) { name = name[4..]; comparison = ">="; }
            else if (name.StartsWith("max-", StringComparison.Ordinal)) { name = name[4..]; comparison = "<="; }
            string? actual = name switch
            {
                "width" or "inline-size" => env.Width.ToString("R", CultureInfo.InvariantCulture) + "px",
                "height" or "block-size" => env.Height.ToString("R", CultureInfo.InvariantCulture) + "px",
                "aspect-ratio" => (env.Width / env.Height).ToString("R", CultureInfo.InvariantCulture),
                "resolution" => env.Resolution.ToString("R", CultureInfo.InvariantCulture) + "dppx",
                "orientation" => env.Width > env.Height ? "landscape" : "portrait",
                _ => env.Features.TryGetValue(name, out string? value) ? value : null,
            };
            if (actual == null) return false;
            string? type = name switch
            {
                "width" or "height" or "inline-size" or "block-size" or "device-width" or "device-height" => "length",
                "resolution" => "resolution", "aspect-ratio" or "device-aspect-ratio" => "ratio",
                "color" or "color-index" or "monochrome" or "grid" => "integer", _ => null,
            };
            if (feature.Value == null) return type == null
                ? actual != "none" && actual != "0" && actual != "no-preference"
                : TryNumeric(actual, env, type, out double booleanValue) && booleanValue != 0;
            if (type == null) return comparison == "=" && feature.LeftValue == null && string.Equals(actual, feature.Value.Trim(), StringComparison.OrdinalIgnoreCase);
            if (!TryNumeric(actual, env, type, out double actualNumber) || !TryNumeric(feature.Value, env, type, out double expected)) return false;
            if (!Compare(actualNumber, comparison, expected)) return false;
            return feature.LeftValue == null || TryNumeric(feature.LeftValue, env, type, out double left) && Compare(left, feature.LeftComparison ?? "=", actualNumber);
        }
        private static bool Compare(double left, string op, double right) => op switch
        { "=" or ":" => left == right, "<" => left < right, ">" => left > right, "<=" => left <= right, ">=" => left >= right, _ => false };
        private static bool TryNumeric(string text, MediaEnvironment env, string type, out double value)
        {
            value = 0;
            text = text.Trim();
            if (type == "ratio")
            {
                int slash = text.IndexOf('/');
                if (slash >= 0 && double.TryParse(text[..slash], NumberStyles.Float, CultureInfo.InvariantCulture, out double numerator)
                    && double.TryParse(text[(slash + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double denominator) && numerator >= 0 && denominator > 0)
                { value = numerator / denominator; return true; }
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0;
            }
            if (!CssTypeParser.TryParse(type, CssParser.ParseComponentValueList(text), out CssValue? parsed)) return false;
            if (parsed is CssNumberValue number) value = number.Value;
            else if (parsed is CssDimensionValue dimension)
            {
                double? scale = UnitScale(dimension.Unit, env);
                if (!scale.HasValue) return false;
                value = dimension.Value * scale.Value;
            }
            else if (parsed is CssMathValue math)
            {
                if (!math.TryEvaluate(new CssMathEvaluationContext(unit => UnitScale(unit, env)), out value)) return false;
            }
            else return false;
            return double.IsFinite(value) && value >= 0;
        }
        private static double? UnitScale(string unit, MediaEnvironment env) => env.UnitScale?.Invoke(unit) ?? (unit.ToLowerInvariant() switch
        {
            "px" or "dppx" or "x" => 1, "em" or "rem" => env.RootFontSize,
            "in" => 96, "cm" => 96 / 2.54, "mm" => 96 / 25.4, "q" => 96 / 101.6,
            "pt" => 96 / 72.0, "pc" => 16, "dpi" => 1 / 96.0, "dpcm" => 2.54 / 96,
            "vw" or "vi" => env.Width / 100, "vh" or "vb" => env.Height / 100,
            "vmin" => System.Math.Min(env.Width, env.Height) / 100, "vmax" => System.Math.Max(env.Width, env.Height) / 100,
            _ => null,
        });
    }
}
