using UnityEngine;

namespace LightsOut
{
    public enum LightKind { Normal, Broken, Reignite, Cursed, Golden }
    public static class LightsOutStyle
    {
        public static Color Hex(string code) { ColorUtility.TryParseHtmlString("#" + code, out Color color); return color; }
        public static string Format(double value)
        {
            if (value >= 1e12) return "₩ " + (value / 1e12).ToString("0.##") + "조";
            if (value >= 1e8) return "₩ " + (value / 1e8).ToString("0.##") + "억";
            if (value >= 1e4) return "₩ " + (value / 1e4).ToString("0.##") + "만";
            return "₩ " + value.ToString("N0");
        }
    }
}
