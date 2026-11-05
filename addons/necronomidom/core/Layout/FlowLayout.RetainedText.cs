using System;
using System.Collections.Generic;
using NecronomiDom.Dom;
using NecronomiDom.Paint;

namespace NecronomiDom.Layout;

/// <summary>A numeric text replacement against one completed, unchanged layout generation.</summary>
public readonly record struct NumericTextUpdate(Element Element, int Generation, int Index, PaintRun Run, BeginRectClipRun? Clip);

public sealed partial class FlowLayout
{
    private readonly Dictionary<Element, int> _numericTextSlots = new();
    private int _numericTextGeneration;
    private long _numericStyleGeneration;

    private void ResetNumericText()
    {
        _numericTextGeneration++;
        _numericTextSlots.Clear();
    }

    private void CaptureNumericTextSlots()
    {
        _numericStyleGeneration = _styles.RestyleGeneration;
        for (int i = 0; i < _result.Runs.Count; i++)
        {
            var text = TextCarrier(_result.Runs[i]);
            if (text?.Owner is not { } owner) continue;
            if (!_numericTextSlots.TryAdd(owner, i)) _numericTextSlots[owner] = -1;
        }
    }

    private static TextRun? TextCarrier(PaintRun run) => run switch
    {
        TextRun text => text,
        TextEffectsRun effects => effects.Text,
        _ => null
    };

    /// <summary>Retain only a complete single-node line whose ASCII digits changed, with identical
    /// canonical font metrics and every shaping boundary. This preserves wrapping, intrinsic widths,
    /// flex sizing, selection geometry and bidi classes; all other content uses normal layout.</summary>
    public bool TryPrepareNumericTextUpdate(Element element, out NumericTextUpdate update)
    {
        update = default;
        if (_styles.ValueContext != null || _styles.ContainerEnvironment != null || !element.IsConnected ||
            !_numericTextSlots.TryGetValue(element, out int index) || index <= 0 ||
            element.FirstChild is not Text node || !ReferenceEquals(node, element.LastChild)) return false;
        var before = TextCarrier(_result.Runs[index])!;
        string next = node.Data;
        if (before.TextControl != null || !NumericReplacement(before.Text, next)) return false;
        var fragment = before.InlineFragment;
        if (fragment != null)
        {
            if (fragment.Start != 0 || fragment.Length != before.Text.Length || before.ShapingText != before.Text ||
                _result.Runs[index - 1] is not BeginRectClipRun ||
                index + 1 >= _result.Runs.Count || _result.Runs[index + 1] is not EndRectClipRun) return false;
            for (int i = 0; i < fragment.SourceOffsets.Count; i++)
                if (fragment.SourceOffsets[i] != i) return false;
        }
        else if (before.ShapingText != null) return false;
        var style = _styles.GetComputedStyle(element);
        if (_styles.RestyleGeneration != _numericStyleGeneration) return false;
        if (TextFontSize(style) != before.FontSize || style.GetPropertyValue("direction") != "ltr") return false;
        var shaping = TextShaping(style, before.FontSize);
        if (shaping.Shaper is not IInlineTextShaper shaper || shaping.Family != before.FontFamily ||
            shaping.Weight != before.FontWeight || shaping.Spacing != before.LetterSpacing || shaping.Italic != before.FontItalic) return false;
        var priorMetrics = shaper.Measure(before.Text, before.FontSize, shaping.Weight, shaping.Spacing, shaping.Italic);
        var nextMetrics = shaper.Measure(next, before.FontSize, shaping.Weight, shaping.Spacing, shaping.Italic);
        if (priorMetrics != nextMetrics) return false;
        for (int i = 1; i < next.Length; i++)
            if (shaper.InlineAdvance(before.Text, i, before.FontSize, shaping.Weight, shaping.Spacing, shaping.Italic) !=
                shaper.InlineAdvance(next, i, before.FontSize, shaping.Weight, shaping.Spacing, shaping.Italic)) return false;
        var replacement = before with
        {
            Text = next, ShapingText = fragment == null ? null : next,
            InlineFragment = fragment == null ? null : fragment with { Node = node }
        };
        BeginRectClipRun? clip = null;
        if (fragment != null)
        {
            var effects = _result.Runs[index] is TextEffectsRun decorated ? decorated.Effects : default;
            Rect ink = shaper.InlineInkBounds(next, before.FontSize, shaping.Weight, shaping.Spacing, shaping.Italic);
            clip = new BeginRectClipRun(InlineInkClipBounds(ink, effects, before.X, before.Baseline,
                0, nextMetrics.Width, true, true));
        }
        update = new(element, _numericTextGeneration, index,
            _result.Runs[index] is TextEffectsRun wrapper ? wrapper with { Text = replacement } : replacement, clip);
        return true;
    }

    private static bool NumericReplacement(string before, string next)
    {
        if (before.Length == 0 || before.Length != next.Length) return false;
        for (int i = 0; i < next.Length; i++)
        {
            if (before[i] < ' ' || before[i] > '~' || next[i] < ' ' || next[i] > '~') return false;
            if (before[i] != next[i] && (before[i] is < '0' or > '9' || next[i] is < '0' or > '9')) return false;
        }
        return true;
    }

    public bool ApplyNumericTextUpdates(ReadOnlySpan<NumericTextUpdate> updates)
    {
        foreach (var update in updates)
            if (update.Generation != _numericTextGeneration || !_numericTextSlots.TryGetValue(update.Element, out int index) ||
                index != update.Index) throw new InvalidOperationException("Numeric text update belongs to a different layout generation.");
        foreach (var update in updates)
        {
            if (update.Clip != null) _result.Runs[update.Index - 1] = update.Clip;
            _result.Runs[update.Index] = update.Run;
        }
        return updates.Length != 0;
    }
}
