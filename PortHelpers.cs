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
        public static readonly string[] Fundamentals = { "Integer", "Char", "Float", "Bit", "Any" };

        public static string Normalize(string? s) {
            if (string.IsNullOrWhiteSpace(s)) return "Integer";
            s = s.Trim();

            // Canonicalize common aliases (case-insensitive)
            var k = s.ToLowerInvariant();
            return k switch {
                "int" or "i32" or "integer" => "Integer",
                "float" or "f32" => "Float",
                "bool" or "boolean" or "bit" => "Bit",
                "char" or "character" => "Char",
                "any" => "Any",
                _ => s          // composite or custom, keep verbatim
            };
        }

        public static string Short(string t) => t switch {
            "Integer" => "Int",
            "Float" => "F32",
            "Bit" => "Bit",
            "Char" => "Char",
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
            "Integer" => Color.FromArgb(255, 165, 90),
            "Float" => Color.FromArgb(90, 170, 255),
            "Bit" => Color.FromArgb(100, 200, 140),
            "Char" => Color.FromArgb(200, 120, 220),
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