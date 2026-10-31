using System;
using NecronomiDom.Css.Selectors;

namespace NecronomiDom.Css.Cascade
{
    /// <summary>CSS Cascade 5 § 6.1 origin, importance, layer, specificity and source-order comparison.</summary>
    public readonly record struct CascadePriority(
        CascadeOrigin Origin, bool Important, int Layer, bool Inline,
        Specificity Specificity, int ScopeDistance, long Order, int EncapsulationDepth = 0) : IComparable<CascadePriority>
    {
        public int CompareTo(CascadePriority other)
        {
            int result = OriginRank.CompareTo(other.OriginRank);
            if (result != 0) return result;
            result = (Important ? EncapsulationDepth : -EncapsulationDepth)
                .CompareTo(other.Important ? other.EncapsulationDepth : -other.EncapsulationDepth);
            if (result != 0) return result;
            result = Inline.CompareTo(other.Inline);
            if (result != 0) return result;
            int layer = Important ? -Layer : Layer;
            int otherLayer = other.Important ? -other.Layer : other.Layer;
            result = layer.CompareTo(otherLayer);
            if (result != 0) return result;
            result = Specificity.A.CompareTo(other.Specificity.A);
            if (result != 0) return result;
            result = Specificity.B.CompareTo(other.Specificity.B);
            if (result != 0) return result;
            result = Specificity.C.CompareTo(other.Specificity.C);
            if (result != 0) return result;
            result = other.ScopeDistance.CompareTo(ScopeDistance);
            return result != 0 ? result : Order.CompareTo(other.Order);
        }

        private int OriginRank => Important
            ? Origin switch { CascadeOrigin.UserAgent => 7, CascadeOrigin.User => 6, _ => 5 }
            : Origin switch { CascadeOrigin.UserAgent => 0, CascadeOrigin.User => 1, _ => 2 };
    }

    /// <summary>CSS Cascade 5 stylesheet origins. Animation and transition values are supplied by the animation pipeline.</summary>
    public enum CascadeOrigin { UserAgent, User, Author }
}
