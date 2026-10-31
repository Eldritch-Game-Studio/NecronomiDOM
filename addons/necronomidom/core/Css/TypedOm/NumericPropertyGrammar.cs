using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NecronomiDom.Css.Syntax;
using NecronomiDom.Css.Values;
using NecronomiDom.Css.Values.Grammar;
using NecronomiDom.Css.Values.Math;

namespace NecronomiDom.Css.TypedOm
{
    /// <summary>One-value Typed OM validation compiled from the shared webref production graph, not a second property table.</summary>
    internal sealed class NumericPropertyGrammar
    {
        private readonly record struct Range(double Min, double Max, bool Integer, bool Zero);
        private static readonly ConcurrentDictionary<(string Property, string Unit), NumericPropertyGrammar> Cache = new();
        private static readonly ConcurrentDictionary<(string Property, CssNumericType Type), NumericPropertyGrammar> MathCache = new();
        private readonly List<Range> _ranges = new();
        private readonly GrammarMatcher _references = new(Array.Empty<ComponentValue>(), "");
        private readonly HashSet<GrammarNode> _visiting = new();
        private readonly Dictionary<GrammarNode, bool> _nullable = new();
        private readonly CSSUnitValue _unit;
        private readonly CssNumericType? _mathType;
        private NumericPropertyGrammar(PropertyDefinition property, CSSUnitValue unit)
        {
            _unit = unit;
            if (!property.IsShorthand) Collect(property.Grammar, double.NegativeInfinity, double.PositiveInfinity);
        }
        private NumericPropertyGrammar(PropertyDefinition property, CssNumericType type)
        {
            _mathType = type;
            if (!property.IsShorthand) Collect(property.Grammar, double.NegativeInfinity, double.PositiveInfinity);
        }
        internal static bool Accepts(string property, CSSNumericValue value)
        {
            var definition = CssPropertyRegistry.Get(property);
            if (definition == null || !value.IsInitialized) return false;
            var key = (definition.Name, value.Type);
            if (!MathCache.TryGetValue(key, out var grammar))
                grammar = MathCache.GetOrAdd(key, new NumericPropertyGrammar(definition, value.Type));
            return grammar._ranges.Count > 0;
        }
        internal static bool Accepts(string property, CSSUnitValue value)
        {
            var definition = CssPropertyRegistry.Get(property);
            if (definition == null) return false;
            var key = (definition.Name, value.Unit);
            if (!Cache.TryGetValue(key, out var grammar))
            {
                grammar = new NumericPropertyGrammar(definition, value);
                grammar = Cache.GetOrAdd(key, grammar);
            }
            double canonical = CssNumericRange.Canonical(value.Value, value.NumericUnit);
            for (int i = 0; i < grammar._ranges.Count; i++)
            {
                Range range = grammar._ranges[i];
                if (canonical >= range.Min && canonical <= range.Max && (!range.Integer || value.Value == System.Math.Truncate(value.Value))
                    && (!range.Zero || value.Value == 0)) return true;
            }
            return false;
        }
        private void Collect(GrammarNode node, double min, double max)
        {
            if (!_visiting.Add(node)) return;
            try
            {
                switch (node)
                {
                    case TypeNode type:
                        min = System.Math.Max(min, type.Min); max = System.Math.Min(max, type.Max);
                        if (!type.IsProperty && IsNumericPrimitive(type.Name))
                        {
                            if (Primitive(type.Name, out bool integer, out bool zero)) _ranges.Add(new Range(min, max, integer, zero));
                        }
                        else foreach (GrammarNode reference in _references.ResolveTypedReference(type)) Collect(reference, min, max);
                        break;
                    case MultiplierNode multiple when multiple.Min <= 1 && multiple.Max >= 1: Collect(multiple.Child, min, max); break;
                    case RequiredNode required: Collect(required.Child, min, max); break;
                    case CombinationNode combination:
                        for (int i = 0; i < combination.Items.Count; i++)
                        {
                            bool permitted = true;
                            if (combination.Kind is CombinatorKind.Sequence or CombinatorKind.AllOf)
                                for (int j = 0; j < combination.Items.Count; j++)
                                    if (i != j && !Nullable(combination.Items[j])) { permitted = false; break; }
                            if (permitted) Collect(combination.Items[i], min, max);
                        }
                        break;
                }
            }
            finally { _visiting.Remove(node); }
        }
        private bool Primitive(string name, out bool integer, out bool zero)
        {
            integer = name == "integer"; zero = name == "zero";
            if (_mathType is { } type)
                return name switch
                {
                    "number" or "integer" => type.IsNumber,
                    "number-percentage" => type.MatchesNumber(CssNumericDimension.Number) || type.IsPercentage,
                    "percentage" => type.IsPercentage,
                    "length" => type.IsDimension(CssNumericDimension.Length),
                    "length-percentage" => type.Matches(CssNumericDimension.Length, true),
                    "angle" => type.IsDimension(CssNumericDimension.Angle),
                    "time" => type.IsDimension(CssNumericDimension.Time),
                    "frequency" => type.IsDimension(CssNumericDimension.Frequency),
                    "resolution" => type.IsDimension(CssNumericDimension.Resolution),
                    "flex" => type.IsDimension(CssNumericDimension.Flex),
                    _ => false,
                };
            CssMathUnits.TryGet(_unit.NumericUnit, out var dimension, out _);
            return name switch
            {
                "number" or "integer" or "zero" or "number-token" => dimension == CssNumericDimension.Number,
                "percentage" => dimension == CssNumericDimension.Percentage,
                "length" => dimension == CssNumericDimension.Length,
                "length-percentage" => dimension is CssNumericDimension.Length or CssNumericDimension.Percentage,
                "angle" => dimension == CssNumericDimension.Angle,
                "time" => dimension == CssNumericDimension.Time,
                "frequency" => dimension == CssNumericDimension.Frequency,
                "resolution" => dimension == CssNumericDimension.Resolution,
                "flex" => dimension == CssNumericDimension.Flex,
                "dimension" or "dimension-token" => dimension is not CssNumericDimension.Number and not CssNumericDimension.Percentage,
                _ => false,
            };
        }
        private bool Nullable(GrammarNode node)
        {
            if (_nullable.TryGetValue(node, out bool known)) return known;
            _nullable[node] = false;
            bool result = node switch
            {
                MultiplierNode multiple => multiple.Min == 0 || Nullable(multiple.Child),
                CombinationNode combination when combination.Kind is CombinatorKind.Sequence or CombinatorKind.AllOf => EveryNullable(combination.Items),
                CombinationNode combination when combination.Kind == CombinatorKind.OneOf => AnyNullable(combination.Items),
                TypeNode type => ReferenceNullable(type),
                _ => false,
            };
            _nullable[node] = result;
            return result;
        }
        private bool EveryNullable(IReadOnlyList<GrammarNode> nodes) { foreach (var node in nodes) if (!Nullable(node)) return false; return true; }
        private bool AnyNullable(IReadOnlyList<GrammarNode> nodes) { foreach (var node in nodes) if (Nullable(node)) return true; return false; }
        private bool ReferenceNullable(TypeNode type) { foreach (var reference in _references.ResolveTypedReference(type)) if (Nullable(reference)) return true; return false; }
        private static bool IsNumericPrimitive(string name) => name is "number" or "integer" or "zero" or "number-token" or "percentage"
            or "length" or "length-percentage" or "angle" or "time" or "frequency" or "resolution" or "flex" or "dimension" or "dimension-token";
    }
}
