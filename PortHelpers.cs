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
        // Special bit length value indicating "any length" (for END blocks)
        public const int AnyLength = -1;

        // Convert old type names to bit lengths (for backward compatibility)
        public static int GetBitLength(string? typeName) {
            if (string.IsNullOrWhiteSpace(typeName)) return 1;
            
            var normalized = typeName.Trim().ToLowerInvariant();
            return normalized switch {
                "bit" or "bool" or "boolean" => 1,
                "any" => AnyLength,
                _ => 1 // Default to single bit
            };
        }

        // Convert bit length to display string
        public static string FormatBitLength(int bitLength) {
            return bitLength == AnyLength ? "[*]" : $"[{bitLength}]";
        }

        // Check if two ports are compatible (bit lengths must match, or one accepts any length)
        public static bool Compatible(int outputBitLength, int inputBitLength) {
            // "Any" length (used by END blocks) can accept any input
            if (inputBitLength == AnyLength) return true;
            // Lengths must match exactly
            return outputBitLength == inputBitLength;
        }

        // Get color based on bit length
        public static Color BaseColor(int bitLength) {
            if (bitLength == AnyLength) {
                return Color.FromArgb(180, 180, 180); // Gray for "any length"
            }
            
            // Generate colors based on bit length
            return bitLength switch {
                1 => Color.FromArgb(100, 200, 140),  // Green for single bit
                8 => Color.FromArgb(100, 140, 200),  // Blue for byte
                16 => Color.FromArgb(200, 140, 100), // Orange for word
                32 => Color.FromArgb(200, 100, 140), // Pink for dword
                _ => HashColor(bitLength)            // Generate color for other lengths
            };
        }

        private static Color HashColor(int bitLength) {
            unchecked {
                int h = bitLength * 31 + 23;
                int r = 100 + (h & 0x7F);
                int g = 100 + ((h >> 7) & 0x7F);
                int b = 100 + ((h >> 14) & 0x7F);
                return Color.FromArgb(r, g, b);
            }
        }
    }
}