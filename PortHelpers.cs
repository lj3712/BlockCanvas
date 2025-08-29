using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace BlockCanvas {
    public enum PortSide { Input, Output }

    public static class TypeUtil {
        public static readonly string[] Fundamentals = { "Bit", "Any" };

        public static string Normalize(string? s) {
            if (string.IsNullOrWhiteSpace(s)) return "Bit";
            s = s.Trim();

            // Canonicalize common aliases (case-insensitive)
            var k = s.ToLowerInvariant();
            return k switch {
                "bool" or "boolean" or "bit" => "Bit",
                "any" => "Any",
                _ => "Bit"          // everything else defaults to Bit
            };
        }

        public static string Short(string t) => t switch {
            "Bit" => "Bit",
            "Any" => "Any",
            _ => t // composite names already short/meaningful
        };

        public static bool Compatible(string outT, string inT) {
            // "Any" type (used by END blocks) can accept any input
            if (string.Equals(inT, "Any", StringComparison.Ordinal)) return true;
            // Regular type compatibility check
            return string.Equals(outT, inT, StringComparison.Ordinal);
        }

        public static Color BaseColor(string t) => t switch {
            "Bit" => Color.FromArgb(100, 200, 140),
            "Any" => Color.FromArgb(180, 180, 180), // Gray for "Any" type
            _ => HashColor(t) // composite: stable pleasant color
        };

        private static Color HashColor(string s) {
            unchecked {
                int h = 23;
                foreach (var ch in s) h = h * 31 + ch;
                int r = 100 + (h & 0x7F);
                int g = 100 + ((h >> 7) & 0x7F);
                int b = 100 + ((h >> 14) & 0x7F);
                return Color.FromArgb(r, g, b);
            }
        }
    }
}