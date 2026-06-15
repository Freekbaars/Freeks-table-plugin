using System.Text.RegularExpressions;

namespace RhinoTable.Core.Layout
{
    public static class DrawHelper
    {
        public static string BuildRichText(string raw, string fontName, double fontSize, bool bold = false, bool italic = false)
        {
            double subSupSize = fontSize * 0.6;
            int subSupFontSize = (int)(subSupSize * 2);
            int baseFontSize = (int)(fontSize * 2);

            string processed = Regex.Replace(raw, @"\^\{([^}]*)\}",
                m => $@"{{\super\fs{subSupFontSize} {m.Groups[1].Value}}}");
            processed = Regex.Replace(processed, @"_\{([^}]*)\}",
                m => $@"{{\sub\fs{subSupFontSize} {m.Groups[1].Value}}}");

            string boldTag   = bold   ? @"\b"  : string.Empty;
            string italicTag = italic ? @"\i"  : string.Empty;

            return $@"{{\rtf1\ansi\deff0{{\fonttbl{{\f0 {fontName};}}}}
\f0\fs{baseFontSize}{boldTag}{italicTag} {processed}}}";
        }
    }
}
